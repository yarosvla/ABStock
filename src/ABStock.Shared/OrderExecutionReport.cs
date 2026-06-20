namespace ABStock.Shared;

public record OrderExecutionReport(
    Order? Order,
    OrderExecutionStatus Status,
    decimal RequestedQuantity,
    decimal FilledQuantity,
    decimal RemainingQuantity,
    decimal? AveragePrice,
    string? Message
);
