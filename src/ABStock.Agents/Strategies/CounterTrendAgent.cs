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

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
        => DecideCore(snapshot, asset: null);

    public override AgentDecision Decide(AgentMarketContext context, NewsSignal? newsSignal)
        => DecideCore(context.Snapshot, context.Asset);

    private AgentDecision DecideCore(MarketSnapshot snapshot, Asset? asset)
    {
        var quantity = asset is null
            ? _orderQuantity
            : AssetStrategyPolicy.MomentumQuantity(_orderQuantity, asset);
        var prefix = asset is null ? string.Empty : $"{AssetStrategyPolicy.Symbol(asset)}: ";
        var buyPrice = snapshot.BestAsk ?? snapshot.LastPrice;
        var sellPrice = snapshot.BestBid ?? snapshot.LastPrice;

        if (snapshot.RecentPrices.Count < 2)
        {
            if (CanBuy(buyPrice, quantity))
            {
                var order = CreateLimitOrder(OrderSide.Buy, buyPrice, quantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    $"{prefix}истории цены ещё нет — открываю первую покупку объёмом {quantity:F2}", [order]);
            }
            return HoldDecision($"{prefix}истории цены ещё нет, доступных денег на вход не хватает");
        }

        var lastPrice = snapshot.RecentPrices[^1];
        var prevPrice = snapshot.RecentPrices[^2];

        if (asset is not null)
        {
            var movePercent = prevPrice == 0m ? 0m : (lastPrice - prevPrice) / prevPrice * 100m;
            var threshold = AssetStrategyPolicy.MomentumThresholdPercent(asset);

            if (Math.Abs(movePercent) < threshold)
            {
                return HoldDecision(
                    $"{prefix}движение {movePercent:+0.00;-0.00;0.00} % ниже порога {threshold:F2} % — жду отката");
            }

            if (movePercent < 0m && CanBuy(buyPrice, quantity))
            {
                var order = CreateLimitOrder(OrderSide.Buy, buyPrice, quantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    $"{prefix}просадка {movePercent:+0.00;-0.00;0.00} % глубже порога −{threshold:F2} % — покупаю {quantity:F2}",
                    [order]);
            }

            if (movePercent > 0m && CanSell(quantity))
            {
                var order = CreateLimitOrder(OrderSide.Sell, sellPrice, quantity);
                return new AgentDecision(State.AgentName, TradeAction.Sell,
                    $"{prefix}рост {movePercent:+0.00;-0.00;0.00} % выше порога {threshold:F2} % — продаю {quantity:F2}",
                    [order]);
            }

            return HoldDecision($"{prefix}сигнал есть, но доступных денег или позиции для {quantity:F2} не хватает");
        }

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
