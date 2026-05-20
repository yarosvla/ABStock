namespace ABStock.Shared;

public record Trade(
    Guid Id,
    Guid BuyOrderId,
    Guid SellOrderId,
    decimal Price,
    decimal Quantity,
    DateTimeOffset ExecutedAt
);
