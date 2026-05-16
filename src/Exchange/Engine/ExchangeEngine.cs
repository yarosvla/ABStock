using ABStock.Exchange.Domain;

namespace ABStock.Exchange.Engine;

public sealed class ExchangeEngine
{
    private readonly OrderBook _orderBook = new();
    private readonly OrderValidator _orderValidator = new();
    private readonly List<Trade> _trades = [];
    private readonly List<decimal> _prices;
    private decimal _lastPrice;

    public ExchangeEngine(decimal startPrice = 100m)
    {
        if (startPrice <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startPrice), "Start price must be positive.");
        }
        
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

    public MarketSnapshot GetSnapshot()
    {
        return new MarketSnapshot
        {
            LastPrice = _lastPrice,
            BestBid = _orderBook.BestBid?.LimitPrice,
            BestAsk = _orderBook.BestAsk?.LimitPrice,
            Volume = _trades.Sum(trade => trade.Quantity),
            RecentPrices = _prices.ToArray(),
            RecentTrades = _trades.ToArray()
        };
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

        if (bestBid?.LimitPrice is null || bestAsk?.LimitPrice is null)
        {
            return false;
        }

        return bestBid.LimitPrice >= bestAsk.LimitPrice;
    }

    private void ExecuteBestMatch()
    {
        var buyOrder = _orderBook.BestBid ?? throw new InvalidOperationException("Best bid is missing.");
        var sellOrder = _orderBook.BestAsk ?? throw new InvalidOperationException("Best ask is missing.");
        var quantity = Math.Min(buyOrder.Quantity, sellOrder.Quantity);
        var price = CalculateTradePrice(buyOrder, sellOrder);

        var trade = new Trade
        {
            Id = Guid.NewGuid().ToString("N"),
            BuyOrderId = buyOrder.Id,
            SellOrderId = sellOrder.Id,
            Price = price,
            Quantity = quantity
        };

        _trades.Add(trade);
        _lastPrice = price;
        _prices.Add(price);

        _orderBook.ReduceBestBidBy(quantity);
        _orderBook.ReduceBestAskBy(quantity);
    }

    private static decimal CalculateTradePrice(Order buyOrder, Order sellOrder)
    {
        if (buyOrder.LimitPrice is null || sellOrder.LimitPrice is null)
        {
            throw new InvalidOperationException("Limit prices are required for matching.");
        }

        return (buyOrder.LimitPrice.Value + sellOrder.LimitPrice.Value) / 2m;
    }
}
