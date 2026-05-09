using ABStock.Agents.Models;

namespace ABStock.Agents;

public interface ITradeAgent
{
    AgentState State { get; }

    // TODO: add MarketSnapshot and NewsSignal as parameters
    AgentDecision Decide();
}
