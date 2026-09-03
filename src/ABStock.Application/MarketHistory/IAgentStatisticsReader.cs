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

/// <param name="TotalVolume">
/// Объём НАРАСТАЮЩИМ ИТОГОМ на момент тика — так его хранит рынок
/// (<c>MarketSnapshot.Volume</c>). Объём одной свечи получается разностью
/// соседних значений; хранить здесь уже разность нельзя, иначе прореживание
/// ряда молча теряло бы часть объёма.
/// </param>
public sealed record AgentPricePoint(
    DateTimeOffset Time,
    decimal Price,
    decimal TotalVolume);
