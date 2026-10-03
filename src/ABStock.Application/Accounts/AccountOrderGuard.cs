using ABStock.Shared;

namespace ABStock.Application.Accounts;

internal static class AccountOrderGuard
{
    public static string? GetRejectionReason(
        Guid assetId,
        Order order,
        AgentAccount account,
        IReadOnlyList<Order> openOrders)
    {
        if (order.Side == OrderSide.Sell)
        {
            var available = account.GetAvailablePosition(assetId);
            return order.Quantity > available
                ? $"Insufficient position for asset '{assetId}'. Required {order.Quantity}, available {available}."
                : null;
        }

        decimal requiredCash;
        try
        {
            requiredCash = order.Type == OrderType.Limit
                ? order.Price!.Value * order.Quantity
                : GetMarketBuyCost(order, openOrders);
        }
        catch (OverflowException)
        {
            return "Order cost is outside the supported decimal range.";
        }

        return requiredCash > account.AvailableCash
            ? $"Insufficient cash for order. Required {requiredCash}, available {account.AvailableCash}."
            : null;
    }

    private static decimal GetMarketBuyCost(Order order, IReadOnlyList<Order> openOrders)
    {
        var remaining = order.Quantity;
        var cost = 0m;
        var asks = openOrders
            .Where(resting => resting.Side == OrderSide.Sell &&
                !string.Equals(resting.AgentName, order.AgentName, StringComparison.Ordinal))
            .OrderBy(resting => resting.Price);

        foreach (var ask in asks)
        {
            var quantity = Math.Min(remaining, ask.Quantity);
            cost += ask.Price!.Value * quantity;
            remaining -= quantity;
            if (remaining == 0m)
            {
                break;
            }
        }

        return cost;
    }
}
