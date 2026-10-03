using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class NewsDrivenAgent : AgentBase
{
    private const decimal MinimumMatchScore = 0.60m;
    private const decimal MinimumConviction = 0.10m;
    private readonly decimal _orderQuantity;

    public NewsDrivenAgent(decimal initialCash, decimal initialPosition = 0, decimal orderQuantity = 1m)
        : base("NewsDriven", AgentType.NewsDriven, initialCash, initialPosition)
    {
        _orderQuantity = orderQuantity;
    }

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

    public override AgentDecision Decide(AgentMarketContext context, NewsSignal? newsSignal)
    {
        if (context.Asset is not { } asset)
        {
            return Decide(context.Snapshot, newsSignal);
        }

        var symbol = AssetStrategyPolicy.Symbol(asset);
        if (newsSignal is null)
        {
            return HoldDecision($"{symbol}: новостей для актива не было — сигналов нет");
        }

        if (newsSignal.MatchScore < MinimumMatchScore)
        {
            return HoldDecision(
                $"{symbol}: совпадение с профилем {newsSignal.MatchScore:P0} ниже порога {MinimumMatchScore:P0} — жду");
        }

        var conviction = Math.Abs(newsSignal.ImpactScore) * Math.Clamp(newsSignal.MatchScore, 0m, 1m);
        if (conviction < MinimumConviction || newsSignal.ImpactScore == 0m)
        {
            return HoldDecision(
                $"{symbol}: влияние {newsSignal.ImpactScore:+0.00;-0.00;0.00} при совпадении {newsSignal.MatchScore:P0} слишком слабое — жду");
        }

        var quantity = Math.Round(_orderQuantity * ConvictionMultiplier(conviction), 2,
            MidpointRounding.AwayFromZero);

        if (newsSignal.ImpactScore > 0m)
        {
            if (context.Snapshot.BestAsk is null)
            {
                return HoldDecision($"{symbol}: влияние положительное, но покупать не у кого — асков нет");
            }

            if (!CanBuy(context.Snapshot.BestAsk.Value, quantity))
            {
                return HoldDecision($"{symbol}: влияние положительное, но доступных денег на {quantity:F2} не хватает");
            }

            var order = CreateMarketOrder(OrderSide.Buy, quantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"{symbol}: влияние {newsSignal.ImpactScore:+0.00;-0.00;0.00}, совпадение {newsSignal.MatchScore:P0} — покупаю {quantity:F2} по рынку",
                [order]);
        }

        if (context.Snapshot.BestBid is null)
        {
            return HoldDecision($"{symbol}: влияние отрицательное, но продавать некому — бидов нет");
        }

        if (!CanSell(quantity))
        {
            return HoldDecision($"{symbol}: влияние отрицательное, но доступной позиции на {quantity:F2} не хватает");
        }

        var sellOrder = CreateMarketOrder(OrderSide.Sell, quantity);
        return new AgentDecision(State.AgentName, TradeAction.Sell,
            $"{symbol}: влияние {newsSignal.ImpactScore:+0.00;-0.00;0.00}, совпадение {newsSignal.MatchScore:P0} — продаю {quantity:F2} по рынку",
            [sellOrder]);
    }

    private static decimal ConvictionMultiplier(decimal conviction) => conviction switch
    {
        >= 1.60m => 2m,
        >= 0.90m => 1.5m,
        >= 0.30m => 1m,
        _ => 0.5m
    };
}
