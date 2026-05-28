using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class TrendFollowingAgent : AgentBase
{
    private readonly decimal _orderQuantity;

    public TrendFollowingAgent(decimal initialCash, decimal initialPosition = 0, decimal orderQuantity = 1m)
        : base("TrendFollowing", AgentType.TrendFollowing, initialCash, initialPosition)
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

        if (lastPrice >= prevPrice && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Price rising {prevPrice:F2} -> {lastPrice:F2}, buying", [order]);
        }

        if (lastPrice < prevPrice && CanSell(_orderQuantity))
        {
            var order = CreateOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"Price falling {prevPrice:F2} -> {lastPrice:F2}, selling", [order]);
        }

        return HoldDecision("Insufficient funds/position");
    }
}
