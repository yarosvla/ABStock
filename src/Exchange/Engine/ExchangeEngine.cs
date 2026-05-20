using ABStock.Shared;

namespace ABStock.Exchange.Engine;

public sealed class ExchangeEngine
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

        _orderBook.Add(order);
        MatchOrders();

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
            _orderBook.Add(order);
        }

        MatchOrders();

        return GetSnapshot();
    }

    public MarketSnapshot GetSnapshot()
    {
        return new MarketSnapshot(
            LastPrice: _lastPrice,
            BestBid: _orderBook.BestBid?.Price,
            BestAsk: _orderBook.BestAsk?.Price,
            Volume: _trades.Sum(trade => trade.Quantity),
            RecentPrices: _prices.ToArray(),
            RecentTrades: _trades.ToArray()
        );
    }

    private void MatchOrders()
    {
        while (CanMatchBestOrders())
        {
            ExecuteBestMatch();
        }
    }

    private bool CanMatchBestOrders()
    {
        var bestBid = _orderBook.BestBid;
        var bestAsk = _orderBook.BestAsk;

        if (bestBid?.Price is null || bestAsk?.Price is null)
        {
            return false;
        }

        return bestBid.Price >= bestAsk.Price;
    }

    private void ExecuteBestMatch()
    {
        var buyOrder = _orderBook.BestBid ?? throw new InvalidOperationException("Best bid is missing.");
        var sellOrder = _orderBook.BestAsk ?? throw new InvalidOperationException("Best ask is missing.");
        var quantity = Math.Min(buyOrder.Quantity, sellOrder.Quantity);
        var price = CalculateTradePrice(buyOrder, sellOrder);

        var trade = new Trade(
            Id: Guid.NewGuid(),
            BuyOrderId: buyOrder.Id,
            SellOrderId: sellOrder.Id,
            Price: price,
            Quantity: quantity,
            ExecutedAt: DateTimeOffset.UtcNow
        );

        _trades.Add(trade);
        _lastPrice = price;
        _prices.Add(price);
        TrimHistory();

        _orderBook.ReduceBestBidBy(quantity);
        _orderBook.ReduceBestAskBy(quantity);
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
