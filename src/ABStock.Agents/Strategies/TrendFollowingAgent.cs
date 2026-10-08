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
                    $"{prefix}движение {movePercent:+0.00;-0.00;0.00} % ниже порога {threshold:F2} % — жду");
            }

            if (movePercent > 0m && CanBuy(buyPrice, quantity))
            {
                var order = CreateLimitOrder(OrderSide.Buy, buyPrice, quantity);
                return new AgentDecision(State.AgentName, TradeAction.Buy,
                    $"{prefix}рост {movePercent:+0.00;-0.00;0.00} % выше порога {threshold:F2} % — покупаю {quantity:F2}",
                    [order]);
            }

            if (movePercent < 0m && CanSell(quantity))
            {
                var order = CreateLimitOrder(OrderSide.Sell, sellPrice, quantity);
                return new AgentDecision(State.AgentName, TradeAction.Sell,
                    $"{prefix}падение {movePercent:+0.00;-0.00;0.00} % ниже порога −{threshold:F2} % — продаю {quantity:F2}",
                    [order]);
            }

            return HoldDecision($"{prefix}сигнал есть, но доступных денег или позиции для {quantity:F2} не хватает");
        }

        if (lastPrice >= prevPrice && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"цена растёт {DescribeMove(prevPrice, lastPrice)} — иду за движением", [order]);
        }

        if (lastPrice < prevPrice && CanSell(_orderQuantity))
        {
            var order = CreateLimitOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"цена падает {DescribeMove(prevPrice, lastPrice)} — выхожу из позиции", [order]);
        }

        return HoldDecision("не хватает денег или позиции для сделки");
    }
}
