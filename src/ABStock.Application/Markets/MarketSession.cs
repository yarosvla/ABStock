using ABStock.Application.Assets;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Markets;

internal sealed class MarketSession(
    IAssetCatalog assetCatalog,
    IExchangeEngineFactory exchangeEngineFactory) : IMarketSession
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, IExchangeEngine> _exchanges = new();

    public Guid SessionId { get; } = Guid.NewGuid();

    public event Action<MarketState>? OnMarketChanged;

    public event Action<MarketSubmitResult>? OnOrdersSubmitted;

    public MarketState AddMarket(Guid assetId)
    {
        EnsureAssetId(assetId);
        MarketState state;

        lock (_sync)
        {
            if (_exchanges.TryGetValue(assetId, out var existing))
            {
                return CreateState(assetId, existing);
            }

            var asset = assetCatalog.Get(assetId)
                ?? throw new KeyNotFoundException($"Asset '{assetId}' is not in the catalog.");
            if (asset.IsArchived)
            {
                throw new InvalidOperationException("An archived asset cannot start a new market.");
            }

            var exchange = exchangeEngineFactory.Create(asset.StartPrice);
            _exchanges.Add(assetId, exchange);
            state = CreateState(assetId, exchange);
        }

        OnMarketChanged?.Invoke(state);
        return state;
    }

    public MarketState GetMarket(Guid assetId, int depth = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        lock (_sync)
        {
            return CreateState(assetId, GetExchange(assetId), depth);
        }
    }

    public IReadOnlyList<MarketState> GetMarkets(int depth = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);

        lock (_sync)
        {
            return Array.AsReadOnly(_exchanges
                .Select(market => CreateState(market.Key, market.Value, depth))
                .ToArray());
        }
    }

    public MarketSubmitResult Submit(Guid assetId, Order order) => SubmitMany(assetId, [order]);

    public MarketSubmitResult SubmitMany(Guid assetId, IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        EnsureAssetId(assetId);
        var batch = orders.ToArray();
        MarketSubmitResult result;
        MarketState state;

        lock (_sync)
        {
            var exchange = GetExchange(assetId);
            result = new MarketSubmitResult(SessionId, assetId, exchange.SubmitManyWithResult(batch));
            state = CreateState(assetId, exchange);
        }

        OnOrdersSubmitted?.Invoke(result);
        OnMarketChanged?.Invoke(state);
        return result;
    }

    public bool CancelOrder(Guid assetId, Guid orderId)
    {
        MarketState state;

        lock (_sync)
        {
            var exchange = GetExchange(assetId);
            if (!exchange.CancelOrder(orderId))
            {
                return false;
            }

            state = CreateState(assetId, exchange);
        }

        OnMarketChanged?.Invoke(state);
        return true;
    }

    public int CancelOrdersByAgent(Guid assetId, string agentName)
    {
        int cancelled;
        MarketState state;

        lock (_sync)
        {
            var exchange = GetExchange(assetId);
            cancelled = exchange.CancelOrdersByAgent(agentName);
            if (cancelled == 0)
            {
                return 0;
            }

            state = CreateState(assetId, exchange);
        }

        OnMarketChanged?.Invoke(state);
        return cancelled;
    }

    public IReadOnlyList<Order> GetOpenOrders(Guid assetId)
    {
        lock (_sync)
        {
            return Array.AsReadOnly(GetExchange(assetId).GetOpenOrders().ToArray());
        }
    }

    private IExchangeEngine GetExchange(Guid assetId)
    {
        EnsureAssetId(assetId);
        return _exchanges.TryGetValue(assetId, out var exchange)
            ? exchange
            : throw new KeyNotFoundException($"Market for asset '{assetId}' is not in session '{SessionId}'.");
    }

    private MarketState CreateState(Guid assetId, IExchangeEngine exchange, int depth = 5) =>
        new(SessionId, assetId, exchange.GetSnapshot(), exchange.GetOrderBookSnapshot(depth));

    private static void EnsureAssetId(Guid assetId)
    {
        if (assetId == Guid.Empty)
        {
            throw new ArgumentException("Asset id is required.", nameof(assetId));
        }
    }
}
