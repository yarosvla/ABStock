namespace ABStock.Persistence.Entities;

public sealed class SessionMarketEntity
{
    public Guid RunId { get; set; }
    public SimulationRunEntity? Run { get; set; }
    public Guid SessionId { get; set; }
    public TradingSessionEntity? Session { get; set; }
    public Guid AssetId { get; set; }
    public AssetEntity? Asset { get; set; }
}
