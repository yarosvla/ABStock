namespace ABStock.Shared;

public enum AgentType
{
    TrendFollowing,
    CounterTrend,
    MarketMaker,
    NewsDriven
}

public enum TradeAction
{
    Hold,
    Buy,
    Sell
}

public enum OrderSide
{
    Buy,
    Sell
}

public enum OrderType
{
    Limit,
    Market
}

public enum OrderExecutionStatus
{
    Open,
    PartiallyFilled,
    Filled,
    Expired,
    Rejected,
    Cancelled
}

public enum SignalPolarity
{
    Positive,
    Neutral,
    Negative
}

public enum AssetType
{
    Stock,
    Commodity,
    Crypto,
    Bond
}
