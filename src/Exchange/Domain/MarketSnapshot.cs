namespace ABStock.Exchange.Domain;

public sealed record MarketSnapshot
{
    public decimal LastPrice { get; init; }

    public decimal? BestBid { get; init; }

    public decimal? BestAsk { get; init; }

    public decimal Volume { get; init; }

    public IReadOnlyList<decimal> RecentPrices { get; init; } = [];

    public IReadOnlyList<Trade> RecentTrades { get; init; } = [];
}
