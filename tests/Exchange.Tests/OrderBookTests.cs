using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Exchange.Tests;

public sealed class OrderBookTests
{
    [Fact]
    public void Add_SortsBuyOrdersByHighestPriceFirst()
    {
        var orderBook = new OrderBook();

        orderBook.Add(CreateOrder(1, OrderSide.Buy, 100m));
        orderBook.Add(CreateOrder(2, OrderSide.Buy, 105m));
        orderBook.Add(CreateOrder(3, OrderSide.Buy, 102m));

        Assert.Equal(CreateId(2), orderBook.BestBid?.Id);
        Assert.Equal(new decimal?[] { 105m, 102m, 100m }, orderBook.BuyOrders.Select(order => order.Price));
    }

    [Fact]
    public void Add_SortsSellOrdersByLowestPriceFirst()
    {
        var orderBook = new OrderBook();

        orderBook.Add(CreateOrder(1, OrderSide.Sell, 100m));
        orderBook.Add(CreateOrder(2, OrderSide.Sell, 95m));
        orderBook.Add(CreateOrder(3, OrderSide.Sell, 98m));

        Assert.Equal(CreateId(2), orderBook.BestAsk?.Id);
        Assert.Equal(new decimal?[] { 95m, 98m, 100m }, orderBook.SellOrders.Select(order => order.Price));
    }

    [Fact]
    public void GetSnapshot_GroupsOrdersByPriceAndLimitsDepth()
    {
        var orderBook = new OrderBook();

        orderBook.Add(CreateOrder(1, OrderSide.Buy, 105m, quantity: 1m));
        orderBook.Add(CreateOrder(2, OrderSide.Buy, 105m, quantity: 2m));
        orderBook.Add(CreateOrder(3, OrderSide.Buy, 103m, quantity: 4m));
        orderBook.Add(CreateOrder(4, OrderSide.Buy, 101m, quantity: 8m));
        orderBook.Add(CreateOrder(5, OrderSide.Sell, 106m, quantity: 1m));
        orderBook.Add(CreateOrder(6, OrderSide.Sell, 106m, quantity: 5m));
        orderBook.Add(CreateOrder(7, OrderSide.Sell, 108m, quantity: 2m));
        orderBook.Add(CreateOrder(8, OrderSide.Sell, 110m, quantity: 3m));

        var snapshot = orderBook.GetSnapshot(depth: 2);

        Assert.Equal(2, snapshot.Bids.Count);
        Assert.Equal(105m, snapshot.Bids[0].Price);
        Assert.Equal(3m, snapshot.Bids[0].Quantity);
        Assert.Equal(2, snapshot.Bids[0].OrdersCount);
        Assert.Equal(103m, snapshot.Bids[1].Price);
        Assert.Equal(4m, snapshot.Bids[1].Quantity);
        Assert.Equal(1, snapshot.Bids[1].OrdersCount);

        Assert.Equal(2, snapshot.Asks.Count);
        Assert.Equal(106m, snapshot.Asks[0].Price);
        Assert.Equal(6m, snapshot.Asks[0].Quantity);
        Assert.Equal(2, snapshot.Asks[0].OrdersCount);
        Assert.Equal(108m, snapshot.Asks[1].Price);
        Assert.Equal(2m, snapshot.Asks[1].Quantity);
        Assert.Equal(1, snapshot.Asks[1].OrdersCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void GetSnapshot_RejectsNonPositiveDepth(int depth)
    {
        var orderBook = new OrderBook();

        Assert.Throws<ArgumentOutOfRangeException>(() => orderBook.GetSnapshot(depth));
    }

    private static Order CreateOrder(int id, OrderSide side, decimal price, decimal quantity = 1m)
    {
        return new Order(
            Id: CreateId(id),
            AgentName: "agent-1",
            Side: side,
            Type: OrderType.Limit,
            Price: price,
            Quantity: quantity,
            CreatedAt: DateTimeOffset.UtcNow
        );
    }

    private static Guid CreateId(int id)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
    }
}
