using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents.Tests;

public sealed class MarketMakerAgentTests
{
    private const int LadderLevels = 4;

    private static MarketSnapshot CreateSnapshot(decimal lastPrice) =>
        new(lastPrice, BestBid: null, BestAsk: null, Volume: 0m, RecentPrices: [], RecentTrades: []);

    private static decimal GetPriceStep(decimal spreadPercent) =>
        Math.Max(spreadPercent / LadderLevels, 0.0025m);

    private static decimal GetBidPrice(decimal lastPrice, decimal spreadPercent, int level) =>
        lastPrice * (1 - GetPriceStep(spreadPercent) * level);

    private static decimal GetAskPrice(decimal lastPrice, decimal spreadPercent, int level) =>
        lastPrice * (1 + GetPriceStep(spreadPercent) * level);

    [Fact]
    public void Decide_PlacesBidAndAsk_WhenHasPositionAndCash()
    {
        var agent = new MarketMakerAgent(10000m);
        agent.State.Position = 5m;
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(6, decision.Orders.Count);
        Assert.Equal(4, decision.Orders.Count(o => o.Side == OrderSide.Buy));
        Assert.Equal(2, decision.Orders.Count(o => o.Side == OrderSide.Sell));
    }

    [Fact]
    public void Decide_BidAndAskAroundLastPrice()
    {
        var agent = new MarketMakerAgent(10000m, spreadPercent: 0.01m);
        agent.State.Position = 5m;
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        var bid = decision.Orders.First(o => o.Side == OrderSide.Buy);
        var ask = decision.Orders.First(o => o.Side == OrderSide.Sell);

        Assert.Equal(GetBidPrice(100m, 0.01m, 1), bid.Price);
        Assert.Equal(GetAskPrice(100m, 0.01m, 1), ask.Price);
    }

    [Fact]
    public void Decide_OnlyBid_WhenNoPosition()
    {
        var agent = new MarketMakerAgent(10000m);
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(LadderLevels, decision.Orders.Count);
        Assert.All(decision.Orders, order => Assert.Equal(OrderSide.Buy, order.Side));
    }

    [Fact]
    public void Decide_OnlyAsk_WhenNoCash()
    {
        var agent = new MarketMakerAgent(0m);
        agent.State.Position = 5m;
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(2, decision.Orders.Count);
        Assert.All(decision.Orders, order => Assert.Equal(OrderSide.Sell, order.Side));
    }

    [Fact]
    public void Decide_Hold_WhenNoCashAndNoPosition()
    {
        var agent = new MarketMakerAgent(0m);
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(TradeAction.Hold, decision.Action);
        Assert.Empty(decision.Orders);
    }

    [Fact]
    public void Decide_UsesCustomSpread()
    {
        var agent = new MarketMakerAgent(10000m, spreadPercent: 0.05m);
        agent.State.Position = 5m;
        var snapshot = CreateSnapshot(200m);

        var decision = agent.Decide(snapshot, null);

        var bid = decision.Orders.First(o => o.Side == OrderSide.Buy);
        var ask = decision.Orders.First(o => o.Side == OrderSide.Sell);

        Assert.Equal(GetBidPrice(200m, 0.05m, 1), bid.Price);
        Assert.Equal(GetAskPrice(200m, 0.05m, 1), ask.Price);
    }
}
