namespace ABStock.Shared;

public sealed record AgentMarketContext(
    Guid SessionId,
    Guid AssetId,
    MarketSnapshot Snapshot,
    AgentAccountSnapshot Account
);
