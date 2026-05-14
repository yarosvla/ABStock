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
