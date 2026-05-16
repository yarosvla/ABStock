using ABStock.Exchange.Domain;

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

    private static int CompareBuyOrders(Order left, Order right)
    {
        var priceCompare = Nullable.Compare(right.LimitPrice, left.LimitPrice);
        return priceCompare != 0 ? priceCompare : left.CreatedAt.CompareTo(right.CreatedAt);
    }

    private static int CompareSellOrders(Order left, Order right)
    {
        var priceCompare = Nullable.Compare(left.LimitPrice, right.LimitPrice);
        return priceCompare != 0 ? priceCompare : left.CreatedAt.CompareTo(right.CreatedAt);
    }
}
