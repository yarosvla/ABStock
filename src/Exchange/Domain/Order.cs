namespace ABStock.Exchange.Domain;

public sealed record Order
{
    public required string Id { get; init; }

    public required string AgentId { get; init; }

    public OrderSide Side { get; init; }

    public OrderType Type { get; init; } = OrderType.Limit;

    public decimal Quantity { get; init; }

    public decimal? LimitPrice { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
