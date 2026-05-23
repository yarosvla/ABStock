using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class CounterTrendAgent : AgentBase
{
    public CounterTrendAgent(decimal initialCash)
        : base("CounterTrend", AgentType.CounterTrend, initialCash) { }

    // TODO: implement counter-trend logic using snapshot.RecentPrices
    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        return HoldDecision("Not implemented yet");
    }
}
