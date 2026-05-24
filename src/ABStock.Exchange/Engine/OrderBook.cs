using ABStock.Shared;

namespace ABStock.Exchange.Engine;

public sealed class OrderBook
{
    private readonly List<Order> _buyOrders = [];
    private readonly List<Order> _sellOrders = [];

    public IReadOnlyList<Order> BuyOrders => _buyOrders;

    public IReadOnlyList<Order> SellOrders => _sellOrders;

    public Order? BestBid => _buyOrders.FirstOrDefault();

    public Order? BestAsk => _sellOrders.FirstOrDefault();

    public void Add(Order order)
    {
        if (order.Side == OrderSide.Buy)
        {
            _buyOrders.Add(order);
            _buyOrders.Sort(CompareBuyOrders);
            return;
        }

        _sellOrders.Add(order);
        _sellOrders.Sort(CompareSellOrders);
    }

    public void ReduceBestBidBy(decimal quantity)
    {
        ReduceBestOrderBy(_buyOrders, quantity);
    }

    public void ReduceBestAskBy(decimal quantity)
    {
        ReduceBestOrderBy(_sellOrders, quantity);
    }

    public (Order BuyOrder, Order SellOrder)? FindBestMatch()
    {
        foreach (var buyOrder in _buyOrders)
        {
            if (buyOrder.Price is null)
            {
                continue;
            }

            var sellOrder = _sellOrders.FirstOrDefault(sell =>
                sell.Price is not null &&
                sell.Price <= buyOrder.Price &&
                !IsSameAgent(buyOrder, sell));

            if (sellOrder is not null)
            {
                return (buyOrder, sellOrder);
            }
        }

        return null;
    }

    public void Reduce(Order order, decimal quantity)
    {
        if (order.Side == OrderSide.Buy)
        {
            ReduceOrderBy(_buyOrders, order, quantity);
            return;
        }

        ReduceOrderBy(_sellOrders, order, quantity);
    }

    public OrderBookSnapshot GetSnapshot(int depth = 5)
    {
        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Order book depth must be positive.");
        }

        return new OrderBookSnapshot(
            Bids: BuildLevels(_buyOrders, depth),
            Asks: BuildLevels(_sellOrders, depth)
        );
    }

    private static void ReduceBestOrderBy(List<Order> orders, decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        if (orders.Count == 0)
        {
            throw new InvalidOperationException("Order book side is empty.");
        }

        var bestOrder = orders[0];
        if (quantity > bestOrder.Quantity)
        {
            throw new InvalidOperationException("Cannot reduce order by more than its current quantity.");
        }

        var remainingQuantity = bestOrder.Quantity - quantity;
        if (remainingQuantity == 0)
        {
            orders.RemoveAt(0);
            return;
        }

        orders[0] = bestOrder with { Quantity = remainingQuantity };
    }

    private static void ReduceOrderBy(List<Order> orders, Order order, decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        var orderIndex = orders.FindIndex(existing => existing.Id == order.Id);
        if (orderIndex < 0)
        {
            throw new InvalidOperationException("Order is not present in the order book.");
        }

        var currentOrder = orders[orderIndex];
        if (quantity > currentOrder.Quantity)
        {
            throw new InvalidOperationException("Cannot reduce order by more than its current quantity.");
        }

        var remainingQuantity = currentOrder.Quantity - quantity;
        if (remainingQuantity == 0)
        {
            orders.RemoveAt(orderIndex);
            return;
        }

        orders[orderIndex] = currentOrder with { Quantity = remainingQuantity };
    }

    private static IReadOnlyList<OrderBookLevel> BuildLevels(IReadOnlyList<Order> orders, int depth)
    {
        return orders
            .Where(order => order.Price is not null)
            .GroupBy(order => order.Price!.Value)
            .Take(depth)
            .Select(level => new OrderBookLevel(
                Price: level.Key,
                Quantity: level.Sum(order => order.Quantity),
                OrdersCount: level.Count()
            ))
            .ToArray();
    }

    private static int CompareBuyOrders(Order left, Order right)
    {
        var priceCompare = Nullable.Compare(right.Price, left.Price);
        return priceCompare != 0 ? priceCompare : left.CreatedAt.CompareTo(right.CreatedAt);
    }

    private static int CompareSellOrders(Order left, Order right)
    {
        var priceCompare = Nullable.Compare(left.Price, right.Price);
        return priceCompare != 0 ? priceCompare : left.CreatedAt.CompareTo(right.CreatedAt);
    }

    private static bool IsSameAgent(Order left, Order right)
    {
        return string.Equals(left.AgentName, right.AgentName, StringComparison.Ordinal);
    }
}
