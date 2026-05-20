using ABStock.Agents.Models;
using ABStock.Shared;

namespace ABStock.Agents;

public interface ITradeAgent
{
    AgentState State { get; }

    AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal);
}
