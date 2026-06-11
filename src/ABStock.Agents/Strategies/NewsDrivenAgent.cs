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

        if (newsSignal.Polarity == SignalPolarity.Positive)
        {
            if (snapshot.BestAsk is null)
                return HoldDecision("Positive news, but no asks to buy from");

            if (!CanBuy(snapshot.BestAsk.Value, _orderQuantity))
                return HoldDecision("Positive news, but insufficient funds");

            var order = CreateMarketOrder(OrderSide.Buy, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Positive news (confidence={newsSignal.Confidence:F2}), market buy", [order]);
        }

        if (newsSignal.Polarity == SignalPolarity.Negative)
        {
            if (snapshot.BestBid is null)
                return HoldDecision("Negative news, but no bids to sell into");

            if (!CanSell(_orderQuantity))
                return HoldDecision("Negative news, but insufficient position");

            var order = CreateMarketOrder(OrderSide.Sell, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"Negative news (confidence={newsSignal.Confidence:F2}), market sell", [order]);
        }

        return HoldDecision($"News is {newsSignal.Polarity}, holding");
    }
}
