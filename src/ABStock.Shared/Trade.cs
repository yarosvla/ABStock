namespace ABStock.Shared;

public record Trade(
    Guid Id,
    Guid BuyOrderId,
    Guid SellOrderId,
    decimal Price,
    int Quantity,
    DateTimeOffset ExecutedAt
);