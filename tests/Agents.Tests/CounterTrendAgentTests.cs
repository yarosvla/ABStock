using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents.Tests;

public sealed class CounterTrendAgentTests
{
    private static MarketSnapshot CreateSnapshot(
        IReadOnlyList<decimal> recentPrices,
        decimal lastPrice,
        decimal? bestBid = 99m,
        decimal? bestAsk = 101m) =>
        new(lastPrice, bestBid, bestAsk, Volume: 0m, recentPrices, RecentTrades: []);

    private static decimal[] Flat(int count, decimal value) =>
        Enumerable.Repeat(value, count).ToArray();

    [Fact]
    public void Decide_BuysDip_WhenPriceBelowBand()
    {
        var agent = new CounterTrendAgent(10000m);
        var snapshot = CreateSnapshot(recentPrices: Flat(10, 100m), lastPrice: 90m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Buy, decision.Action);
        Assert.Single(decision.Orders);
    }

    [Fact]
    public void Decide_SellsPeak_WhenPriceAboveBand()
    {
        var agent = new CounterTrendAgent(10000m, initialPosition: 10m);
        var snapshot = CreateSnapshot(recentPrices: Flat(10, 100m), lastPrice: 110m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Sell, decision.Action);
        Assert.Single(decision.Orders);
    }

    [Fact]
    public void Decide_Holds_WithinBand()
    {
        var agent = new CounterTrendAgent(10000m, initialPosition: 10m);
        var snapshot = CreateSnapshot(recentPrices: Flat(10, 100m), lastPrice: 100.5m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Theory]
    [InlineData(0, 0.02, 1)]
    [InlineData(10, 0, 1)]
    [InlineData(10, 1, 1)]
    [InlineData(10, 0.02, 0)]
    public void Constructor_Throws_OnInvalidParameters(int period, double deviationThreshold, int orderQuantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CounterTrendAgent(
                10000m,
                period: period,
                deviationThreshold: (decimal)deviationThreshold,
                orderQuantity: orderQuantity));
    }
}
