using ABStock.Agents;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

public sealed class SimulationRunner : ISimulationRunner
{
    private readonly IExchangeEngineFactory _exchangeEngineFactory;
    private volatile NewsSignal? _pendingNews;

    public event Action<SimulationTickResult>? OnTick;

    public SimulationRunner(IExchangeEngineFactory exchangeEngineFactory)
    {
        _exchangeEngineFactory = exchangeEngineFactory;
    }

    public void SubmitNews(NewsSignal signal)
    {
        _pendingNews = signal;
    }

    public async Task StartAsync(SimulationConfig config, CancellationToken ct)
    {
        var exchange = _exchangeEngineFactory.Create(config.StartPrice);
        var agents = new AgentFactory().Create(config.Agents);
        var tick = 0;

        while (!ct.IsCancellationRequested)
        {
            var snapshot = exchange.GetSnapshot();
            var news = _pendingNews;
            _pendingNews = null;

            var allOrders = agents
                .SelectMany(a => a.Decide(snapshot, news).Orders)
                .ToList();

            var newSnapshot = snapshot;
            if (allOrders.Count > 0)
            {
                var submitResult = exchange.SubmitManyWithResult(allOrders);
                ApplyTradesToAgents(agents, submitResult.Trades);
                newSnapshot = submitResult.Snapshot;
            }

            OnTick?.Invoke(new SimulationTickResult(
                ++tick,
                newSnapshot,
                exchange.GetOrderBookSnapshot(),
                GetAgentSnapshots(agents, newSnapshot.LastPrice)
            ));

            await Task.Delay(config.TickInterval, ct);
        }
    }

    private static void ApplyTradesToAgents(
        IReadOnlyList<ITradeAgent> agents,
        IReadOnlyList<Trade> trades)
    {
        var agentsByName = agents.ToDictionary(
            agent => agent.State.AgentName,
            StringComparer.Ordinal);

        foreach (var trade in trades)
        {
            var tradeValue = trade.Price * trade.Quantity;

            if (agentsByName.TryGetValue(trade.BuyerAgentName, out var buyer))
            {
                buyer.State.Cash -= tradeValue;
                buyer.State.Position += trade.Quantity;
            }

            if (agentsByName.TryGetValue(trade.SellerAgentName, out var seller))
            {
                seller.State.Cash += tradeValue;
                seller.State.Position -= trade.Quantity;
            }
        }
    }

    private static IReadOnlyList<AgentSnapshot> GetAgentSnapshots(
        IReadOnlyList<ITradeAgent> agents,
        decimal lastPrice) =>
        agents.Select(a => new AgentSnapshot(
            a.State.AgentName,
            a.State.AgentType,
            a.State.Cash,
            a.State.Position,
            a.State.GetPortfolioValue(lastPrice)
        )).ToArray();
}
