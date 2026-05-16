using ABStock.Exchange.Domain;
using ABStock.Exchange.Engine;

namespace ABStock.Exchange.Tests;

public sealed class OrderBookTests
{
    [Fact]
    public void Add_SortsBuyOrdersByHighestPriceFirst()
    {
        var orderBook = new OrderBook();

        orderBook.Add(CreateOrder("buy-100", OrderSide.Buy, 100m));
        orderBook.Add(CreateOrder("buy-105", OrderSide.Buy, 105m));
        orderBook.Add(CreateOrder("buy-102", OrderSide.Buy, 102m));

        Assert.Equal("buy-105", orderBook.BestBid?.Id);
        Assert.Equal([105m, 102m, 100m], orderBook.BuyOrders.Select(order => order.LimitPrice));
    }

    [Fact]
    public void Add_SortsSellOrdersByLowestPriceFirst()
    {
        var orderBook = new OrderBook();

        orderBook.Add(CreateOrder("sell-100", OrderSide.Sell, 100m));
        orderBook.Add(CreateOrder("sell-95", OrderSide.Sell, 95m));
        orderBook.Add(CreateOrder("sell-98", OrderSide.Sell, 98m));

        Assert.Equal("sell-95", orderBook.BestAsk?.Id);
        Assert.Equal([95m, 98m, 100m], orderBook.SellOrders.Select(order => order.LimitPrice));
    }

    private static Order CreateOrder(string id, OrderSide side, decimal price)
    {
        return new Order
        {
            Id = id,
            AgentId = "agent-1",
            Side = side,
            Quantity = 1m,
            LimitPrice = price
        };
    }
}
