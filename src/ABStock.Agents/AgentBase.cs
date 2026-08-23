using ABStock.Agents.Models;
using ABStock.Shared;

namespace ABStock.Agents;

public abstract class AgentBase : ITradeAgent
{
    public AgentState State { get; }

    protected AgentBase(string name, AgentType type, decimal initialCash, decimal initialPosition = 0)
    {
        State = new AgentState
        {
            AgentName = name,
            AgentType = type,
            Cash = initialCash,
            Position = initialPosition,
            InitialCash = initialCash,
            InitialPosition = initialPosition
        };
    }

    public abstract AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal);

    protected bool CanBuy(decimal price, decimal quantity) => State.AvailableCash >= price * quantity;

    protected bool CanSell(decimal quantity) => State.AvailablePosition >= quantity;

    protected Order CreateLimitOrder(OrderSide side, decimal price, decimal quantity) =>
        new(Guid.NewGuid(), State.AgentName, side, OrderType.Limit, price, quantity, DateTimeOffset.UtcNow);

    protected Order CreateMarketOrder(OrderSide side, decimal quantity) =>
        new(Guid.NewGuid(), State.AgentName, side, OrderType.Market, Price: null, quantity, DateTimeOffset.UtcNow);

    protected AgentDecision HoldDecision(string explanation) =>
        new(State.AgentName, TradeAction.Hold, explanation, []);

    /// <summary>
    /// Описывает движение цены так, чтобы на экране никогда не появилось «A → A».
    /// Движение часто происходит в третьем знаке, а цена показывается с двумя:
    /// после округления обе части совпадают, и объяснение теряет смысл
    /// (DESIGN.md 10). В этом случае изменение показывается процентом.
    /// </summary>
    protected static string DescribeMove(decimal from, decimal to)
    {
        var shownFrom = Math.Round(from, 2, MidpointRounding.AwayFromZero);
        var shownTo = Math.Round(to, 2, MidpointRounding.AwayFromZero);

        if (shownFrom != shownTo)
        {
            return $"{shownFrom:F2} → {shownTo:F2}";
        }

        if (from == 0m)
        {
            return $"{shownTo:F2} без изменения";
        }

        var percent = (to - from) / from * 100m;
        var shownPercent = Math.Round(percent, 2, MidpointRounding.AwayFromZero);

        if (shownPercent == 0m)
        {
            return $"{shownTo:F2} без изменения";
        }

        var sign = shownPercent > 0m ? "+" : "\u2212";
        return $"{shownTo:F2} на {sign}{Math.Abs(shownPercent):F2} %";
    }
}
