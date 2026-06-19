using ABStock.Application.MarketHistory;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.MarketHistory;

internal sealed class EfMarketCandleReader(IDbContextFactory<AbStockDbContext> contextFactory) : IMarketCandleReader
{
    public IReadOnlyList<MarketCandle> GetCandles(Guid runId, TimeSpan interval, int limit)
    {
        if (runId == Guid.Empty)
        {
            return [];
        }

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Candle interval must be positive.");
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Candle limit must be positive.");
        }

        using var db = contextFactory.CreateDbContext();

        var ticks = db.MarketTicks
            .AsNoTracking()
            .Where(tick => tick.SimulationRunId == runId)
            .AsEnumerable()
            .OrderBy(tick => tick.CapturedAt)
            .ToArray();

        if (ticks.Length == 0)
        {
            return [];
        }

        var trades = db.Trades
            .AsNoTracking()
            .Where(trade => trade.SimulationRunId == runId)
            .ToArray();

        var intervalMs = checked((long)interval.TotalMilliseconds);
        var tradeVolumes = trades
            .GroupBy(trade => GetBucketStartMs(trade.ExecutedAt, intervalMs))
            .ToDictionary(
                group => group.Key,
                group => group.Sum(trade => trade.Quantity));

        return ticks
            .GroupBy(tick => GetBucketStartMs(tick.CapturedAt, intervalMs))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var orderedTicks = group.OrderBy(tick => tick.CapturedAt).ToArray();
                var open = orderedTicks[0].LastPrice;
                var close = orderedTicks[^1].LastPrice;
                var high = orderedTicks.Max(tick => tick.LastPrice);
                var low = orderedTicks.Min(tick => tick.LastPrice);
                var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(group.Key);

                tradeVolumes.TryGetValue(group.Key, out var volume);

                return new MarketCandle(
                    StartedAt: startedAt,
                    EndedAt: startedAt.Add(interval),
                    Open: open,
                    High: high,
                    Low: low,
                    Close: close,
                    Volume: volume
                );
            })
            .TakeLast(limit)
            .ToArray();
    }

    private static long GetBucketStartMs(DateTimeOffset timestamp, long intervalMs)
    {
        var timestampMs = timestamp.ToUnixTimeMilliseconds();
        return timestampMs - timestampMs % intervalMs;
    }
}
