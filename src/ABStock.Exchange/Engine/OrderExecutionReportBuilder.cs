using ABStock.Shared;

namespace ABStock.Exchange.Engine;

public static class OrderExecutionReportBuilder
{
    public static IReadOnlyList<OrderExecutionReport> Build(
        IReadOnlyList<Order> acceptedOrders,
        IReadOnlyList<Trade> trades,
        IReadOnlyList<Order> openOrders)
    {
        var openOrdersById = openOrders.ToDictionary(order => order.Id);
        return acceptedOrders.Select(order => BuildReport(order, trades, openOrdersById)).ToArray();
    }

    public static OrderExecutionReport CreateRejected(Order? order, string reason) =>
        new(order, OrderExecutionStatus.Rejected, order?.Quantity ?? 0m, 0m,
            order?.Quantity ?? 0m, null, reason);

    private static OrderExecutionReport BuildReport(
        Order order,
        IReadOnlyList<Trade> trades,
        IReadOnlyDictionary<Guid, Order> openOrdersById)
    {
        var orderTrades = trades.Where(trade => trade.BuyOrderId == order.Id || trade.SellOrderId == order.Id).ToArray();
        var filledQuantity = orderTrades.Sum(trade => trade.Quantity);
        var openQuantity = openOrdersById.TryGetValue(order.Id, out var openOrder) ? openOrder.Quantity : 0m;
        var remainingQuantity = openQuantity > 0m ? openQuantity : Math.Max(0m, order.Quantity - filledQuantity);
        var averagePrice = filledQuantity > 0m
            ? orderTrades.Sum(trade => trade.Price * trade.Quantity) / filledQuantity
            : (decimal?)null;

        return new OrderExecutionReport(order, GetStatus(order, filledQuantity, openQuantity),
            order.Quantity, filledQuantity, remainingQuantity, averagePrice, null);
    }

    private static OrderExecutionStatus GetStatus(Order order, decimal filledQuantity, decimal openQuantity)
    {
        if (filledQuantity >= order.Quantity)
        {
            return OrderExecutionStatus.Filled;
        }

        if (filledQuantity > 0m)
        {
            return OrderExecutionStatus.PartiallyFilled;
        }

        return openQuantity > 0m ? OrderExecutionStatus.Open : OrderExecutionStatus.Expired;
    }
}
