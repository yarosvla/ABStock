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
}
