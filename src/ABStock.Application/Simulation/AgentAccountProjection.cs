using ABStock.Agents;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

internal static class AgentAccountProjection
{
    public static void Apply(ITradeAgent agent, AgentAccountSnapshot account, Guid assetId)
    {
        var position = account.Positions[assetId];
        agent.State.AgentName = account.AgentName;
        agent.State.AgentType = account.AgentType;
        agent.State.Cash = account.Cash;
        agent.State.ReservedCash = account.ReservedCash;
        agent.State.Position = position.Quantity;
        agent.State.ReservedPosition = position.ReservedQuantity;
        agent.State.InitialCash = account.InitialCash;
        agent.State.InitialPosition = position.InitialQuantity;
        agent.State.InitialPortfolioValue = account.InitialPortfolioValue;
    }
}
