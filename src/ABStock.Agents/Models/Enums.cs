namespace ABStock.Agents.Models;

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

public enum TradeSide
{
    Buy,
    Sell
}
