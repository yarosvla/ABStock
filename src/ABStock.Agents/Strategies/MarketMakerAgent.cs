using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class MarketMakerAgent : AgentBase
{
    private const int LadderLevels = 4;
    private readonly decimal _spreadPercent;
    private readonly decimal _orderQuantity;

    public MarketMakerAgent(decimal initialCash, decimal initialPosition = 0, decimal spreadPercent = 0.01m, decimal orderQuantity = 1m)
        : base("MarketMaker", AgentType.MarketMaker, initialCash, initialPosition)
    {
        _spreadPercent = spreadPercent;
        _orderQuantity = orderQuantity;
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        var orders = new List<Order>();
        var remainingCash = State.Cash;
        var remainingPosition = State.Position;
        var priceStep = Math.Max(_spreadPercent / LadderLevels, 0.0025m);

        for (var level = 1; level <= LadderLevels; level++)
        {
            var levelQuantity = _orderQuantity + (LadderLevels - level) * 0.5m;
            var bidPrice = snapshot.LastPrice * (1 - priceStep * level);
            var askPrice = snapshot.LastPrice * (1 + priceStep * level);
            var buyCost = bidPrice * levelQuantity;

            if (remainingCash >= buyCost)
            {
                orders.Add(CreateLimitOrder(OrderSide.Buy, bidPrice, levelQuantity));
                remainingCash -= buyCost;
            }

            if (remainingPosition >= levelQuantity)
            {
                orders.Add(CreateLimitOrder(OrderSide.Sell, askPrice, levelQuantity));
                remainingPosition -= levelQuantity;
            }
        }

        if (orders.Count == 0)
            return HoldDecision($"Cannot place orders: cash={State.Cash}, position={State.Position}");

        return new AgentDecision(
            State.AgentName,
            TradeAction.Hold,
            $"Maintaining {LadderLevels}-level liquidity ladder around {snapshot.LastPrice:F2}",
            orders
        );
    }
}
