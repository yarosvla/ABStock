using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class CounterTrendAgent : AgentBase
{
    private readonly decimal _orderQuantity;

    public CounterTrendAgent(decimal initialCash, decimal initialPosition = 0, decimal orderQuantity = 1m)
        : base("CounterTrend", AgentType.CounterTrend, initialCash, initialPosition)
    {
        _orderQuantity = orderQuantity;
    }

    // TODO: improve strategy
    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        var buyPrice = snapshot.BestAsk ?? snapshot.LastPrice;
        var sellPrice = snapshot.BestBid ?? snapshot.LastPrice;

        if (snapshot.RecentPrices.Count < 2)
        {
            if (CanBuy(buyPrice, _orderQuantity))
            {
                var order = CreateOrder(OrderSide.Buy, buyPrice, _orderQuantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    "No price history, placing initial buy", [order]);
            }
            return HoldDecision("No price history and insufficient funds");
        }

        var lastPrice = snapshot.RecentPrices[^1];
        var prevPrice = snapshot.RecentPrices[^2];

        if (lastPrice < prevPrice && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Price dropped {prevPrice:F2} -> {lastPrice:F2}, buying dip", [order]);
        }

        if (lastPrice >= prevPrice && CanSell(_orderQuantity))
        {
            var order = CreateOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"Price rose {prevPrice:F2} -> {lastPrice:F2}, selling peak", [order]);
        }

        return HoldDecision("Insufficient funds/position");
    }
}
