namespace ABStock.Shared;

public record SubmitResult(
    MarketSnapshot Snapshot,
    IReadOnlyList<Trade> Trades,
    IReadOnlyList<Order> AcceptedOrders,
    IReadOnlyList<RejectedOrder> RejectedOrders)
{
    public SubmitResult(
        MarketSnapshot snapshot,
        IReadOnlyList<Trade> trades,
        IReadOnlyList<Order> acceptedOrders,
        IReadOnlyList<RejectedOrder> rejectedOrders,
        IReadOnlyList<OrderExecutionReport> OrderReports)
        : this(snapshot, trades, acceptedOrders, rejectedOrders)
    {
        this.OrderReports = OrderReports;
    }

    public IReadOnlyList<OrderExecutionReport> OrderReports { get; init; } = [];
}
