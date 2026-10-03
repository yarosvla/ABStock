using ABStock.Shared;

namespace ABStock.Application.Simulation;

public interface IMultiAssetSimulationRunner : ISimulationRunner
{
    event Action<SimulationTickResult>? OnMarketTick;

    event Action<MarketSubmitResult>? OnOrdersSubmitted;

    Guid CurrentSessionId { get; }

    Guid PrimaryAssetId { get; }

    Task StartSessionAsync(MultiAssetSimulationConfig config, CancellationToken ct = default);

    SimulationTickResult AddMarket(Guid assetId);

    AgentAccountSnapshot AddSessionAgent(AgentSpec spec);

    SimulationTickResult? GetCurrent(Guid assetId);

    IReadOnlyList<SimulationTickResult> GetCurrentMarkets();

    Guid GetRunId(Guid assetId);

    IReadOnlyList<AgentAccountSnapshot> GetAgentAccounts();

    IReadOnlyList<Order> GetOpenOrders(Guid assetId);

    MarketSubmitResult SubmitMany(Guid assetId, IEnumerable<Order> orders);

    bool CancelOrder(Guid assetId, Guid orderId);

    int CancelOrdersByAgent(Guid assetId, string agentName);

    void SubmitNews(Guid assetId, NewsSignal signal);
}
