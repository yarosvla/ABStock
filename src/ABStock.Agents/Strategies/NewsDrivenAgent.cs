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
            return HoldDecision("новостей в сессии не было — сигнала нет, заявки не выставляются");

        if (newsSignal.Polarity == SignalPolarity.Positive)
        {
            if (snapshot.BestAsk is null)
                return HoldDecision("новость позитивная, но покупать не у кого — асков нет");

            if (!CanBuy(snapshot.BestAsk.Value, _orderQuantity))
                return HoldDecision("новость позитивная, но денег на покупку не хватает");

            var order = CreateMarketOrder(OrderSide.Buy, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"позитивная новость, уверенность {newsSignal.Confidence:F2} — покупаю по рынку", [order]);
        }

        if (newsSignal.Polarity == SignalPolarity.Negative)
        {
            if (snapshot.BestBid is null)
                return HoldDecision("новость негативная, но продавать некому — бидов нет");

            if (!CanSell(_orderQuantity))
                return HoldDecision("новость негативная, но позиции для продажи нет");

            var order = CreateMarketOrder(OrderSide.Sell, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"негативная новость, уверенность {newsSignal.Confidence:F2} — продаю по рынку", [order]);
        }

        return HoldDecision($"новость нейтральная — держу позицию");
    }
}
