namespace ABStock.Application.MarketHistory;

public interface IMarketCandleReader
{
    IReadOnlyList<MarketCandle> GetCandles(Guid runId, TimeSpan interval, int limit);
}

public record MarketCandle(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal Volume
);
