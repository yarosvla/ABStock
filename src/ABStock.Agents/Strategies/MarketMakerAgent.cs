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
        => DecideCore(snapshot, asset: null);

    public override AgentDecision Decide(AgentMarketContext context, NewsSignal? newsSignal)
        => DecideCore(context.Snapshot, context.Asset);

    private AgentDecision DecideCore(MarketSnapshot snapshot, Asset? asset)
    {
        var orders = new List<Order>();
        var remainingCash = State.AvailableCash;
        var remainingPosition = State.AvailablePosition;
        var spreadPercent = asset is null
            ? _spreadPercent
            : AssetStrategyPolicy.MarketMakerSpread(_spreadPercent, asset);
        var priceStep = Math.Max(spreadPercent / LadderLevels, 0.0025m);
        var prefix = asset is null ? string.Empty : $"{AssetStrategyPolicy.Symbol(asset)}: ";

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
            return HoldDecision(
                $"{prefix}нечем выставлять заявки: доступно денег {State.AvailableCash:F2}, позиции {State.AvailablePosition:F2}");

        return new AgentDecision(
            State.AgentName,
            TradeAction.Hold,
            $"{prefix}держу спред {(priceStep * LadderLevels * 100m):F2} % вокруг {snapshot.LastPrice:F2}, лестница на {LadderLevels} уровня",
            orders
        );
    }
}
