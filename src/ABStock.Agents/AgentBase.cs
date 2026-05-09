using ABStock.Agents.Models;

namespace ABStock.Agents;

public abstract class AgentBase : ITradeAgent
{
    public AgentState State { get; }

    protected AgentBase(string name, AgentType type, decimal initialCash)
    {
        State = new AgentState
        {
            AgentName = name,
            AgentType = type,
            Cash = initialCash,
            Position = 0
        };
    }

    public abstract AgentDecision Decide();

    protected bool CanBuy(decimal price, int quantity)
    {
        return State.Cash >= price * quantity;
    }

    protected bool CanSell(int quantity)
    {
        return State.Position >= quantity;
    }

    protected TradeProposal CreateProposal(TradeSide side, decimal price, int quantity)
    {
        return new TradeProposal
        {
            Side = side,
            Price = price,
            Quantity = quantity
        };
    }

    protected AgentDecision HoldDecision(string explanation)
    {
        return new AgentDecision
        {
            Action = TradeAction.Hold,
            Explanation = explanation
        };
    }
}
