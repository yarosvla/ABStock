using ABStock.Shared;

namespace ABStock.Application.MarketHistory;

public interface ISimulationHistoryReader
{
    SimulationRunSummary? GetRun(Guid runId);

    SimulationHistoryOverview GetOverview(int recentRuns = 10);
}

public sealed record SimulationRunSummary(
    Guid RunId,
    string AssetName,
    AssetType AssetType,
    DateTimeOffset StartedAt,
    int TickCount,
    int TradeCount,
    decimal LastPrice);

public sealed record SimulationHistoryOverview(
    int RunCount,
    int DistinctAssetCount,
    int TotalTradeCount,
    int TotalTickCount,
    IReadOnlyList<SimulationActivityItem> RecentActivity);

public sealed record SimulationActivityItem(
    DateTimeOffset OccurredAt,
    string Title,
    string Detail);
