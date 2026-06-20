using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents.Tests;

public sealed class TrendFollowingAgentTests
{
    private static MarketSnapshot CreateSnapshot(
        IReadOnlyList<decimal> recentPrices,
        decimal? bestBid = 99m,
        decimal? bestAsk = 101m,
        decimal lastPrice = 100m) =>
        new(lastPrice, bestBid, bestAsk, Volume: 0m, recentPrices, RecentTrades: []);

    private static decimal[] Rising(int count) =>
        Enumerable.Range(1, count).Select(value => (decimal)value).ToArray();

    private static decimal[] Falling(int count) =>
        Enumerable.Range(1, count).Select(value => (decimal)(count + 1 - value)).ToArray();

    [Fact]
    public void Decide_ProbesLiquidity_DuringWarmup()
    {
        var agent = new TrendFollowingAgent(10000m);
        var snapshot = CreateSnapshot(recentPrices: [100m, 100m]);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Buy, decision.Action);
        var order = Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Buy, order.Side);
        Assert.Equal(101m, order.Price);
    }

    [Fact]
    public void Decide_Buys_OnUptrend()
    {
        var agent = new TrendFollowingAgent(10000m);
        var snapshot = CreateSnapshot(recentPrices: Rising(12));

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Buy, decision.Action);
        var order = Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Buy, order.Side);
    }

    [Fact]
    public void Decide_Sells_OnDowntrend()
    {
        var agent = new TrendFollowingAgent(10000m, initialPosition: 10m);
        var snapshot = CreateSnapshot(recentPrices: Falling(12));

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Sell, decision.Action);
        var order = Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Sell, order.Side);
    }

    [Fact]
    public void Decide_Holds_OnDowntrend_WhenNoPosition()
    {
        var agent = new TrendFollowingAgent(10000m);
        var snapshot = CreateSnapshot(recentPrices: Falling(12));

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(3, 3, 1)]
    [InlineData(3, 10, 0)]
    public void Constructor_Throws_OnInvalidParameters(int shortPeriod, int longPeriod, int orderQuantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TrendFollowingAgent(10000m, shortPeriod: shortPeriod, longPeriod: longPeriod, orderQuantity: orderQuantity));
    }
}
