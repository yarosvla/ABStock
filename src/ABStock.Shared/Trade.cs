namespace ABStock.Shared;

public record Trade(
    Guid Id,
    Guid BuyOrderId,
    Guid SellOrderId,
    string BuyerAgentName,
    string SellerAgentName,
    decimal Price,
    decimal Quantity,
    DateTimeOffset ExecutedAt
);
