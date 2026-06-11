namespace ABStock.Persistence.Entities;

public sealed class MarketTickEntity
{
    public long Id { get; set; }
    public Guid SimulationRunId { get; set; }
    public SimulationRunEntity? SimulationRun { get; set; }
    public int Tick { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
    public decimal LastPrice { get; set; }
    public decimal? BestBid { get; set; }
    public decimal? BestAsk { get; set; }
    public decimal TotalVolume { get; set; }
}
