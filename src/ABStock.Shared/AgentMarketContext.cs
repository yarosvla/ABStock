namespace ABStock.Shared;

public sealed record AgentMarketContext(
    Guid SessionId,
    Guid AssetId,
    MarketSnapshot Snapshot,
    AgentAccountSnapshot Account
)
{
    public Asset? Asset { get; init; }
}
