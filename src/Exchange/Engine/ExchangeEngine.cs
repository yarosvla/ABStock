using ABStock.Exchange.Domain;

namespace ABStock.Exchange.Engine;

public sealed class ExchangeEngine
{
    private readonly OrderBook _orderBook = new();
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
        //TODO
        _orderBook.Add(order);

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
}
