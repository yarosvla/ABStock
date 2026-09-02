using ABStock.Application.MarketHistory;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.MarketHistory;

internal sealed class EfSimulationHistoryReader(IDbContextFactory<AbStockDbContext> contextFactory) : ISimulationHistoryReader
{
    public SimulationRunSummary? GetRun(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            return null;
        }

        using var db = contextFactory.CreateDbContext();

        var run = db.SimulationRuns
            .AsNoTracking()
            .Where(item => item.Id == runId)
            .Select(item => new
            {
                item.Id,
                item.AssetName,
                item.AssetType,
                item.StartedAt,
                TickCount = item.MarketTicks.Count,
                TradeCount = item.Trades.Count
            })
            .FirstOrDefault();

        if (run is null)
        {
            return null;
        }

        var lastPrice = db.MarketTicks
            .AsNoTracking()
            .Where(tick => tick.SimulationRunId == runId)
            .OrderByDescending(tick => tick.Tick)
            .Select(tick => (decimal?)tick.LastPrice)
            .FirstOrDefault() ?? 0m;

        return new SimulationRunSummary(
            run.Id,
            run.AssetName,
            run.AssetType,
            run.StartedAt,
            run.TickCount,
            run.TradeCount,
            lastPrice);
    }

    public SimulationHistoryOverview GetOverview(int recentRuns = 10)
    {
        var normalizedRecentRuns = Math.Clamp(recentRuns, 1, 50);

        using var db = contextFactory.CreateDbContext();

        var runs = db.SimulationRuns.AsNoTracking();
        var runCount = runs.Count();
        var distinctAssetCount = runs
            .Select(run => run.AssetName)
            .Distinct()
            .Count();
        var totalTradeCount = db.Trades.AsNoTracking().Count();
        var totalTickCount = db.MarketTicks.AsNoTracking().Count();

        // SQLite cannot translate ORDER BY over DateTimeOffset, so project the minimal
        // columns server-side and order/take on the client (same workaround as EfMarketCandleReader).
        var recentRunActivity = runs
            .Select(run => new { run.StartedAt, run.AssetName, run.AssetType })
            .AsEnumerable()
            .OrderByDescending(run => run.StartedAt)
            .Take(normalizedRecentRuns)
            .Select(run => new SimulationActivityItem(
                run.StartedAt,
                "Запущена симуляция",
                $"{run.AssetName} · {FormatAssetType(run.AssetType)}"))
            .ToArray();

        var recentTradeActivity = db.Trades
            .AsNoTracking()
            .Select(trade => new { trade.ExecutedAt, trade.BuyerAgentName, trade.SellerAgentName, trade.Price })
            .AsEnumerable()
            .OrderByDescending(trade => trade.ExecutedAt)
            .Take(normalizedRecentRuns)
            .Select(trade => new SimulationActivityItem(
                trade.ExecutedAt,
                "Сделка исполнена",
                // Внутреннее имя агента совпадает с именем типа, и в строку
                // события оно попадало как есть — «CounterTrend ↔ MarketMaker».
                // Английские слова в русском интерфейсе допустимы только для
                // тикеров и таймфреймов (раздел 17).
                $"{ABStock.Shared.AgentTypeNames.LabelForAgentName(trade.BuyerAgentName)} ↔ " +
                $"{ABStock.Shared.AgentTypeNames.LabelForAgentName(trade.SellerAgentName)} · {trade.Price:F2} ₽"))
            .ToArray();

        var recentActivity = recentRunActivity
            .Concat(recentTradeActivity)
            .OrderByDescending(item => item.OccurredAt)
            .Take(normalizedRecentRuns)
            .ToArray();

        return new SimulationHistoryOverview(
            runCount,
            distinctAssetCount,
            totalTradeCount,
            totalTickCount,
            recentActivity);
    }

    private static string FormatAssetType(ABStock.Shared.AssetType assetType) =>
        assetType switch
        {
            ABStock.Shared.AssetType.Stock => "Акция",
            ABStock.Shared.AssetType.Bond => "Облигация",
            ABStock.Shared.AssetType.Commodity => "Товар",
            ABStock.Shared.AssetType.Crypto => "Криптовалюта",
            _ => assetType.ToString()
        };
}
