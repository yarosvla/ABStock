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
            Position = initialPosition
        };
    }

    public abstract AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal);

    protected bool CanBuy(decimal price, decimal quantity) => State.Cash >= price * quantity;

    protected bool CanSell(decimal quantity) => State.Position >= quantity;

    protected Order CreateOrder(OrderSide side, decimal price, decimal quantity) =>
        new(Guid.NewGuid(), State.AgentName, side, OrderType.Limit, price, quantity, DateTimeOffset.UtcNow);

    protected AgentDecision HoldDecision(string explanation) =>
        new(State.AgentName, TradeAction.Hold, explanation, []);
}
