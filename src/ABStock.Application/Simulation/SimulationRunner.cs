using ABStock.Agents;
using ABStock.Agents.Strategies;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

public sealed class SimulationRunner : ISimulationRunner
{
    private volatile NewsSignal? _pendingNews;

    public event Action<SimulationTickResult>? OnTick;

    public void SubmitNews(NewsSignal signal)
    {
        _pendingNews = signal;
    }

    public async Task StartAsync(SimulationConfig config, CancellationToken ct)
    {
        var exchange = new ExchangeEngine(config.StartPrice);
        var agents = CreateAgents(config.Agents);
        var tick = 0;

        while (!ct.IsCancellationRequested)
        {
            var snapshot = exchange.GetSnapshot();
            var news = _pendingNews;
            _pendingNews = null;

            var allOrders = agents
                .SelectMany(a => a.Decide(snapshot, news).Orders)
                .ToList();

            var newSnapshot = allOrders.Count > 0
                ? exchange.SubmitMany(allOrders)
                : snapshot;

            OnTick?.Invoke(new SimulationTickResult(
                ++tick,
                newSnapshot,
                GetAgentSnapshots(agents, newSnapshot.LastPrice)
            ));

            await Task.Delay(config.TickInterval, ct);
        }
    }

    private static List<ITradeAgent> CreateAgents(IReadOnlyList<AgentSpec> specs) =>
        specs.Select<AgentSpec, ITradeAgent>(spec => spec.Type switch
        {
            AgentType.TrendFollowing => new TrendFollowingAgent(spec.InitialCash),
            AgentType.CounterTrend   => new CounterTrendAgent(spec.InitialCash),
            AgentType.MarketMaker    => new MarketMakerAgent(spec.InitialCash),
            AgentType.NewsDriven     => new NewsDrivenAgent(spec.InitialCash),
            _ => throw new ArgumentOutOfRangeException(nameof(spec.Type))
        }).ToList();

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
