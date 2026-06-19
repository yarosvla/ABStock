using ABStock.Agents;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

internal static class AgentReservations
{
    public static void Refresh(IExchangeEngine exchange, IReadOnlyList<ITradeAgent> agents)
    {
        var reservedCash = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var reservedPosition = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var order in exchange.GetOpenOrders())
        {
            if (order.Side == OrderSide.Buy)
            {
                reservedCash[order.AgentName] =
                    reservedCash.GetValueOrDefault(order.AgentName) + (order.Price ?? 0m) * order.Quantity;
            }
            else
            {
                reservedPosition[order.AgentName] =
                    reservedPosition.GetValueOrDefault(order.AgentName) + order.Quantity;
            }
        }

        foreach (var agent in agents)
        {
            agent.State.ReservedCash = reservedCash.GetValueOrDefault(agent.State.AgentName);
            agent.State.ReservedPosition = reservedPosition.GetValueOrDefault(agent.State.AgentName);
        }
    }
}
