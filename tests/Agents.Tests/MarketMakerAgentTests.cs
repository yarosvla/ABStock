using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents.Tests;

public sealed class MarketMakerAgentTests
{
    private static MarketSnapshot CreateSnapshot(decimal lastPrice) =>
        new(lastPrice, BestBid: null, BestAsk: null, Volume: 0m, RecentPrices: [], RecentTrades: []);

    [Fact]
    public void Decide_PlacesBidAndAsk_WhenHasPositionAndCash()
    {
        var agent = new MarketMakerAgent(10000m);
        agent.State.Position = 5m;
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        Assert.Equal(2, decision.Orders.Count);
        Assert.Contains(decision.Orders, o => o.Side == OrderSide.Buy);
        Assert.Contains(decision.Orders, o => o.Side == OrderSide.Sell);
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

        Assert.Equal(99m, bid.Price);
        Assert.Equal(101m, ask.Price);
    }

    [Fact]
    public void Decide_OnlyBid_WhenNoPosition()
    {
        var agent = new MarketMakerAgent(10000m);
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Buy, decision.Orders[0].Side);
    }

    [Fact]
    public void Decide_OnlyAsk_WhenNoCash()
    {
        var agent = new MarketMakerAgent(0m);
        agent.State.Position = 5m;
        var snapshot = CreateSnapshot(100m);

        var decision = agent.Decide(snapshot, null);

        Assert.Single(decision.Orders);
        Assert.Equal(OrderSide.Sell, decision.Orders[0].Side);
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

        Assert.Equal(190m, bid.Price);
        Assert.Equal(210m, ask.Price);
    }
}
