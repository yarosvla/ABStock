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

    private static Order CreateOrder(int id, OrderSide side, decimal price)
    {
        return new Order(
            Id: CreateId(id),
            AgentName: "agent-1",
            Side: side,
            Type: OrderType.Limit,
            Price: price,
            Quantity: 1m,
            CreatedAt: DateTimeOffset.UtcNow
        );
    }

    private static Guid CreateId(int id)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
    }
}
