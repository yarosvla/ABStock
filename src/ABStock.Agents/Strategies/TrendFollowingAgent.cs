using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class TrendFollowingAgent : AgentBase
{
    private readonly int _shortPeriod;
    private readonly int _longPeriod;
    private readonly decimal _orderQuantity;

    public TrendFollowingAgent(
        decimal initialCash,
        decimal initialPosition = 0,
        int shortPeriod = 3,
        int longPeriod = 10,
        decimal orderQuantity = 1m)
        : base("TrendFollowing", AgentType.TrendFollowing, initialCash, initialPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shortPeriod);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(longPeriod, shortPeriod);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderQuantity);

        _shortPeriod = shortPeriod;
        _longPeriod = longPeriod;
        _orderQuantity = orderQuantity;
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        var buyPrice = snapshot.BestAsk ?? snapshot.LastPrice;
        var sellPrice = snapshot.BestBid ?? snapshot.LastPrice;

        if (snapshot.RecentPrices.Count < _longPeriod)
        {
            if (CanBuy(buyPrice, _orderQuantity))
            {
                var probe = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    "Warming up, probing liquidity", [probe]);
            }

            return HoldDecision("Warming up, insufficient funds");
        }

        var shortSma = Indicators.Sma(snapshot.RecentPrices, _shortPeriod);
        var longSma = Indicators.Sma(snapshot.RecentPrices, _longPeriod);

        if (shortSma > longSma && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Uptrend SMA{_shortPeriod}={shortSma:F2} > SMA{_longPeriod}={longSma:F2}, buying", [order]);
        }

        if (shortSma < longSma && CanSell(_orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"Downtrend SMA{_shortPeriod}={shortSma:F2} < SMA{_longPeriod}={longSma:F2}, selling", [order]);
        }

        return HoldDecision($"No SMA crossover (short={shortSma:F2}, long={longSma:F2})");
    }
}
