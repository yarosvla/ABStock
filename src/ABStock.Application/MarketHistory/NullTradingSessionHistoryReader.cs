namespace ABStock.Application.MarketHistory;

internal sealed class NullTradingSessionHistoryReader : ITradingSessionHistoryReader
{
    public TradingSessionSummary? GetSession(Guid sessionId) => null;

    public IReadOnlyList<TradingSessionSummary> GetRecentSessions(int limit = 10) => [];

    public IReadOnlyList<SimulationRunSummary> GetAssetRuns(Guid assetId, int limit = 10) => [];
}
