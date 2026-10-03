using ABStock.Application.MarketHistory;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.MarketHistory;

internal sealed class EfTradingSessionHistoryReader(
    IDbContextFactory<AbStockDbContext> contextFactory,
    StorageInitializer initializer) : ITradingSessionHistoryReader
{
    public TradingSessionSummary? GetSession(Guid sessionId)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        var session = db.TradingSessions.AsNoTracking().FirstOrDefault(item => item.Id == sessionId);
        if (session is null)
        {
            return null;
        }

        var markets = RunHistoryProjection.SelectSummaries(db.SimulationRuns.AsNoTracking()
                .Where(run => run.Market != null && run.Market.SessionId == sessionId))
            .AsEnumerable().OrderBy(run => run.StartedAt).ThenBy(run => run.RunId).ToArray();
        return new TradingSessionSummary(session.Id, session.StartedAt, session.EndedAt,
            TimeSpan.FromTicks(session.TickIntervalTicks), Array.AsReadOnly(markets));
    }

    public IReadOnlyList<TradingSessionSummary> GetRecentSessions(int limit = 10)
    {
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        var sessions = db.TradingSessions.AsNoTracking().AsEnumerable()
            .OrderByDescending(session => session.StartedAt).ThenBy(session => session.Id)
            .Take(Math.Clamp(limit, 1, 50)).ToArray();
        var ids = sessions.Select(session => session.Id).ToArray();
        var markets = RunHistoryProjection.SelectSummaries(db.SimulationRuns.AsNoTracking()
                .Where(run => run.Market != null && ids.Contains(run.Market.SessionId)))
            .AsEnumerable().OrderBy(run => run.StartedAt).ThenBy(run => run.RunId).ToArray();
        return Array.AsReadOnly(sessions.Select(session => new TradingSessionSummary(
            session.Id, session.StartedAt, session.EndedAt, TimeSpan.FromTicks(session.TickIntervalTicks),
            Array.AsReadOnly(markets.Where(run => run.SessionId == session.Id).ToArray()))).ToArray());
    }

    public IReadOnlyList<SimulationRunSummary> GetAssetRuns(Guid assetId, int limit = 10)
    {
        if (assetId == Guid.Empty)
        {
            return [];
        }

        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        return Array.AsReadOnly(RunHistoryProjection.SelectSummaries(db.SimulationRuns.AsNoTracking()
                .Where(run => run.Market != null && run.Market.AssetId == assetId))
            .AsEnumerable().OrderByDescending(run => run.StartedAt).ThenBy(run => run.RunId)
            .Take(Math.Clamp(limit, 1, 50)).ToArray());
    }
}
