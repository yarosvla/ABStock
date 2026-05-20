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

public enum SignalPolarity
{
    Positive,
    Neutral,
    Negative
}