using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Exchange.Tests;

public sealed class ExchangeEngineTests
{
    [Fact]
    public void Submit_CreatesTradeWhenBestBidCrossesBestAsk()
    {
        var exchange = new ExchangeEngine();

        var sellOrderId = CreateId(1);
        var buyOrderId = CreateId(2);

        exchange.Submit(CreateOrder(sellOrderId, OrderSide.Sell, 99m, quantity: 3m));
        var snapshot = exchange.Submit(CreateOrder(buyOrderId, OrderSide.Buy, 101m, quantity: 3m));

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(3m, snapshot.Volume);
        Assert.Null(snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Equal([100m, 100m], snapshot.RecentPrices);

        var trade = Assert.Single(snapshot.RecentTrades);
        Assert.Equal(buyOrderId, trade.BuyOrderId);
        Assert.Equal(sellOrderId, trade.SellOrderId);
        Assert.Equal(100m, trade.Price);
        Assert.Equal(3m, trade.Quantity);
    }

    [Fact]
    public void Submit_KeepsRemainingQuantityAfterPartialFill()
    {
        var exchange = new ExchangeEngine();

        exchange.Submit(CreateOrder(CreateId(1), OrderSide.Buy, 101m, quantity: 10m));
        var snapshot = exchange.Submit(CreateOrder(CreateId(2), OrderSide.Sell, 99m, quantity: 4m));

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

        exchange.Submit(CreateOrder(CreateId(1), OrderSide.Buy, 98m, quantity: 1m));
        var snapshot = exchange.Submit(CreateOrder(CreateId(2), OrderSide.Sell, 102m, quantity: 1m));

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
        var order = CreateOrder(CreateId(1), OrderSide.Buy, 100m, quantity);

        Assert.Throws<ArgumentException>(() => exchange.Submit(order));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_RejectsLimitOrdersWithNonPositivePrice(decimal price)
    {
        var exchange = new ExchangeEngine();
        var order = CreateOrder(CreateId(1), OrderSide.Buy, price, quantity: 1m);

        Assert.Throws<ArgumentException>(() => exchange.Submit(order));
    }

    [Fact]
    public void Submit_RejectsMarketOrdersUntilTheyAreImplemented()
    {
        var exchange = new ExchangeEngine();
        var order = new Order(
            Id: CreateId(1),
            AgentName: "agent-1",
            Side: OrderSide.Buy,
            Type: OrderType.Market,
            Price: null,
            Quantity: 1m,
            CreatedAt: DateTimeOffset.UtcNow
        );

        Assert.Throws<NotSupportedException>(() => exchange.Submit(order));
    }

    private static Order CreateOrder(Guid id, OrderSide side, decimal price, decimal quantity)
    {
        return new Order(
            Id: id,
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
