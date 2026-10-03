namespace ABStock.Shared;

public sealed record AgentPositionSnapshot(
    Guid AssetId,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal InitialQuantity,
    decimal LastPrice
)
{
    public decimal CostBasis { get; init; }

    public decimal RealizedPnl { get; init; }

    public decimal? AverageEntryPrice => Quantity == 0m ? null : CostBasis / Quantity;

    public decimal UnrealizedPnl => MarketValue - CostBasis;

    public decimal TotalPnl => RealizedPnl + UnrealizedPnl;

    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    public decimal MarketValue => Quantity * LastPrice;
}
