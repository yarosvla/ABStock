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

        exchange.Submit(CreateOrder(sellOrderId, OrderSide.Sell, 99m, quantity: 3m, agentName: "seller-agent"));
        var snapshot = exchange.Submit(CreateOrder(buyOrderId, OrderSide.Buy, 101m, quantity: 3m, agentName: "buyer-agent"));

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(3m, snapshot.Volume);
        Assert.Null(snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Equal([100m, 100m], snapshot.RecentPrices);

        var trade = Assert.Single(snapshot.RecentTrades);
        Assert.Equal(buyOrderId, trade.BuyOrderId);
        Assert.Equal(sellOrderId, trade.SellOrderId);
        Assert.Equal("buyer-agent", trade.BuyerAgentName);
        Assert.Equal("seller-agent", trade.SellerAgentName);
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

    [Fact]
    public void SubmitMany_ProcessesOrderBatchAndReturnsFinalSnapshot()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        var snapshot = exchange.SubmitMany(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 101m, quantity: 2m),
            CreateOrder(CreateId(2), OrderSide.Sell, 99m, quantity: 2m),
            CreateOrder(CreateId(3), OrderSide.Sell, 103m, quantity: 1m)
        ]);

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(2m, snapshot.Volume);
        Assert.Null(snapshot.BestBid);
        Assert.Equal(103m, snapshot.BestAsk);
        Assert.Equal([100m, 100m], snapshot.RecentPrices);

        var trade = Assert.Single(snapshot.RecentTrades);
        Assert.Equal(CreateId(1), trade.BuyOrderId);
        Assert.Equal(CreateId(2), trade.SellOrderId);
    }

    [Fact]
    public void SubmitMany_MatchesMultipleAgentOrdersAndUpdatesFinalSnapshot()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        var snapshot = exchange.SubmitMany(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 110m, quantity: 5m, agentName: "trend-agent"),
            CreateOrder(CreateId(2), OrderSide.Sell, 100m, quantity: 2m, agentName: "market-maker"),
            CreateOrder(CreateId(3), OrderSide.Sell, 106m, quantity: 1m, agentName: "news-agent")
        ]);

        Assert.Equal(108m, snapshot.LastPrice);
        Assert.Equal(3m, snapshot.Volume);
        Assert.Equal(110m, snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Equal([100m, 105m, 108m], snapshot.RecentPrices);

        Assert.Collection(
            snapshot.RecentTrades,
            firstTrade =>
            {
                Assert.Equal(CreateId(1), firstTrade.BuyOrderId);
                Assert.Equal(CreateId(2), firstTrade.SellOrderId);
                Assert.Equal("trend-agent", firstTrade.BuyerAgentName);
                Assert.Equal("market-maker", firstTrade.SellerAgentName);
                Assert.Equal(105m, firstTrade.Price);
                Assert.Equal(2m, firstTrade.Quantity);
            },
            secondTrade =>
            {
                Assert.Equal(CreateId(1), secondTrade.BuyOrderId);
                Assert.Equal(CreateId(3), secondTrade.SellOrderId);
                Assert.Equal("trend-agent", secondTrade.BuyerAgentName);
                Assert.Equal("news-agent", secondTrade.SellerAgentName);
                Assert.Equal(108m, secondTrade.Price);
                Assert.Equal(1m, secondTrade.Quantity);
            });

        var orderBook = exchange.GetOrderBookSnapshot();
        var bid = Assert.Single(orderBook.Bids);
        Assert.Equal(110m, bid.Price);
        Assert.Equal(2m, bid.Quantity);
        Assert.Equal(1, bid.OrdersCount);
        Assert.Empty(orderBook.Asks);
    }

    [Fact]
    public void SubmitMany_RejectsWholeBatchWhenAnyOrderIsInvalid()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        var exception = Assert.Throws<ArgumentException>(() => exchange.SubmitMany(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 101m, quantity: 1m),
            CreateOrder(CreateId(2), OrderSide.Sell, 99m, quantity: 0m)
        ]));

        var snapshot = exchange.GetSnapshot();

        Assert.Contains("quantity", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Null(snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Empty(snapshot.RecentTrades);
        Assert.Equal([100m], snapshot.RecentPrices);
    }

    [Fact]
    public void SubmitManyWithResult_ReturnsAcceptedRejectedTradesAndSnapshot()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        var result = exchange.SubmitManyWithResult(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 101m, quantity: 1m, agentName: "buyer-agent"),
            CreateOrder(CreateId(2), OrderSide.Sell, 99m, quantity: 1m, agentName: "seller-agent"),
            CreateOrder(CreateId(3), OrderSide.Sell, 99m, quantity: 0m, agentName: "invalid-agent")
        ]);

        Assert.Equal(2, result.AcceptedOrders.Count);

        var rejectedOrder = Assert.Single(result.RejectedOrders);
        Assert.Equal(CreateId(3), rejectedOrder.Order?.Id);
        Assert.Contains("quantity", rejectedOrder.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(100m, result.Snapshot.LastPrice);
        Assert.Equal(1m, result.Snapshot.Volume);
        Assert.Null(result.Snapshot.BestBid);
        Assert.Null(result.Snapshot.BestAsk);

        var trade = Assert.Single(result.Trades);
        Assert.Equal(CreateId(1), trade.BuyOrderId);
        Assert.Equal(CreateId(2), trade.SellOrderId);
        Assert.Equal("buyer-agent", trade.BuyerAgentName);
        Assert.Equal("seller-agent", trade.SellerAgentName);
        Assert.Equal(100m, trade.Price);
        Assert.Equal(1m, trade.Quantity);
    }

    [Fact]
    public void SubmitManyWithResult_RejectsLimitOrderThatWouldSelfTrade()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        var result = exchange.SubmitManyWithResult(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 101m, quantity: 2m, agentName: "agent-a"),
            CreateOrder(CreateId(2), OrderSide.Sell, 99m, quantity: 2m, agentName: "agent-a")
        ]);

        var snapshot = result.Snapshot;

        var acceptedOrder = Assert.Single(result.AcceptedOrders);
        Assert.Equal(CreateId(1), acceptedOrder.Id);

        var rejectedOrder = Assert.Single(result.RejectedOrders);
        Assert.Equal(CreateId(2), rejectedOrder.Order?.Id);
        Assert.Contains("same agent", rejectedOrder.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(0m, snapshot.Volume);
        Assert.Equal(101m, snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Empty(snapshot.RecentTrades);
        Assert.Equal([100m], snapshot.RecentPrices);

        var orderBook = exchange.GetOrderBookSnapshot();
        var bid = Assert.Single(orderBook.Bids);
        Assert.Equal(101m, bid.Price);
        Assert.Empty(orderBook.Asks);
    }

    [Fact]
    public void SubmitManyWithResult_RejectsSelfTradeAndMatchesCompatibleOtherAgentOrder()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        var result = exchange.SubmitManyWithResult(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 105m, quantity: 1m, agentName: "agent-a"),
            CreateOrder(CreateId(2), OrderSide.Sell, 99m, quantity: 1m, agentName: "agent-a"),
            CreateOrder(CreateId(3), OrderSide.Sell, 101m, quantity: 1m, agentName: "agent-b")
        ]);

        var snapshot = result.Snapshot;

        Assert.Equal(2, result.AcceptedOrders.Count);
        var rejectedOrder = Assert.Single(result.RejectedOrders);
        Assert.Equal(CreateId(2), rejectedOrder.Order?.Id);
        Assert.Contains("same agent", rejectedOrder.Reason, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(103m, snapshot.LastPrice);
        Assert.Equal(1m, snapshot.Volume);
        Assert.Null(snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);

        var trade = Assert.Single(snapshot.RecentTrades);
        Assert.Equal(CreateId(1), trade.BuyOrderId);
        Assert.Equal(CreateId(3), trade.SellOrderId);
        Assert.Equal("agent-a", trade.BuyerAgentName);
        Assert.Equal("agent-b", trade.SellerAgentName);
    }

    [Fact]
    public void GetOrderBookSnapshot_ReturnsCurrentOrderBookDepth()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        exchange.SubmitMany(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 98m, quantity: 2m),
            CreateOrder(CreateId(2), OrderSide.Buy, 97m, quantity: 3m),
            CreateOrder(CreateId(3), OrderSide.Sell, 102m, quantity: 4m),
            CreateOrder(CreateId(4), OrderSide.Sell, 103m, quantity: 5m)
        ]);

        var orderBook = exchange.GetOrderBookSnapshot(depth: 1);

        var bid = Assert.Single(orderBook.Bids);
        Assert.Equal(98m, bid.Price);
        Assert.Equal(2m, bid.Quantity);
        Assert.Equal(1, bid.OrdersCount);

        var ask = Assert.Single(orderBook.Asks);
        Assert.Equal(102m, ask.Price);
        Assert.Equal(4m, ask.Quantity);
        Assert.Equal(1, ask.OrdersCount);
    }

    [Fact]
    public void Submit_TrimsRecentPricesAndTradesToConfiguredLimits()
    {
        var exchange = new ExchangeEngine(
            startPrice: 100m,
            maxRecentPrices: 3,
            maxRecentTrades: 2);

        exchange.SubmitMany(
        [
            CreateOrder(CreateId(1), OrderSide.Buy, 102m, quantity: 1m),
            CreateOrder(CreateId(2), OrderSide.Buy, 104m, quantity: 1m),
            CreateOrder(CreateId(3), OrderSide.Buy, 106m, quantity: 1m),
            CreateOrder(CreateId(4), OrderSide.Sell, 100m, quantity: 1m),
            CreateOrder(CreateId(5), OrderSide.Sell, 100m, quantity: 1m),
            CreateOrder(CreateId(6), OrderSide.Sell, 100m, quantity: 1m)
        ]);

        var snapshot = exchange.GetSnapshot();

        Assert.Equal(3m, snapshot.Volume);
        Assert.Equal([103m, 102m, 101m], snapshot.RecentPrices);
        Assert.Equal(2, snapshot.RecentTrades.Count);
        Assert.All(snapshot.RecentTrades, trade => Assert.Equal(1m, trade.Quantity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveRecentPricesLimit(int maxRecentPrices)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExchangeEngine(maxRecentPrices: maxRecentPrices));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveRecentTradesLimit(int maxRecentTrades)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ExchangeEngine(maxRecentTrades: maxRecentTrades));
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
    public void Submit_MarketBuyConsumesBestAsksAndDoesNotRestInOrderBook()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        exchange.Submit(CreateOrder(CreateId(1), OrderSide.Sell, 101m, quantity: 1m, agentName: "seller-a"));
        exchange.Submit(CreateOrder(CreateId(2), OrderSide.Sell, 102m, quantity: 3m, agentName: "seller-b"));

        var snapshot = exchange.Submit(CreateMarketOrder(CreateId(3), OrderSide.Buy, quantity: 3m, agentName: "buyer-agent"));

        Assert.Equal(102m, snapshot.LastPrice);
        Assert.Equal(3m, snapshot.Volume);
        Assert.Null(snapshot.BestBid);
        Assert.Equal(102m, snapshot.BestAsk);
        Assert.Equal([100m, 101m, 102m], snapshot.RecentPrices);

        Assert.Collection(
            snapshot.RecentTrades,
            firstTrade =>
            {
                Assert.Equal(CreateId(3), firstTrade.BuyOrderId);
                Assert.Equal(CreateId(1), firstTrade.SellOrderId);
                Assert.Equal(101m, firstTrade.Price);
                Assert.Equal(1m, firstTrade.Quantity);
            },
            secondTrade =>
            {
                Assert.Equal(CreateId(3), secondTrade.BuyOrderId);
                Assert.Equal(CreateId(2), secondTrade.SellOrderId);
                Assert.Equal(102m, secondTrade.Price);
                Assert.Equal(2m, secondTrade.Quantity);
            });

        var orderBook = exchange.GetOrderBookSnapshot();
        Assert.Empty(orderBook.Bids);

        var ask = Assert.Single(orderBook.Asks);
        Assert.Equal(102m, ask.Price);
        Assert.Equal(1m, ask.Quantity);
    }

    [Fact]
    public void Submit_MarketSellConsumesBestBidsAndDoesNotRestInOrderBook()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        exchange.Submit(CreateOrder(CreateId(1), OrderSide.Buy, 100m, quantity: 2m, agentName: "buyer-a"));
        exchange.Submit(CreateOrder(CreateId(2), OrderSide.Buy, 99m, quantity: 3m, agentName: "buyer-b"));

        var snapshot = exchange.Submit(CreateMarketOrder(CreateId(3), OrderSide.Sell, quantity: 4m, agentName: "seller-agent"));

        Assert.Equal(99m, snapshot.LastPrice);
        Assert.Equal(4m, snapshot.Volume);
        Assert.Equal(99m, snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Equal([100m, 100m, 99m], snapshot.RecentPrices);

        Assert.Collection(
            snapshot.RecentTrades,
            firstTrade =>
            {
                Assert.Equal(CreateId(1), firstTrade.BuyOrderId);
                Assert.Equal(CreateId(3), firstTrade.SellOrderId);
                Assert.Equal(100m, firstTrade.Price);
                Assert.Equal(2m, firstTrade.Quantity);
            },
            secondTrade =>
            {
                Assert.Equal(CreateId(2), secondTrade.BuyOrderId);
                Assert.Equal(CreateId(3), secondTrade.SellOrderId);
                Assert.Equal(99m, secondTrade.Price);
                Assert.Equal(2m, secondTrade.Quantity);
            });

        var orderBook = exchange.GetOrderBookSnapshot();
        var bid = Assert.Single(orderBook.Bids);
        Assert.Equal(99m, bid.Price);
        Assert.Equal(1m, bid.Quantity);

        Assert.Empty(orderBook.Asks);
    }

    [Fact]
    public void Submit_MarketOrderWithNoLiquidityDoesNotRestInOrderBook()
    {
        var exchange = new ExchangeEngine(startPrice: 100m);

        var snapshot = exchange.Submit(CreateMarketOrder(CreateId(1), OrderSide.Buy, quantity: 10m));

        Assert.Equal(100m, snapshot.LastPrice);
        Assert.Equal(0m, snapshot.Volume);
        Assert.Null(snapshot.BestBid);
        Assert.Null(snapshot.BestAsk);
        Assert.Empty(snapshot.RecentTrades);
        Assert.Equal([100m], snapshot.RecentPrices);

        var orderBook = exchange.GetOrderBookSnapshot();
        Assert.Empty(orderBook.Bids);
        Assert.Empty(orderBook.Asks);
    }

    [Fact]
    public void Submit_RejectsMarketOrdersWithPrice()
    {
        var exchange = new ExchangeEngine();
        var order = new Order(
            Id: CreateId(1),
            AgentName: "agent-1",
            Side: OrderSide.Buy,
            Type: OrderType.Market,
            Price: 100m,
            Quantity: 1m,
            CreatedAt: DateTimeOffset.UtcNow
        );

        var exception = Assert.Throws<ArgumentException>(() => exchange.Submit(order));
        Assert.Contains("market order price", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Order CreateOrder(
        Guid id,
        OrderSide side,
        decimal price,
        decimal quantity,
        string? agentName = null)
    {
        return new Order(
            Id: id,
            AgentName: agentName ?? GetDefaultAgentName(side),
            Side: side,
            Type: OrderType.Limit,
            Price: price,
            Quantity: quantity,
            CreatedAt: DateTimeOffset.UtcNow
        );
    }

    private static Order CreateMarketOrder(
        Guid id,
        OrderSide side,
        decimal quantity,
        string? agentName = null)
    {
        return new Order(
            Id: id,
            AgentName: agentName ?? GetDefaultAgentName(side),
            Side: side,
            Type: OrderType.Market,
            Price: null,
            Quantity: quantity,
            CreatedAt: DateTimeOffset.UtcNow
        );
    }

    private static string GetDefaultAgentName(OrderSide side)
    {
        return side == OrderSide.Buy ? "buyer-agent" : "seller-agent";
    }

    private static Guid CreateId(int id)
    {
        return Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
    }
}
