using ABStock.Agents.Models;

namespace ABStock.Agents.Strategies;

public class CounterTrendAgent : AgentBase
{
    public CounterTrendAgent(decimal initialCash)
        : base("CounterTrend", AgentType.CounterTrend, initialCash) { }

    // TODO
    public override AgentDecision Decide()
    {
        return HoldDecision("No market data available yet");
    }
}
