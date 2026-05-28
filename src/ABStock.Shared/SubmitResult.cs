namespace ABStock.Shared;

public record SubmitResult(
    MarketSnapshot Snapshot,
    IReadOnlyList<Trade> Trades,
    IReadOnlyList<Order> AcceptedOrders,
    IReadOnlyList<RejectedOrder> RejectedOrders
);
