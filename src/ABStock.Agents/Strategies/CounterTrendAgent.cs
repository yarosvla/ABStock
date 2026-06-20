using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class CounterTrendAgent : AgentBase
{
    private readonly int _period;
    private readonly decimal _deviationThreshold;
    private readonly decimal _orderQuantity;

    public CounterTrendAgent(
        decimal initialCash,
        decimal initialPosition = 0,
        int period = 10,
        decimal deviationThreshold = 0.02m,
        decimal orderQuantity = 1m)
        : base("CounterTrend", AgentType.CounterTrend, initialCash, initialPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deviationThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(deviationThreshold, 1m);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderQuantity);

        _period = period;
        _deviationThreshold = deviationThreshold;
        _orderQuantity = orderQuantity;
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        var buyPrice = snapshot.BestAsk ?? snapshot.LastPrice;
        var sellPrice = snapshot.BestBid ?? snapshot.LastPrice;

        if (snapshot.RecentPrices.Count < _period)
        {
            if (CanBuy(buyPrice, _orderQuantity))
            {
                var probe = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    "Warming up, probing liquidity", [probe]);
            }

            return HoldDecision("Warming up, insufficient funds");
        }

        var sma = Indicators.Sma(snapshot.RecentPrices, _period);
        var price = snapshot.LastPrice;
        var lowerBand = sma * (1 - _deviationThreshold);
        var upperBand = sma * (1 + _deviationThreshold);

        if (price < lowerBand && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Price {price:F2} below band {lowerBand:F2} (SMA{_period}), buying dip", [order]);
        }

        if (price > upperBand && CanSell(_orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"Price {price:F2} above band {upperBand:F2} (SMA{_period}), selling peak", [order]);
        }

        return HoldDecision($"Price {price:F2} within band [{lowerBand:F2}, {upperBand:F2}]");
    }
}
