namespace ABStock.Application.MarketHistory;

public interface ITradingSessionHistoryReader
{
    TradingSessionSummary? GetSession(Guid sessionId);

    IReadOnlyList<TradingSessionSummary> GetRecentSessions(int limit = 10);

    IReadOnlyList<SimulationRunSummary> GetAssetRuns(Guid assetId, int limit = 10);
}

public sealed record TradingSessionSummary(
    Guid SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    TimeSpan TickInterval,
    IReadOnlyList<SimulationRunSummary> Markets);
