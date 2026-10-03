using ABStock.Shared;

namespace ABStock.Application.Markets;

public interface IMarketSession
{
    Guid SessionId { get; }

    event Action<MarketState>? OnMarketChanged;

    event Action<MarketSubmitResult>? OnOrdersSubmitted;

    MarketState AddMarket(Guid assetId);

    MarketState GetMarket(Guid assetId, int depth = 5);

    IReadOnlyList<MarketState> GetMarkets(int depth = 5);

    MarketSubmitResult Submit(Guid assetId, Order order);

    MarketSubmitResult SubmitMany(Guid assetId, IEnumerable<Order> orders);

    bool CancelOrder(Guid assetId, Guid orderId);

    int CancelOrdersByAgent(Guid assetId, string agentName);

    IReadOnlyList<Order> GetOpenOrders(Guid assetId);
}
