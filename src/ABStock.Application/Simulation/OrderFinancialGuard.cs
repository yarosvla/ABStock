using ABStock.Agents;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

internal static class FinancialOrderSubmission
{
    public static SubmitResult Submit(
        IExchangeEngine exchange,
        IReadOnlyList<ITradeAgent> agents,
        IReadOnlyList<Order> orders,
        MarketSnapshot snapshot)
    {
        var checkResult = OrderFinancialGuard.Filter(orders, agents, snapshot);

        if (checkResult.AcceptedOrders.Count == 0)
        {
            return new SubmitResult(
                snapshot,
                Trades: [],
                AcceptedOrders: [],
                RejectedOrders: checkResult.RejectedOrders
            );
        }

        var submitResult = exchange.SubmitManyWithResult(checkResult.AcceptedOrders);
        if (checkResult.RejectedOrders.Count == 0)
        {
            return submitResult;
        }

        return submitResult with
        {
            RejectedOrders = checkResult.RejectedOrders
                .Concat(submitResult.RejectedOrders)
                .ToArray()
        };
    }
}

internal static class OrderFinancialGuard
{
    public static FinancialOrderCheckResult Filter(
        IReadOnlyList<Order> orders,
        IReadOnlyList<ITradeAgent> agents,
        MarketSnapshot snapshot)
    {
        var budgets = agents.ToDictionary(
            agent => agent.State.AgentName,
            agent => new AgentBudget(agent.State.AvailableCash, agent.State.AvailablePosition),
            StringComparer.Ordinal);

        var acceptedOrders = new List<Order>();
        var rejectedOrders = new List<RejectedOrder>();

        foreach (var order in orders)
        {
            if (CanReserve(order, budgets, snapshot, out var reason))
            {
                acceptedOrders.Add(order);
                continue;
            }

            rejectedOrders.Add(new RejectedOrder(order, reason));
        }

        return new FinancialOrderCheckResult(
            acceptedOrders.ToArray(),
            rejectedOrders.ToArray()
        );
    }

    private static bool CanReserve(
        Order order,
        Dictionary<string, AgentBudget> budgets,
        MarketSnapshot snapshot,
        out string reason)
    {
        reason = string.Empty;

        if (!budgets.TryGetValue(order.AgentName, out var budget))
        {
            reason = $"Agent '{order.AgentName}' is not available for financial validation.";
            return false;
        }

        if (order.Quantity <= 0)
        {
            return true;
        }

        if (order.Side == OrderSide.Buy)
        {
            return CanReserveCash(order, snapshot, budget, out reason);
        }

        if (order.Side == OrderSide.Sell)
        {
            return CanReservePosition(order, budget, out reason);
        }

        return true;
    }

    private static bool CanReserveCash(
        Order order,
        MarketSnapshot snapshot,
        AgentBudget budget,
        out string reason)
    {
        reason = string.Empty;

        if (!TryGetEstimatedBuyPrice(order, snapshot, out var estimatedPrice))
        {
            return true;
        }

        var requiredCash = estimatedPrice * order.Quantity;
        if (budget.Cash < requiredCash)
        {
            reason = $"Insufficient cash for order. Required {requiredCash}, available {budget.Cash}.";
            return false;
        }

        budget.Cash -= requiredCash;
        return true;
    }

    private static bool CanReservePosition(
        Order order,
        AgentBudget budget,
        out string reason)
    {
        reason = string.Empty;

        if (budget.Position < order.Quantity)
        {
            reason = $"Insufficient position for order. Required {order.Quantity}, available {budget.Position}.";
            return false;
        }

        budget.Position -= order.Quantity;
        return true;
    }

    private static bool TryGetEstimatedBuyPrice(
        Order order,
        MarketSnapshot snapshot,
        out decimal estimatedPrice)
    {
        estimatedPrice = 0m;

        if (order.Type == OrderType.Limit)
        {
            if (order.Price is null)
            {
                return false;
            }

            estimatedPrice = order.Price.Value;
            return true;
        }

        if (order.Type == OrderType.Market)
        {
            estimatedPrice = snapshot.BestAsk ?? snapshot.LastPrice;
            return true;
        }

        return false;
    }

    private sealed class AgentBudget(decimal cash, decimal position)
    {
        public decimal Cash { get; set; } = cash;
        public decimal Position { get; set; } = position;
    }
}

internal sealed record FinancialOrderCheckResult(
    IReadOnlyList<Order> AcceptedOrders,
    IReadOnlyList<RejectedOrder> RejectedOrders
);
