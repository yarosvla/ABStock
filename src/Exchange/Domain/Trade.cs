namespace ABStock.Exchange.Domain;

public sealed record Trade
{
    public required string Id { get; init; }

    public required string BuyOrderId { get; init; }

    public required string SellOrderId { get; init; }

    public decimal Price { get; init; }

    public decimal Quantity { get; init; }

    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;
}
