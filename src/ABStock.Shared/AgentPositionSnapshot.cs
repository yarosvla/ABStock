namespace ABStock.Shared;

public sealed record AgentPositionSnapshot(
    Guid AssetId,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal InitialQuantity,
    decimal LastPrice
)
{
    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    public decimal MarketValue => Quantity * LastPrice;
}
