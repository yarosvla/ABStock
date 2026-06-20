using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class MarketMakerAgent : AgentBase
{
    private const int LadderLevels = 4;
    private readonly decimal _spreadPercent;
    private readonly decimal _orderQuantity;
    private readonly decimal _maxPosition;

    public MarketMakerAgent(
        decimal initialCash,
        decimal initialPosition = 0,
        decimal spreadPercent = 0.01m,
        decimal orderQuantity = 1m,
        decimal maxPosition = decimal.MaxValue)
        : base("MarketMaker", AgentType.MarketMaker, initialCash, initialPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(spreadPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(spreadPercent, 0.5m);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(orderQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPosition);

        _spreadPercent = spreadPercent;
        _orderQuantity = orderQuantity;
        _maxPosition = maxPosition;
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        var mid = snapshot.BestBid is { } bid && snapshot.BestAsk is { } ask
            ? (bid + ask) / 2m
            : snapshot.LastPrice;

        var orders = new List<Order>();
        var remainingCash = State.AvailableCash;
        var remainingPosition = State.AvailablePosition;
        var projectedPosition = State.Position;
        var priceStep = Math.Max(_spreadPercent / LadderLevels, 0.0025m);

        for (var level = 1; level <= LadderLevels; level++)
        {
            var levelQuantity = _orderQuantity + (LadderLevels - level) * 0.5m;
            var bidPrice = mid * (1 - priceStep * level);
            var askPrice = mid * (1 + priceStep * level);
            var buyCost = bidPrice * levelQuantity;

            if (remainingCash >= buyCost && projectedPosition + levelQuantity <= _maxPosition)
            {
                orders.Add(CreateLimitOrder(OrderSide.Buy, bidPrice, levelQuantity));
                remainingCash -= buyCost;
                projectedPosition += levelQuantity;
            }

            if (remainingPosition >= levelQuantity)
            {
                orders.Add(CreateLimitOrder(OrderSide.Sell, askPrice, levelQuantity));
                remainingPosition -= levelQuantity;
            }
        }

        if (orders.Count == 0)
            return HoldDecision($"Cannot place orders: cash={State.AvailableCash}, position={State.AvailablePosition}");

        return new AgentDecision(
            State.AgentName,
            TradeAction.Hold,
            $"Maintaining {LadderLevels}-level liquidity ladder around {mid:F2}",
            orders
        );
    }
}
