namespace ABStock.Persistence.Entities;

public sealed class TradeEntity
{
    public Guid Id { get; set; }
    public Guid SimulationRunId { get; set; }
    public SimulationRunEntity? SimulationRun { get; set; }
    public Guid BuyOrderId { get; set; }
    public Guid SellOrderId { get; set; }
    public string BuyerAgentName { get; set; } = string.Empty;
    public string SellerAgentName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public DateTimeOffset ExecutedAt { get; set; }
}
