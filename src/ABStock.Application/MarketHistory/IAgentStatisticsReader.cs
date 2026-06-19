using ABStock.Shared;

namespace ABStock.Application.MarketHistory;

public interface IAgentStatisticsReader
{
    AgentStatisticsReport? GetReport(Guid runId, string agentName);
}

public sealed record AgentStatisticsReport(
    string AgentName,
    IReadOnlyList<AgentTradeRecord> Trades,
    IReadOnlyList<AgentPricePoint> PriceSeries);

public sealed record AgentTradeRecord(
    DateTimeOffset ExecutedAt,
    OrderSide Side,
    decimal Price,
    decimal Quantity);

public sealed record AgentPricePoint(
    DateTimeOffset Time,
    decimal Price);
