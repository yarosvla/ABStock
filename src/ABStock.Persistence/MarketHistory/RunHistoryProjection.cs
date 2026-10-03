using ABStock.Application.MarketHistory;
using ABStock.Persistence.Entities;

namespace ABStock.Persistence.MarketHistory;

internal static class RunHistoryProjection
{
    public static IQueryable<SimulationRunSummary> SelectSummaries(IQueryable<SimulationRunEntity> runs) =>
        runs.Select(run => new SimulationRunSummary(
            run.Id, run.AssetName, run.AssetType, run.StartedAt,
            run.MarketTicks.Count, run.Trades.Count,
            run.MarketTicks.OrderByDescending(tick => tick.Tick)
                .Select(tick => (decimal?)tick.LastPrice).FirstOrDefault() ?? run.StartPrice)
        {
            SessionId = run.Market == null ? null : (Guid?)run.Market.SessionId,
            AssetId = run.Market == null ? null : (Guid?)run.Market.AssetId
        });
}
