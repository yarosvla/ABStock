using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.Markets;
using ABStock.Exchange.Engine;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

public sealed class MarketSessionIntegrationTests
{
    [Fact]
    public void Factory_CreatesEmptySessionsWithDistinctIds()
    {
        var factory = new MarketSessionFactory(new InMemoryAssetCatalog(), new ExchangeEngineFactory());

        var first = factory.Create();
        var second = factory.Create();

        Assert.NotEqual(Guid.Empty, first.SessionId);
        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Empty(first.GetMarkets());
        Assert.Empty(second.GetMarkets());
    }

    [Fact]
    public void AddMarket_CreatesSeparateEnginesWithCatalogStartPrices()
    {
        var catalog = new InMemoryAssetCatalog();
        var first = CreateAsset(catalog, "Energy", 100m);
        var second = CreateAsset(catalog, "Metal", 200m);
        var exchanges = new RecordingExchangeEngineFactory();
        var session = new MarketSessionFactory(catalog, exchanges).Create();

        var firstState = session.AddMarket(first.AssetId);
        var secondState = session.AddMarket(second.AssetId);

        Assert.Equal(new[] { 100m, 200m }, exchanges.StartPrices);
        Assert.Equal(2, exchanges.Engines.Count);
        Assert.NotSame(exchanges.Engines[0], exchanges.Engines[1]);
        Assert.Equal(session.SessionId, firstState.SessionId);
        Assert.Equal(session.SessionId, secondState.SessionId);
        Assert.Equal(first.AssetId, firstState.AssetId);
        Assert.Equal(second.AssetId, secondState.AssetId);
        Assert.Equal(100m, firstState.Snapshot.LastPrice);
        Assert.Equal(200m, secondState.Snapshot.LastPrice);
        Assert.Equal(2, session.GetMarkets().Count);
        Assert.Empty(firstState.OrderBook.Bids);
        Assert.Empty(secondState.OrderBook.Asks);
    }

