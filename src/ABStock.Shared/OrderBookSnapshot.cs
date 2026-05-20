namespace ABStock.Shared;

public record OrderBookSnapshot(
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks
);
