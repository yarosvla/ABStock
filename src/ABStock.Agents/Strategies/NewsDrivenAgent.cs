using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class NewsDrivenAgent : AgentBase
{
    private readonly decimal _baseQuantity;
    private readonly decimal _minStrength;
    private readonly decimal _maxMultiplier;

    public NewsDrivenAgent(
        decimal initialCash,
        decimal initialPosition = 0,
        decimal baseQuantity = 1m,
        decimal minStrength = 0.1m,
        decimal maxMultiplier = 5m)
        : base("NewsDriven", AgentType.NewsDriven, initialCash, initialPosition)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseQuantity);
        ArgumentOutOfRangeException.ThrowIfNegative(minStrength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minStrength, 1m);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxMultiplier, 1m);

        _baseQuantity = baseQuantity;
        _minStrength = minStrength;
        _maxMultiplier = maxMultiplier;
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        if (newsSignal is null)
            return HoldDecision("No news");

        if (newsSignal.Polarity == SignalPolarity.Neutral)
            return HoldDecision("News is Neutral, holding");

        var strength = newsSignal.Confidence * newsSignal.ImpactScore;
        if (strength < _minStrength)
            return HoldDecision($"News too weak (strength={strength:F2})");

        var quantity = Math.Ceiling(_baseQuantity * (1 + strength * (_maxMultiplier - 1)));

        if (newsSignal.Polarity == SignalPolarity.Positive)
        {
            if (snapshot.BestAsk is null)
                return HoldDecision("Positive news, but no asks to buy from");

            if (!CanBuy(snapshot.BestAsk.Value, quantity))
                return HoldDecision("Positive news, but insufficient funds");

            var order = CreateMarketOrder(OrderSide.Buy, quantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Positive news (strength={strength:F2}), market buy {quantity}", [order]);
        }

        if (snapshot.BestBid is null)
            return HoldDecision("Negative news, but no bids to sell into");

        if (!CanSell(quantity))
            return HoldDecision("Negative news, but insufficient position");

        var sellOrder = CreateMarketOrder(OrderSide.Sell, quantity);
        return new AgentDecision(State.AgentName, TradeAction.Sell,
            $"Negative news (strength={strength:F2}), market sell {quantity}", [sellOrder]);
    }
}
