using ABStock.Shared;

namespace ABStock.Exchange.Engine;

public sealed class ExchangeEngine : IExchangeEngine
{
    private const int DefaultMaxRecentPrices = 100;
    private const int DefaultMaxRecentTrades = 50;

    private readonly OrderBook _orderBook = new();
    private readonly OrderValidator _orderValidator = new();
    private readonly List<Trade> _trades = [];
    private readonly List<decimal> _prices;
    private readonly int _maxRecentPrices;
    private readonly int _maxRecentTrades;
    private decimal _lastPrice;
    private decimal _totalVolume;

    public ExchangeEngine(
        decimal startPrice = 100m,
        int maxRecentPrices = DefaultMaxRecentPrices,
        int maxRecentTrades = DefaultMaxRecentTrades)
    {
        if (startPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startPrice), "Start price must be positive.");
        }

        if (maxRecentPrices <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRecentPrices), "Recent prices limit must be positive.");
        }

        if (maxRecentTrades <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRecentTrades), "Recent trades limit must be positive.");
        }

        _maxRecentPrices = maxRecentPrices;
        _maxRecentTrades = maxRecentTrades;
        _lastPrice = startPrice;
        _prices = [startPrice];
    }

    public MarketSnapshot Submit(Order order)
    {
        _orderValidator.Validate(order);

        ProcessAcceptedOrder(order);

        return GetSnapshot();
    }

    public MarketSnapshot SubmitMany(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var orderBatch = orders.ToArray();
        foreach (var order in orderBatch)
        {
            _orderValidator.Validate(order);
        }

        foreach (var order in orderBatch)
        {
            ProcessAcceptedOrder(order);
        }

        return GetSnapshot();
    }

    public SubmitResult SubmitWithResult(Order order)
    {
        return SubmitManyWithResult([order]);
    }

    public SubmitResult SubmitManyWithResult(IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);

        var candidateOrders = new List<Order>();
        var acceptedOrders = new List<Order>();
        var rejectedOrders = new List<RejectedOrder>();

        foreach (var order in orders)
        {
            try
            {
                _orderValidator.Validate(order);
                candidateOrders.Add(order);
            }
            catch (Exception exception)
            {
                rejectedOrders.Add(new RejectedOrder(order, exception.Message));
            }
        }

        var trades = new List<Trade>();
        foreach (var order in candidateOrders)
        {
            try
            {
                trades.AddRange(ProcessAcceptedOrder(order));
                acceptedOrders.Add(order);
            }
            catch (Exception exception)
            {
                rejectedOrders.Add(new RejectedOrder(order, exception.Message));
            }
        }

        return new SubmitResult(
            Snapshot: GetSnapshot(),
            Trades: trades,
            AcceptedOrders: acceptedOrders.ToArray(),
            RejectedOrders: rejectedOrders.ToArray()
        );
    }

    public MarketSnapshot GetSnapshot()
    {
        return new MarketSnapshot(
            LastPrice: _lastPrice,
            BestBid: _orderBook.BestBid?.Price,
            BestAsk: _orderBook.BestAsk?.Price,
            Volume: _totalVolume,
            RecentPrices: _prices.ToArray(),
            RecentTrades: _trades.ToArray()
        );
    }

    public OrderBookSnapshot GetOrderBookSnapshot(int depth = 5)
    {
        return _orderBook.GetSnapshot(depth);
    }

    private IReadOnlyList<Trade> ProcessAcceptedOrder(Order order)
    {
        if (order.Type == OrderType.Market)
        {
            return ExecuteMarketOrder(order);
        }

        EnsureLimitOrderDoesNotSelfTrade(order);
        _orderBook.Add(order);

        return MatchOrders();
    }

    private void EnsureLimitOrderDoesNotSelfTrade(Order order)
    {
        if (_orderBook.WouldSelfTrade(order))
        {
            throw new InvalidOperationException("Order would match another order from the same agent.");
        }
    }

    private IReadOnlyList<Trade> ExecuteMarketOrder(Order marketOrder)
    {
        var trades = new List<Trade>();
        var remainingQuantity = marketOrder.Quantity;

        while (remainingQuantity > 0)
        {
            var restingOrder = marketOrder.Side == OrderSide.Buy
                ? _orderBook.FindBestAskFor(marketOrder)
                : _orderBook.FindBestBidFor(marketOrder);

            if (restingOrder is null)
            {
                return trades;
            }

            var quantity = Math.Min(remainingQuantity, restingOrder.Quantity);
            trades.Add(ExecuteMarketMatch(marketOrder, restingOrder, quantity));
            remainingQuantity -= quantity;
        }

        return trades;
    }

    private IReadOnlyList<Trade> MatchOrders()
    {
        var trades = new List<Trade>();

        while (true)
        {
            var match = _orderBook.FindBestMatch();
            if (match is null)
            {
                return trades;
            }

            trades.Add(ExecuteMatch(match.Value.BuyOrder, match.Value.SellOrder));
        }
    }

    private Trade ExecuteMatch(Order buyOrder, Order sellOrder)
    {
        var quantity = Math.Min(buyOrder.Quantity, sellOrder.Quantity);
        var price = CalculateTradePrice(buyOrder, sellOrder);

        var trade = RecordTrade(buyOrder, sellOrder, price, quantity);

        _orderBook.Reduce(buyOrder, quantity);
        _orderBook.Reduce(sellOrder, quantity);

        return trade;
    }

    private Trade ExecuteMarketMatch(Order marketOrder, Order restingOrder, decimal quantity)
    {
        if (restingOrder.Price is null)
        {
            throw new InvalidOperationException("Resting limit price is required for market matching.");
        }

        var buyOrder = marketOrder.Side == OrderSide.Buy ? marketOrder : restingOrder;
        var sellOrder = marketOrder.Side == OrderSide.Sell ? marketOrder : restingOrder;
        var trade = RecordTrade(buyOrder, sellOrder, restingOrder.Price.Value, quantity);

        _orderBook.Reduce(restingOrder, quantity);

        return trade;
    }

    private Trade RecordTrade(Order buyOrder, Order sellOrder, decimal price, decimal quantity)
    {
        var trade = new Trade(
            Id: Guid.NewGuid(),
            BuyOrderId: buyOrder.Id,
            SellOrderId: sellOrder.Id,
            BuyerAgentName: buyOrder.AgentName,
            SellerAgentName: sellOrder.AgentName,
            Price: price,
            Quantity: quantity,
            ExecutedAt: DateTimeOffset.UtcNow
        );

        _trades.Add(trade);
        _totalVolume += quantity;
        _lastPrice = price;
        _prices.Add(price);
        TrimHistory();

        return trade;
    }

    private void TrimHistory()
    {
        TrimOldest(_prices, _maxRecentPrices);
        TrimOldest(_trades, _maxRecentTrades);
    }

    private static void TrimOldest<T>(List<T> items, int maxCount)
    {
        var extraItemsCount = items.Count - maxCount;
        if (extraItemsCount > 0)
        {
            items.RemoveRange(0, extraItemsCount);
        }
    }

    private static decimal CalculateTradePrice(Order buyOrder, Order sellOrder)
    {
        if (buyOrder.Price is null || sellOrder.Price is null)
        {
            throw new InvalidOperationException("Limit prices are required for matching.");
        }

        return (buyOrder.Price.Value + sellOrder.Price.Value) / 2m;
    }
}
