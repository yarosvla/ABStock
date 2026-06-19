namespace ABStock.Application.MarketHistory;

internal sealed class NullMarketCandleReader : IMarketCandleReader
{
    public IReadOnlyList<MarketCandle> GetCandles(Guid runId, TimeSpan interval, int limit) => [];
}
