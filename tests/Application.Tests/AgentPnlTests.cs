using ABStock.Application.Accounts;
using ABStock.Application.Assets;
using ABStock.Application.Markets;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Tests;

public sealed class AgentPnlTests
{
    [Fact]
    public void InitialInventoryHasCostBasisAndDoesNotCreateProfit()
    {
        var fixture = new Fixture();
        var account = fixture.AddAgent("Investor", 500m, (fixture.First, 2m), (fixture.Second, 3m));

        Assert.Equal(200m, account.Positions[fixture.First].CostBasis);
        Assert.Equal(100m, account.Positions[fixture.First].AverageEntryPrice);
        Assert.Equal(600m, account.Positions[fixture.Second].CostBasis);
        Assert.Equal(0m, account.RealizedPnl);
        Assert.Equal(0m, account.UnrealizedPnl);
        Assert.Equal(0m, account.TotalPnl);
    }

    [Fact]
    public void PurchasesUseWeightedAverageCostAndCurrentMarketPrice()
    {
        var fixture = new Fixture();
        fixture.AddAgent("Buyer", 1_000m);
        fixture.AddAgent("Seller", 0m, (fixture.First, 4m));
        fixture.Trade(fixture.First, "Buyer", "Seller", 100m, 2m);
        fixture.Trade(fixture.First, "Buyer", "Seller", 120m, 2m);

        var account = fixture.Session.GetAgentAccount("Buyer");
        var position = account.Positions[fixture.First];
        Assert.Equal(4m, position.Quantity);
        Assert.Equal(440m, position.CostBasis);
        Assert.Equal(110m, position.AverageEntryPrice);
        Assert.Equal(0m, position.RealizedPnl);
        Assert.Equal(40m, position.UnrealizedPnl);
        Assert.Equal(40m, position.TotalPnl);
        Assert.Equal(account.PortfolioValue - account.InitialPortfolioValue, account.TotalPnl);
    }

    [Fact]
    public void PartialSaleSeparatesRealizedAndUnrealizedProfit()
    {
        var fixture = new Fixture();
        fixture.AddAgent("Investor", 500m, (fixture.First, 2m));
        fixture.AddAgent("Buyer", 1_000m);
        fixture.Trade(fixture.First, "Buyer", "Investor", 110m, 1m);

        var account = fixture.Session.GetAgentAccount("Investor");
        var position = account.Positions[fixture.First];
        Assert.Equal(1m, position.Quantity);
        Assert.Equal(100m, position.CostBasis);
        Assert.Equal(100m, position.AverageEntryPrice);
        Assert.Equal(10m, position.RealizedPnl);
        Assert.Equal(10m, position.UnrealizedPnl);
        Assert.Equal(20m, position.TotalPnl);
        Assert.Equal(610m, account.Cash);
        Assert.Equal(account.PortfolioValue - account.InitialPortfolioValue, account.TotalPnl);
    }

    [Fact]
    public void ClosingPositionClearsBasisAndReentryKeepsPreviouslyRealizedProfit()
    {
        var fixture = new Fixture();
        fixture.AddAgent("Investor", 0m, (fixture.First, 2m));
        fixture.AddAgent("Buyer", 1_000m);
        fixture.AddAgent("Supplier", 0m, (fixture.First, 1m));
        fixture.Trade(fixture.First, "Buyer", "Investor", 120m, 2m);
        var closed = fixture.Session.GetAgentAccount("Investor").Positions[fixture.First];
        Assert.Equal(0m, closed.CostBasis);
        Assert.Null(closed.AverageEntryPrice);
        Assert.Equal(40m, closed.RealizedPnl);
        Assert.Equal(0m, closed.UnrealizedPnl);

        fixture.Trade(fixture.First, "Investor", "Supplier", 110m, 1m);

        var account = fixture.Session.GetAgentAccount("Investor");
        var position = account.Positions[fixture.First];
        Assert.Equal(110m, position.CostBasis);
        Assert.Equal(110m, position.AverageEntryPrice);
        Assert.Equal(40m, position.TotalPnl);
        Assert.Equal(account.PortfolioValue - account.InitialPortfolioValue, account.TotalPnl);
    }

