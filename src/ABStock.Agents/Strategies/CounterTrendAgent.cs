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
                var order = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    "истории цены ещё нет — открываю первую покупку", [order]);
            }
            return HoldDecision("истории цены ещё нет, денег на вход не хватает");
        }

        var lastPrice = snapshot.RecentPrices[^1];
        var prevPrice = snapshot.RecentPrices[^2];

        if (lastPrice < prevPrice && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"цена ушла вниз {DescribeMove(prevPrice, lastPrice)} — покупаю просадку", [order]);
        }

        if (lastPrice >= prevPrice && CanSell(_orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"цена ушла вверх {DescribeMove(prevPrice, lastPrice)} — продаю на пике", [order]);
        }

        return HoldDecision("не хватает денег или позиции для сделки");
    }
}
