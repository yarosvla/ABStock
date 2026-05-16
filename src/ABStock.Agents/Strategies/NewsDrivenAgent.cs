using ABStock.Agents.Models;

namespace ABStock.Agents.Strategies;

public class NewsDrivenAgent : AgentBase
{
    public NewsDrivenAgent(decimal initialCash)
        : base("NewsDriven", AgentType.NewsDriven, initialCash) { }

    // TODO
    public override AgentDecision Decide()
    {
        return HoldDecision("No news signal available yet");
    }
}
