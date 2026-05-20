namespace ABStock.Shared;

public record OrderBookLevel(
    decimal Price,
    decimal Quantity,
    int OrdersCount
);
