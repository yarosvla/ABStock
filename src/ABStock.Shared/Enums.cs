namespace ABStock.Shared;

public enum AgentType
{
    TrendFollowing,
    CounterTrend,
    MarketMaker,
    NewsDriven,
    SentimentFlow
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