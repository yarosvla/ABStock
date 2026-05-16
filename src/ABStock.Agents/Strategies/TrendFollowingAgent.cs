using ABStock.Agents.Models;

namespace ABStock.Agents.Strategies;

public class TrendFollowingAgent : AgentBase
{
    public TrendFollowingAgent(decimal initialCash)
        : base("TrendFollowing", AgentType.TrendFollowing, initialCash) { }

    // TODO
    public override AgentDecision Decide()
    {
        return HoldDecision("No market data available yet");
    }
}
