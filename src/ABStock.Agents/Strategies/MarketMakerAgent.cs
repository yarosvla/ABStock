using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class MarketMakerAgent : AgentBase
{
    private readonly decimal _spreadPercent;
    private readonly decimal _orderQuantity;
    private readonly decimal _maxPosition;

    public MarketMakerAgent(decimal initialCash, decimal initialPosition = 0, decimal spreadPercent = 0.01m, decimal orderQuantity = 1m, decimal maxPosition = 10m)
        : base("MarketMaker", AgentType.MarketMaker, initialCash, initialPosition)
    {
        _spreadPercent = spreadPercent;
        _orderQuantity = orderQuantity;
        _maxPosition = maxPosition;
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        var bidPrice = snapshot.LastPrice * (1 - _spreadPercent);
        var askPrice = snapshot.LastPrice * (1 + _spreadPercent);

        var orders = new List<Order>();

        if (CanBuy(bidPrice, _orderQuantity) && State.Position < _maxPosition)
            orders.Add(CreateOrder(OrderSide.Buy, bidPrice, _orderQuantity));

        if (CanSell(_orderQuantity))
            orders.Add(CreateOrder(OrderSide.Sell, askPrice, _orderQuantity));

        if (orders.Count == 0)
            return HoldDecision($"Cannot place orders: cash={State.Cash}, position={State.Position}");

        return new AgentDecision(
            State.AgentName,
            TradeAction.Hold,
            $"Bid at {bidPrice:F2}, Ask at {askPrice:F2}, spread={_spreadPercent:P0}",
            orders
        );
    }
}
