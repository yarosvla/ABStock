using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class TrendFollowingAgent : AgentBase
{
    public TrendFollowingAgent(decimal initialCash)
        : base("TrendFollowing", AgentType.TrendFollowing, initialCash) { }

    // TODO: implement trend-following logic using snapshot.RecentPrices
    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        return HoldDecision("Not implemented yet");
    }
}