    [Fact]
    public void AddMarket_AfterTradingDoesNotResetExistingMarket()
    {
        var catalog = new InMemoryAssetCatalog();
        var first = CreateAsset(catalog, "Energy", 100m);
        var exchanges = new RecordingExchangeEngineFactory();
        var session = new MarketSessionFactory(catalog, exchanges).Create();
        session.AddMarket(first.AssetId);
        session.SubmitMany(first.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, 110m, 10m),
            LimitOrder("Seller", OrderSide.Sell, 100m, 4m)
        ]);
        var originalOrder = Assert.Single(session.GetOpenOrders(first.AssetId));
        var originalTrade = Assert.Single(session.GetMarket(first.AssetId).Snapshot.RecentTrades);
        var second = CreateAsset(catalog, "Metal", 200m);

        session.AddMarket(second.AssetId);
        var repeated = session.AddMarket(first.AssetId);

        Assert.Equal(2, exchanges.Engines.Count);
        Assert.Equal(105m, repeated.Snapshot.LastPrice);
        Assert.Equal(4m, repeated.Snapshot.Volume);
        Assert.Equal(originalTrade.Id, Assert.Single(repeated.Snapshot.RecentTrades).Id);
        Assert.Equal(originalOrder.Id, Assert.Single(session.GetOpenOrders(first.AssetId)).Id);
        Assert.Equal(6m, Assert.Single(session.GetOpenOrders(first.AssetId)).Quantity);
        Assert.Equal(200m, session.GetMarket(second.AssetId).Snapshot.LastPrice);
    }

    [Fact]
    public void AddMarket_RejectsUnknownAssetWithoutCreatingEngine()
    {
        var exchanges = new RecordingExchangeEngineFactory();
        var session = new MarketSessionFactory(new InMemoryAssetCatalog(), exchanges).Create();

        Assert.Throws<KeyNotFoundException>(() => session.AddMarket(Guid.NewGuid()));
        Assert.Empty(exchanges.Engines);
        Assert.Empty(session.GetMarkets());
    }

    [Fact]
    public void Operations_RejectEmptyAssetId()
    {
        var session = new MarketSessionFactory(new InMemoryAssetCatalog(), new ExchangeEngineFactory()).Create();
        var order = LimitOrder("Buyer", OrderSide.Buy, 100m);

        Assert.Throws<ArgumentException>(() => session.AddMarket(Guid.Empty));
        Assert.Throws<ArgumentException>(() => session.GetMarket(Guid.Empty));
        Assert.Throws<ArgumentException>(() => session.Submit(Guid.Empty, order));
        Assert.Throws<ArgumentException>(() => session.CancelOrder(Guid.Empty, order.Id));
        Assert.Throws<ArgumentException>(() => session.CancelOrdersByAgent(Guid.Empty, "Buyer"));
        Assert.Throws<ArgumentException>(() => session.GetOpenOrders(Guid.Empty));
        Assert.Empty(session.GetMarkets());
    }

    [Fact]
    public void Operations_DoNotImplicitlyCreateKnownOrUnknownMarkets()
    {
        var catalog = new InMemoryAssetCatalog();
        var known = CreateAsset(catalog, "Energy", 100m);
        var session = new MarketSessionFactory(catalog, new ExchangeEngineFactory()).Create();
        var order = LimitOrder("Buyer", OrderSide.Buy, 100m);

        foreach (var assetId in new[] { known.AssetId, Guid.NewGuid() })
        {
            Assert.Throws<KeyNotFoundException>(() => session.GetMarket(assetId));
            Assert.Throws<KeyNotFoundException>(() => session.Submit(assetId, order));
            Assert.Throws<KeyNotFoundException>(() => session.CancelOrder(assetId, order.Id));
            Assert.Throws<KeyNotFoundException>(() => session.CancelOrdersByAgent(assetId, "Buyer"));
            Assert.Throws<KeyNotFoundException>(() => session.GetOpenOrders(assetId));
        }

        Assert.Empty(session.GetMarkets());
    }

    [Fact]
    public void CrossingOrders_InDifferentMarketsDoNotMatch()
    {
        var (session, first, second) = CreateTwoMarkets();
        var buy = LimitOrder("Buyer", OrderSide.Buy, 150m, 10m);
        var sell = LimitOrder("Seller", OrderSide.Sell, 100m, 10m);

        var buyResult = session.Submit(first.AssetId, buy);
        var sellResult = session.Submit(second.AssetId, sell);

        Assert.Empty(buyResult.Result.Trades);
        Assert.Empty(sellResult.Result.Trades);
        Assert.Equal(OrderExecutionStatus.Open, Assert.Single(buyResult.Result.OrderReports).Status);
        Assert.Equal(OrderExecutionStatus.Open, Assert.Single(sellResult.Result.OrderReports).Status);
        Assert.Equal(buy.Id, Assert.Single(session.GetOpenOrders(first.AssetId)).Id);
        Assert.Equal(sell.Id, Assert.Single(session.GetOpenOrders(second.AssetId)).Id);
        Assert.All(session.GetMarkets(), state => Assert.Equal(0m, state.Snapshot.Volume));
        Assert.Null(session.GetMarket(first.AssetId).Snapshot.BestAsk);
        Assert.Null(session.GetMarket(second.AssetId).Snapshot.BestBid);
    }

    [Fact]
    public void SubmitMany_TradePartialFillAndHistoryBelongOnlyToTargetMarket()
    {
        var (session, first, second) = CreateTwoMarkets();
        var buy = LimitOrder("Buyer", OrderSide.Buy, 110m, 10m);
        var sell = LimitOrder("Seller", OrderSide.Sell, 100m, 4m);

        var submitted = session.SubmitMany(first.AssetId, [buy, sell]);

        Assert.Equal(session.SessionId, submitted.SessionId);
        Assert.Equal(first.AssetId, submitted.AssetId);
        var result = submitted.Result;
        var trade = Assert.Single(result.Trades);
        Assert.Equal(buy.Id, trade.BuyOrderId);
        Assert.Equal(sell.Id, trade.SellOrderId);
        Assert.Equal(105m, trade.Price);
        Assert.Equal(4m, trade.Quantity);
        Assert.Equal(4m, result.Snapshot.Volume);
        var buyReport = Assert.Single(result.OrderReports, report => report.Order?.Id == buy.Id);
        Assert.Equal(OrderExecutionStatus.PartiallyFilled, buyReport.Status);
        Assert.Equal(6m, buyReport.RemainingQuantity);
        var sellReport = Assert.Single(result.OrderReports, report => report.Order?.Id == sell.Id);
        Assert.Equal(OrderExecutionStatus.Filled, sellReport.Status);
        var other = session.GetMarket(second.AssetId);
        Assert.Equal(200m, other.Snapshot.LastPrice);
        Assert.Equal(0m, other.Snapshot.Volume);
        Assert.Equal(new[] { 200m }, other.Snapshot.RecentPrices);
        Assert.Empty(other.Snapshot.RecentTrades);
        Assert.Empty(other.OrderBook.Bids);
        Assert.Empty(other.OrderBook.Asks);
    }

    [Fact]
    public void TradesInBothMarkets_HaveSeparateVolumesPricesAndHistories()
    {
        var (session, first, second) = CreateTwoMarkets();
        var firstResult = session.SubmitMany(first.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, 110m, 4m),
            LimitOrder("Seller", OrderSide.Sell, 100m, 4m)
        ]);
        var secondResult = session.SubmitMany(second.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, 210m, 3m),
            LimitOrder("Seller", OrderSide.Sell, 200m, 3m)
        ]);

        var firstState = session.GetMarket(first.AssetId);
        var secondState = session.GetMarket(second.AssetId);
        Assert.Equal(4m, firstState.Snapshot.Volume);
        Assert.Equal(3m, secondState.Snapshot.Volume);
        Assert.Equal(new[] { 100m, 105m }, firstState.Snapshot.RecentPrices);
        Assert.Equal(new[] { 200m, 205m }, secondState.Snapshot.RecentPrices);
        Assert.Equal(Assert.Single(firstResult.Result.Trades).Id, Assert.Single(firstState.Snapshot.RecentTrades).Id);
        Assert.Equal(Assert.Single(secondResult.Result.Trades).Id, Assert.Single(secondState.Snapshot.RecentTrades).Id);
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderSide.Sell)]
    [InlineData(OrderSide.Sell, OrderSide.Buy)]
    public void MarketOrder_DoesNotUseLiquidityOfAnotherAsset(OrderSide side, OrderSide restingSide)
    {
        var (session, first, second) = CreateTwoMarkets();
        var resting = LimitOrder("Liquidity", restingSide, 100m, 5m);
        session.Submit(second.AssetId, resting);
        var market = new Order(Guid.NewGuid(), "Taker", side, OrderType.Market, null, 3m, DateTimeOffset.UtcNow);

        var result = session.Submit(first.AssetId, market);

        Assert.Empty(result.Result.Trades);
        Assert.Equal(OrderExecutionStatus.Expired, Assert.Single(result.Result.OrderReports).Status);
        Assert.Empty(session.GetOpenOrders(first.AssetId));
        Assert.Equal(5m, Assert.Single(session.GetOpenOrders(second.AssetId)).Quantity);
        Assert.All(session.GetMarkets(), state => Assert.Equal(0m, state.Snapshot.Volume));
    }

    [Theory]
    [InlineData(OrderSide.Buy, OrderSide.Sell)]
    [InlineData(OrderSide.Sell, OrderSide.Buy)]
    public void MarketOrder_ConsumesOnlyTargetLiquidity(OrderSide side, OrderSide restingSide)
    {
        var (session, first, second) = CreateTwoMarkets();
        session.Submit(first.AssetId, LimitOrder("Liquidity", restingSide, 100m, 5m));
        session.Submit(second.AssetId, LimitOrder("Liquidity", restingSide, 200m, 5m));
        var market = new Order(Guid.NewGuid(), "Taker", side, OrderType.Market, null, 3m, DateTimeOffset.UtcNow);

        var result = session.Submit(first.AssetId, market);

        Assert.Equal(100m, Assert.Single(result.Result.Trades).Price);
        Assert.Equal(3m, result.Result.Snapshot.Volume);
        Assert.Equal(OrderExecutionStatus.Filled, Assert.Single(result.Result.OrderReports).Status);
        Assert.Equal(2m, Assert.Single(session.GetOpenOrders(first.AssetId)).Quantity);
        Assert.Equal(5m, Assert.Single(session.GetOpenOrders(second.AssetId)).Quantity);
        Assert.Equal(0m, session.GetMarket(second.AssetId).Snapshot.Volume);
    }

    [Fact]
    public void SubmitMany_ReturnsAddressedAcceptedAndRejectedReports()
    {
        var (session, first, second) = CreateTwoMarkets();
        var valid = LimitOrder("Buyer", OrderSide.Buy, 99m, 2m);
        var invalid = LimitOrder("Buyer", OrderSide.Buy, 99m, 0m);

        var result = session.SubmitMany(first.AssetId, [valid, invalid]);

        Assert.Equal(first.AssetId, result.AssetId);
        Assert.Equal(session.SessionId, result.SessionId);
        Assert.Equal(valid.Id, Assert.Single(result.Result.AcceptedOrders).Id);
        Assert.Equal(invalid.Id, Assert.Single(result.Result.RejectedOrders).Order!.Id);
        Assert.Equal(OrderExecutionStatus.Rejected,
            Assert.Single(result.Result.OrderReports, report => report.Order?.Id == invalid.Id).Status);
        Assert.Equal(valid.Id, Assert.Single(session.GetOpenOrders(first.AssetId)).Id);
        Assert.Empty(session.GetOpenOrders(second.AssetId));
    }

    [Fact]
    public void CancelOrder_OnlyRemovesOrderFromAddressedMarket()
    {
        var (session, first, second) = CreateTwoMarkets();
        var firstOrder = LimitOrder("Buyer", OrderSide.Buy, 99m);
        var secondOrder = LimitOrder("Buyer", OrderSide.Buy, 199m);
        session.Submit(first.AssetId, firstOrder);
        session.Submit(second.AssetId, secondOrder);

        Assert.False(session.CancelOrder(second.AssetId, firstOrder.Id));
        Assert.Equal(firstOrder.Id, Assert.Single(session.GetOpenOrders(first.AssetId)).Id);
        Assert.True(session.CancelOrder(first.AssetId, firstOrder.Id));
        Assert.False(session.CancelOrder(first.AssetId, firstOrder.Id));
        Assert.Empty(session.GetOpenOrders(first.AssetId));
        Assert.Null(session.GetMarket(first.AssetId).Snapshot.BestBid);
        Assert.Equal(secondOrder.Id, Assert.Single(session.GetOpenOrders(second.AssetId)).Id);
    }

    [Fact]
    public void CancelOrdersByAgent_DoesNotCancelAgentsOrdersInAnotherMarket()
    {
        var (session, first, second) = CreateTwoMarkets();
        session.SubmitMany(first.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, 99m),
            LimitOrder("Buyer", OrderSide.Buy, 98m),
            LimitOrder("Other", OrderSide.Buy, 97m)
        ]);
        session.Submit(second.AssetId, LimitOrder("Buyer", OrderSide.Buy, 199m));

        Assert.Equal(2, session.CancelOrdersByAgent(first.AssetId, "Buyer"));
        Assert.Equal("Other", Assert.Single(session.GetOpenOrders(first.AssetId)).AgentName);
        Assert.Equal("Buyer", Assert.Single(session.GetOpenOrders(second.AssetId)).AgentName);
        Assert.Equal(0, session.CancelOrdersByAgent(first.AssetId, "Buyer"));
    }

    [Fact]
    public void GetMarket_RespectsRequestedBookDepth()
    {
        var (session, first, _) = CreateTwoMarkets();
        session.SubmitMany(first.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, 99m),
            LimitOrder("Buyer", OrderSide.Buy, 98m),
            LimitOrder("Seller", OrderSide.Sell, 101m),
            LimitOrder("Seller", OrderSide.Sell, 102m)
        ]);

        var state = session.GetMarket(first.AssetId, depth: 1);

        Assert.Equal(99m, Assert.Single(state.OrderBook.Bids).Price);
        Assert.Equal(101m, Assert.Single(state.OrderBook.Asks).Price);
        Assert.All(session.GetMarkets(depth: 1), market => Assert.True(market.OrderBook.Bids.Count <= 1));
        Assert.Equal(4, session.GetOpenOrders(first.AssetId).Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ReadMethods_RejectInvalidDepthEvenForEmptySession(int depth)
    {
        var session = new MarketSessionFactory(new InMemoryAssetCatalog(), new ExchangeEngineFactory()).Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.GetMarkets(depth));
        Assert.Throws<ArgumentOutOfRangeException>(() => session.GetMarket(Guid.NewGuid(), depth));
    }

    [Fact]
    public void ReadMethods_ReturnDetachedSnapshots()
    {
        var (session, first, _) = CreateTwoMarkets();
        var emptyState = session.GetMarket(first.AssetId);
        var marketsBefore = session.GetMarkets();
        var order = LimitOrder("Buyer", OrderSide.Buy, 99m);
        session.Submit(first.AssetId, order);
        var orders = session.GetOpenOrders(first.AssetId);

        session.CancelOrder(first.AssetId, order.Id);

        Assert.Empty(emptyState.OrderBook.Bids);
        Assert.All(marketsBefore, state => Assert.Empty(state.OrderBook.Bids));
        Assert.Equal(order.Id, Assert.Single(orders).Id);
        Assert.Empty(session.GetOpenOrders(first.AssetId));
        Assert.Throws<NotSupportedException>(() => ((ICollection<Order>)orders).Clear());
    }

    [Fact]
    public void Events_ContainMarketIdsAndReportsAndIgnoreUnchangedOperations()
    {
        var catalog = new InMemoryAssetCatalog();
        var first = CreateAsset(catalog, "Energy", 100m);
        var second = CreateAsset(catalog, "Metal", 200m);
        var session = new MarketSessionFactory(catalog, new ExchangeEngineFactory()).Create();
        var changes = new List<MarketState>();
        var submissions = new List<MarketSubmitResult>();
        session.OnMarketChanged += changes.Add;
        session.OnOrdersSubmitted += submissions.Add;
        session.AddMarket(first.AssetId);
        session.AddMarket(second.AssetId);
        session.AddMarket(first.AssetId);
        var order = LimitOrder("Buyer", OrderSide.Buy, 99m);

        var submitted = session.Submit(first.AssetId, order);
        session.GetMarket(first.AssetId);
        session.GetMarkets();
        session.GetOpenOrders(first.AssetId);
        Assert.False(session.CancelOrder(second.AssetId, order.Id));
        Assert.True(session.CancelOrder(first.AssetId, order.Id));
        Assert.Equal(0, session.CancelOrdersByAgent(first.AssetId, "Buyer"));

        Assert.Same(submitted, Assert.Single(submissions));
        Assert.Equal(OrderExecutionStatus.Open, Assert.Single(submissions[0].Result.OrderReports).Status);
        Assert.Equal(new[] { first.AssetId, second.AssetId, first.AssetId, first.AssetId },
            changes.Select(state => state.AssetId));
        Assert.All(changes, state => Assert.Equal(session.SessionId, state.SessionId));
        Assert.Empty(changes[^1].OrderBook.Bids);
    }

    [Fact]
    public void Events_IncludeRejectedOrderReports()
    {
        var (session, first, _) = CreateTwoMarkets();
        MarketSubmitResult? received = null;
        session.OnOrdersSubmitted += result => received = result;

        session.Submit(first.AssetId, LimitOrder("Buyer", OrderSide.Buy, 0m));

        Assert.NotNull(received);
        Assert.Equal(first.AssetId, received.AssetId);
        Assert.Equal(OrderExecutionStatus.Rejected, Assert.Single(received.Result.OrderReports).Status);
        Assert.Empty(session.GetOpenOrders(first.AssetId));
    }

    [Fact]
    public void Events_AreInvokedOutsideSessionLock()
    {
        var catalog = new InMemoryAssetCatalog();
        var asset = CreateAsset(catalog, "Energy", 100m);
        var session = new MarketSessionFactory(catalog, new ExchangeEngineFactory()).Create();
        session.OnMarketChanged += state =>
        {
            var read = Task.Run(() => session.GetMarket(state.AssetId));
            Assert.True(read.Wait(TimeSpan.FromSeconds(3)), "Market callback must not hold the session lock.");
            Assert.Equal(state.AssetId, read.Result.AssetId);
        };

        session.AddMarket(asset.AssetId);
        session.Submit(asset.AssetId, LimitOrder("Buyer", OrderSide.Buy, 99m));
    }

    [Fact]
    public void SubmitMany_InputEnumerationFailureDoesNotPartiallySubmitOrders()
    {
        var (session, first, _) = CreateTwoMarkets();

        Assert.Throws<InvalidOperationException>(() => session.SubmitMany(first.AssetId, ThrowingOrders()));
        Assert.Empty(session.GetOpenOrders(first.AssetId));
        Assert.Throws<ArgumentNullException>(() => session.SubmitMany(first.AssetId, null!));

        static IEnumerable<Order> ThrowingOrders()
        {
            yield return LimitOrder("Buyer", OrderSide.Buy, 99m);
            throw new InvalidOperationException("Broken input enumeration.");
        }
    }

    [Fact]
    public async Task ConcurrentSubmissionsAndReads_KeepMarketsIsolated()
    {
        var (session, first, second) = CreateTwoMarkets();
        var tasks = Enumerable.Range(0, 48).Select(index => Task.Run(() =>
        {
            var assetId = index % 2 == 0 ? first.AssetId : second.AssetId;
            var result = session.Submit(assetId, LimitOrder($"Buyer{index}", OrderSide.Buy, 99m));
            Assert.Equal(assetId, result.AssetId);
            Assert.Single(result.Result.AcceptedOrders);
            Assert.Equal(2, session.GetMarkets().Count);
            Assert.Equal(assetId, session.GetMarket(assetId).AssetId);
        }));

        await Task.WhenAll(tasks);

        Assert.Equal(24, session.GetOpenOrders(first.AssetId).Count);
        Assert.Equal(24, session.GetOpenOrders(second.AssetId).Count);
        Assert.All(session.GetMarkets(), state =>
        {
            Assert.Equal(0m, state.Snapshot.Volume);
            Assert.Equal(24m, Assert.Single(state.OrderBook.Bids).Quantity);
        });
    }

    [Fact]
    public void DependencyInjection_UsesSharedCatalogAndRegisteredExchangeFactory()
    {
        var exchanges = new RecordingExchangeEngineFactory();
        using var provider = new ServiceCollection()
            .AddSingleton<IExchangeEngineFactory>(exchanges)
            .AddABStockApplication()
            .BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var catalog = firstScope.ServiceProvider.GetRequiredService<IAssetCatalog>();
        var asset = CreateAsset(catalog, "Energy", 123m);
        var factory = firstScope.ServiceProvider.GetRequiredService<IMarketSessionFactory>();
        var session = secondScope.ServiceProvider.GetRequiredService<IMarketSessionFactory>().Create();

        session.AddMarket(asset.AssetId);

        Assert.Same(factory, secondScope.ServiceProvider.GetRequiredService<IMarketSessionFactory>());
        Assert.Equal(123m, Assert.Single(exchanges.StartPrices));
        Assert.Equal(asset.AssetId, Assert.Single(session.GetMarkets()).AssetId);
    }

    [Fact]
    public void NewSessionForSameAsset_DoesNotReusePreviousEngineOrTrades()
    {
        var catalog = new InMemoryAssetCatalog();
        var asset = CreateAsset(catalog, "Energy", 100m);
        var factory = new MarketSessionFactory(catalog, new ExchangeEngineFactory());
        var first = factory.Create();
        first.AddMarket(asset.AssetId);
        first.SubmitMany(asset.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, 110m, 4m),
            LimitOrder("Seller", OrderSide.Sell, 100m, 4m)
        ]);

        var second = factory.Create();
        var secondState = second.AddMarket(asset.AssetId);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(100m, secondState.Snapshot.LastPrice);
        Assert.Equal(0m, secondState.Snapshot.Volume);
        Assert.Empty(secondState.Snapshot.RecentTrades);
        Assert.Equal(4m, first.GetMarket(asset.AssetId).Snapshot.Volume);
    }

    private static (IMarketSession Session, Asset First, Asset Second) CreateTwoMarkets()
    {
        var catalog = new InMemoryAssetCatalog();
        var first = CreateAsset(catalog, "Energy", 100m);
        var second = CreateAsset(catalog, "Metal", 200m);
        var session = new MarketSessionFactory(catalog, new ExchangeEngineFactory()).Create();
        session.AddMarket(first.AssetId);
        session.AddMarket(second.AssetId);
        return (session, first, second);
    }

    private static Asset CreateAsset(IAssetCatalog catalog, string name, decimal startPrice) =>
        catalog.Create(new CreateAssetRequest(
            new AssetProfile(name, AssetType.Stock, $"Description of {name}.", [], 0.9m), startPrice));

    private static Order LimitOrder(string agentName, OrderSide side, decimal price, decimal quantity = 1m) =>
        new(Guid.NewGuid(), agentName, side, OrderType.Limit, price, quantity, DateTimeOffset.UtcNow);

    private sealed class RecordingExchangeEngineFactory : IExchangeEngineFactory
    {
        public List<decimal> StartPrices { get; } = [];

        public List<IExchangeEngine> Engines { get; } = [];

        public IExchangeEngine Create(decimal startPrice)
        {
            StartPrices.Add(startPrice);
            var engine = new ExchangeEngine(startPrice);
            Engines.Add(engine);
            return engine;
        }
    }
}
