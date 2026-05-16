using ABStock.Exchange.Domain;
using ABStock.Exchange.Engine;

namespace ABStock.Exchange.Tests;

public sealed class ExchangeEngineTests
{
    [Fact]
    public void Submit_CreatesTradeWhenBestBidCrossesBestAsk()
    {
        var exchange = new ExchangeEngine();

        exchange.Submit(CreateOrder("sell-1", OrderSide.Sell, 99m, quantity: 3m));
        var snapshot = exchange.Submit(CreateOrder("buy-1", OrderSide.Buy, 101m, quantity: 3m));

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(3m, snapshot.Volume);
        Assert.Null(snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Equal([100m, 100m], snapshot.RecentPrices);

        var trade = Assert.Single(snapshot.RecentTrades);
        Assert.Equal("buy-1", trade.BuyOrderId);
        Assert.Equal("sell-1", trade.SellOrderId);
        Assert.Equal(100m, trade.Price);
        Assert.Equal(3m, trade.Quantity);
    }

    [Fact]
    public void Submit_KeepsRemainingQuantityAfterPartialFill()
    {
        var exchange = new ExchangeEngine();

        exchange.Submit(CreateOrder("buy-1", OrderSide.Buy, 101m, quantity: 10m));
        var snapshot = exchange.Submit(CreateOrder("sell-1", OrderSide.Sell, 99m, quantity: 4m));

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(4m, snapshot.Volume);
        Assert.Equal(101m, snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);

        var trade = Assert.Single(snapshot.RecentTrades);
        Assert.Equal(4m, trade.Quantity);
    }

    [Fact]
    public void Submit_DoesNotUpdatePriceWhenOrdersDoNotCross()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        exchange.Submit(CreateOrder("buy-1", OrderSide.Buy, 98m, quantity: 1m));
        var snapshot = exchange.Submit(CreateOrder("sell-1", OrderSide.Sell, 102m, quantity: 1m));

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(98m, snapshot.BestBid);
        Assert.Equal(102m, snapshot.BestAsk);
        Assert.Empty(snapshot.RecentTrades);
        Assert.Equal([100m], snapshot.RecentPrices);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_RejectsOrdersWithNonPositiveQuantity(decimal quantity)
    {
        var exchange = new ExchangeEngine();
        var order = CreateOrder("bad-quantity", OrderSide.Buy, 100m, quantity);

        Assert.Throws<ArgumentException>(() => exchange.Submit(order));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_RejectsLimitOrdersWithNonPositivePrice(decimal price)
    {
        var exchange = new ExchangeEngine();
        var order = CreateOrder("bad-price", OrderSide.Buy, price, quantity: 1m);

        Assert.Throws<ArgumentException>(() => exchange.Submit(order));
    }

    [Fact]
    public void Submit_RejectsMarketOrdersUntilTheyAreImplemented()
    {
        var exchange = new ExchangeEngine();
        var order = new Order
        {
            Id = "market-1",
            AgentId = "agent-1",
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = 1m
        };

        Assert.Throws<NotSupportedException>(() => exchange.Submit(order));
    }

    private static Order CreateOrder(string id, OrderSide side, decimal price, decimal quantity)
    {
        return new Order
        {
            Id = id,
            AgentId = "agent-1",
            Side = side,
            Quantity = quantity,
            LimitPrice = price
        };
    }
}
