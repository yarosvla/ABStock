using ABStock.Shared;

namespace ABStock.Persistence.Entities;

public sealed class SimulationRunEntity
{
    public Guid Id { get; set; }
    public string AssetName { get; set; } = string.Empty;
    public string AssetDescription { get; set; } = string.Empty;
    public AssetType AssetType { get; set; }
    public decimal StartPrice { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    public List<MarketTickEntity> MarketTicks { get; set; } = [];
    public List<TradeEntity> Trades { get; set; } = [];
}
