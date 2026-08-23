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
                var order = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    "истории цены ещё нет — открываю первую покупку", [order]);
            }
            return HoldDecision("истории цены ещё нет, денег на вход не хватает");
        }

        var lastPrice = snapshot.RecentPrices[^1];
        var prevPrice = snapshot.RecentPrices[^2];

        if (lastPrice >= prevPrice && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"цена растёт {prevPrice:F2} → {lastPrice:F2} — иду за движением", [order]);
        }

        if (lastPrice < prevPrice && CanSell(_orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"цена падает {prevPrice:F2} → {lastPrice:F2} — выхожу из позиции", [order]);
        }

        return HoldDecision("не хватает денег или позиции для сделки");
    }
}
