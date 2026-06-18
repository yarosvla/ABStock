namespace ABStock.Application.MarketHistory;

internal sealed class NullAgentStatisticsReader : IAgentStatisticsReader
{
    public AgentStatisticsReport? GetReport(Guid runId, string agentName) => null;
}
