namespace ABStock.Shared;

public sealed record MarketState(
    Guid SessionId,
    Guid AssetId,
    MarketSnapshot Snapshot,
    OrderBookSnapshot OrderBook
);
