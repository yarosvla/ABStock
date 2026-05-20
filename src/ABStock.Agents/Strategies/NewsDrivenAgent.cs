using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class NewsDrivenAgent : AgentBase
{
    public NewsDrivenAgent(decimal initialCash)
        : base("NewsDriven", AgentType.NewsDriven, initialCash) { }

    // TODO: implement news-driven logic using newsSignal.Polarity and ImpactScore
    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        return HoldDecision("Not implemented yet");
    }
}