    [Fact]
    public void AssetProfitsAndLossesAreSeparateDespiteSharedCash()
    {
        var fixture = new Fixture();
        fixture.AddAgent("Investor", 0m, (fixture.First, 2m), (fixture.Second, 2m));
        fixture.AddAgent("Buyer", 1_000m);
        fixture.Trade(fixture.First, "Buyer", "Investor", 110m, 1m);
        fixture.Trade(fixture.Second, "Buyer", "Investor", 180m, 1m);

        var account = fixture.Session.GetAgentAccount("Investor");
        Assert.Equal(20m, account.Positions[fixture.First].TotalPnl);
        Assert.Equal(-40m, account.Positions[fixture.Second].TotalPnl);
        Assert.Equal(-10m, account.RealizedPnl);
        Assert.Equal(-10m, account.UnrealizedPnl);
        Assert.Equal(-20m, account.TotalPnl);
        Assert.Equal(account.PortfolioValue - account.InitialPortfolioValue, account.TotalPnl);
        Assert.Equal(1L, fixture.Session.GetMarket(fixture.First).Snapshot.TotalTradeCount);
        Assert.Equal(1L, fixture.Session.GetMarket(fixture.Second).Snapshot.TotalTradeCount);
    }

    [Fact]
    public void AddingNewInventoryAndCancellingOrdersDoesNotCreateProfit()
    {
        var fixture = new Fixture();
        fixture.Session.AddAgent(new("Investor", AgentType.MarketMaker, 500m) { InitialPosition = 2m });
        var order = new Order(Guid.NewGuid(), "Investor", OrderSide.Buy, OrderType.Limit, 99m, 1m, DateTimeOffset.UtcNow);
        fixture.Session.Submit(fixture.First, order);
        var before = fixture.Session.GetAgentAccount("Investor");
        var third = fixture.Catalog.Create(new(new AssetProfile("Third", AssetType.Stock, "Third asset", [], 1m), 300m));
        fixture.Session.AddMarket(third.AssetId);
        fixture.Session.CancelOrder(fixture.First, order.Id);

        var after = fixture.Session.GetAgentAccount("Investor");
        Assert.Equal(600m, after.Positions[third.AssetId].CostBasis);
        Assert.Equal(0m, after.Positions[third.AssetId].TotalPnl);
        Assert.Equal(before.TotalPnl, after.TotalPnl);
        Assert.Equal(0m, after.TotalPnl);
        Assert.Equal(before.Cash, after.Cash);
    }

    private sealed class Fixture
    {
        public InMemoryAssetCatalog Catalog { get; } = new();
        public ITradingSession Session { get; }
        public Guid First { get; }
        public Guid Second { get; }

        public Fixture()
        {
            First = Catalog.Create(new(new AssetProfile("First", AssetType.Stock, "First asset", [], 1m), 100m)).AssetId;
            Second = Catalog.Create(new(new AssetProfile("Second", AssetType.Stock, "Second asset", [], 1m), 200m)).AssetId;
            Session = new TradingSessionFactory(new MarketSessionFactory(Catalog, new ExchangeEngineFactory())).Create();
            Session.AddMarket(First);
            Session.AddMarket(Second);
        }

        public AgentAccountSnapshot AddAgent(string name, decimal cash, params (Guid AssetId, decimal Quantity)[] positions) =>
            Session.AddAgent(new(name, AgentType.TrendFollowing, cash)
            {
                InitialPositions = positions.ToDictionary(position => position.AssetId, position => position.Quantity)
            });

        public void Trade(Guid assetId, string buyer, string seller, decimal price, decimal quantity)
        {
            Session.Submit(assetId, new(Guid.NewGuid(), seller, OrderSide.Sell, OrderType.Limit, price, quantity, DateTimeOffset.UtcNow));
            var result = Session.Submit(assetId, new(Guid.NewGuid(), buyer, OrderSide.Buy, OrderType.Market, null, quantity, DateTimeOffset.UtcNow));
            Assert.Single(result.Result.Trades);
            Assert.Empty(result.Result.RejectedOrders);
        }
    }
}
