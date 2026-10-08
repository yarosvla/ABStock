using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents.Tests;

public sealed class AssetAwareMomentumAgentTests
{
    [Fact]
    public void TrendFollowing_DifferentRiskProfilesChangeDecisionForSameMove()
    {
        var snapshot = Snapshot(100m, 100.10m);
        var bond = Asset("BOND", AssetType.Bond, newsSensitivity: 0m);
        var crypto = Asset("CRYP", AssetType.Crypto, newsSensitivity: 1m);

        var bondDecision = new TrendFollowingAgent(10_000m).Decide(Context(snapshot, bond), null);
        var cryptoDecision = new TrendFollowingAgent(10_000m).Decide(Context(snapshot, crypto), null);

        Assert.Equal(TradeAction.Buy, bondDecision.Action);
        Assert.Equal(TradeAction.Hold, cryptoDecision.Action);
        Assert.Contains("BOND", bondDecision.Explanation);
        Assert.Contains("CRYP", cryptoDecision.Explanation);
        Assert.Contains("порог", cryptoDecision.Explanation);
    }

    [Fact]
    public void CounterTrend_DifferentRiskProfilesChangeDecisionForSameMove()
    {
        var snapshot = Snapshot(100m, 99.90m);
        var bond = Asset("BOND", AssetType.Bond, newsSensitivity: 0m);
        var crypto = Asset("CRYP", AssetType.Crypto, newsSensitivity: 1m);

        var bondDecision = new CounterTrendAgent(10_000m).Decide(Context(snapshot, bond), null);
        var cryptoDecision = new CounterTrendAgent(10_000m).Decide(Context(snapshot, crypto), null);

        Assert.Equal(TradeAction.Buy, bondDecision.Action);
        Assert.Equal(TradeAction.Hold, cryptoDecision.Action);
        Assert.Contains("BOND", bondDecision.Explanation);
        Assert.Contains("CRYP", cryptoDecision.Explanation);
    }

    [Fact]
    public void TrendFollowing_GrowthPotentialChangesOrderQuantity()
    {
        var snapshot = Snapshot(100m, 101m);
        var defensive = Asset("LOW", AssetType.Stock, newsSensitivity: 0.5m, growthPotential: 0);
        var growth = Asset("HIGH", AssetType.Stock, newsSensitivity: 0.5m, growthPotential: 100);

        var defensiveOrder = Assert.Single(
            new TrendFollowingAgent(10_000m).Decide(Context(snapshot, defensive), null).Orders);
        var growthOrder = Assert.Single(
            new TrendFollowingAgent(10_000m).Decide(Context(snapshot, growth), null).Orders);

        Assert.True(growthOrder.Quantity > defensiveOrder.Quantity);
    }

    private static MarketSnapshot Snapshot(decimal previousPrice, decimal lastPrice) =>
        new(lastPrice, lastPrice - 0.1m, lastPrice + 0.1m, 0m, [previousPrice, lastPrice], []);

    private static Asset Asset(
        string ticker,
        AssetType type,
        decimal newsSensitivity,
        int? growthPotential = null) =>
        new(Guid.NewGuid(), new AssetProfile(ticker, type, $"Profile for {ticker}.", [], newsSensitivity),
            100m, DateTimeOffset.UtcNow)
        {
            Ticker = ticker,
            GrowthPotential = growthPotential
        };

    private static AgentMarketContext Context(MarketSnapshot snapshot, Asset asset) =>
        new(Guid.NewGuid(), asset.AssetId, snapshot,
            new AgentAccountSnapshot("Test", AgentType.TrendFollowing, 10_000m, 0m, 10_000m,
                10_000m, 10_000m, new Dictionary<Guid, AgentPositionSnapshot>()))
        {
            Asset = asset
        };
}
