namespace ABStock.Shared;

public record MarketSnapshot(
    decimal LastPrice,
    decimal? BestBid,
    decimal? BestAsk,
    decimal Volume,
    IReadOnlyList<decimal> RecentPrices,
    IReadOnlyList<Trade> RecentTrades
)
{
    public long TotalTradeCount { get; init; }
}
