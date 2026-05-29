using ABStock.Shared;

namespace ABStock.Agents;

public interface IAgentFactory
{
    IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs);
}
