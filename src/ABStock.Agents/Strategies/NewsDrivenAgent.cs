using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class NewsDrivenAgent : AgentBase
{
    private readonly decimal _orderQuantity;

    public NewsDrivenAgent(decimal initialCash, decimal initialPosition = 0, decimal orderQuantity = 1m)
        : base("NewsDriven", AgentType.NewsDriven, initialCash, initialPosition)
    {
        _orderQuantity = orderQuantity;
    }

    // TODO: improve strategy
    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        if (newsSignal is null)
            return HoldDecision("No news");

        var buyPrice = snapshot.BestAsk ?? snapshot.LastPrice;
        var sellPrice = snapshot.BestBid ?? snapshot.LastPrice;

        if (newsSignal.Polarity == SignalPolarity.Positive && CanBuy(buyPrice, _orderQuantity))
        {
            var order = CreateOrder(OrderSide.Buy, buyPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Positive news (confidence={newsSignal.Confidence:F2}), buying", [order]);
        }

        if (newsSignal.Polarity == SignalPolarity.Negative && CanSell(_orderQuantity))
        {
            var order = CreateOrder(OrderSide.Sell, sellPrice, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"Negative news (confidence={newsSignal.Confidence:F2}), selling", [order]);
        }

        return HoldDecision($"News is {newsSignal.Polarity}, but insufficient funds/position");
    }
}
