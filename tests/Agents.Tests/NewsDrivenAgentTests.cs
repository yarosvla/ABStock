using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents.Tests;

public sealed class NewsDrivenAgentTests
{
    private static MarketSnapshot CreateSnapshot(decimal? bestBid, decimal? bestAsk, decimal lastPrice = 100m) =>
        new(lastPrice, bestBid, bestAsk, Volume: 0m, RecentPrices: [], RecentTrades: []);

    private static NewsSignal News(SignalPolarity polarity) =>
        new(polarity, Confidence: 0.9m, ImpactScore: 0.5m, Explanation: "test");

    [Fact]
    public void Decide_Hold_WhenNoNews()
    {
        var agent = new NewsDrivenAgent(10000m, initialPosition: 5m);
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 101m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_MarketBuy_OnPositiveNews()
    {
        var agent = new NewsDrivenAgent(10000m);
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 101m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Positive));

        Assert.Equal(TradeAction.Buy, decision.Action);
        var order = Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(OrderType.Market, order.Type);
        Assert.Null(order.Price);
    }

    [Fact]
    public void Decide_MarketSell_OnNegativeNews()
    {
        var agent = new NewsDrivenAgent(10000m, initialPosition: 5m);
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 101m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Negative));

        Assert.Equal(TradeAction.Sell, decision.Action);
        var order = Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Sell, order.Side);
        Assert.Equal(OrderType.Market, order.Type);
        Assert.Null(order.Price);
    }

    [Fact]
    public void Decide_Hold_OnNeutralNews()
    {
        var agent = new NewsDrivenAgent(10000m, initialPosition: 5m);
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 101m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Neutral));

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_Hold_OnPositiveNews_WhenNoAsk()
    {
        var agent = new NewsDrivenAgent(10000m);
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: null);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Positive));

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_Hold_OnNegativeNews_WhenNoBid()
    {
        var agent = new NewsDrivenAgent(10000m, initialPosition: 5m);
        var snapshot = CreateSnapshot(bestBid: null, bestAsk: 101m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Negative));

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_Hold_OnPositiveNews_WhenInsufficientCash()
    {
        var agent = new NewsDrivenAgent(50m);
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 101m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Positive));

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_Hold_OnNegativeNews_WhenNoPosition()
    {
        var agent = new NewsDrivenAgent(10000m);
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 101m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Negative));

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_Hold_OnPositiveNews_WhenReservedCashLeavesInsufficientAvailable()
    {
        var agent = new NewsDrivenAgent(100m);
        agent.State.ReservedCash = 60m;
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 50m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Positive));

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_Hold_OnNegativeNews_WhenReservedPositionLeavesInsufficientAvailable()
    {
        var agent = new NewsDrivenAgent(10000m, initialPosition: 1m);
        agent.State.ReservedPosition = 1m;
        var snapshot = CreateSnapshot(bestBid: 99m, bestAsk: 101m);

        var decision = agent.Decide(snapshot, News(SignalPolarity.Negative));

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_ContextHoldsWhenNewsDoesNotMatchAssetProfile()
    {
        var agent = new NewsDrivenAgent(10_000m);
        var context = CreateContext(CreateSnapshot(99m, 101m), "GLEN");
        var news = News(SignalPolarity.Positive) with { MatchScore = 0.40m };

        var decision = agent.Decide(context, news);

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
        Assert.Contains("GLEN", decision.Explanation);
        Assert.Contains("совпадение", decision.Explanation);
    }

    [Fact]
    public void Decide_ContextUsesImpactDirectionInsteadOfGenericPolarity()
    {
        var agent = new NewsDrivenAgent(10_000m, initialPosition: 5m);
        var context = CreateContext(CreateSnapshot(99m, 101m), "GLEN");
        var news = News(SignalPolarity.Positive) with { ImpactScore = -1m, MatchScore = 1m };

        var decision = agent.Decide(context, news);

        Assert.Equal(TradeAction.Sell, decision.Action);
        var order = Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Sell, order.Side);
        Assert.Equal(1.5m, order.Quantity);
        Assert.Contains("GLEN", decision.Explanation);
    }

    [Fact]
    public void Decide_ContextScalesQuantityWithProfileConviction()
    {
        var context = CreateContext(CreateSnapshot(99m, 101m), "GLEN");
        var moderate = News(SignalPolarity.Positive) with { ImpactScore = 0.5m, MatchScore = 0.8m };
        var strong = News(SignalPolarity.Positive) with { ImpactScore = 2m, MatchScore = 1m };

        var moderateOrder = Assert.Single(new NewsDrivenAgent(10_000m).Decide(context, moderate).Orders);
        var strongOrder = Assert.Single(new NewsDrivenAgent(10_000m).Decide(context, strong).Orders);

        Assert.Equal(1m, moderateOrder.Quantity);
        Assert.Equal(2m, strongOrder.Quantity);
    }

    private static AgentMarketContext CreateContext(MarketSnapshot snapshot, string ticker)
    {
        var asset = new Asset(Guid.NewGuid(),
            new AssetProfile(ticker, AssetType.Stock, $"Profile for {ticker}.", [], 0.8m),
            snapshot.LastPrice, DateTimeOffset.UtcNow)
        {
            Ticker = ticker
        };

        return new AgentMarketContext(Guid.NewGuid(), asset.AssetId, snapshot,
            new AgentAccountSnapshot("NewsDriven", AgentType.NewsDriven, 10_000m, 0m, 10_000m,
                10_000m, 10_000m, new Dictionary<Guid, AgentPositionSnapshot>()))
        {
            Asset = asset
        };
    }
}
