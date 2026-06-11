using ABStock.Agents;
using ABStock.Application.Simulation.Diagnostics;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

public sealed class SimulationRunner : ISimulationRunner, ISimulationDebugControl
{
    private readonly object _sync = new();
    private readonly IExchangeEngineFactory _exchangeEngineFactory;
    private readonly IAgentFactory _agentFactory;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private IExchangeEngine? _exchange;
    private List<ITradeAgent> _agents = [];
    private SimulationTickResult? _current;
    private volatile NewsSignal? _pendingNews;
    private int _tick;

    public event Action<SimulationTickResult>? OnTick;

    public SimulationTickResult? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _runTask is { IsCompleted: false };
            }
        }
    }

    public SimulationRunner(
        IExchangeEngineFactory exchangeEngineFactory,
        IAgentFactory agentFactory)
    {
        _exchangeEngineFactory = exchangeEngineFactory;
        _agentFactory = agentFactory;
    }

    public void SubmitNews(NewsSignal signal)
    {
        lock (_sync)
        {
            _pendingNews = signal;
        }
    }

    public Task StartAsync(SimulationConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            if (_runTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            var exchange = _exchangeEngineFactory.Create(config.StartPrice);
            var agents = _agentFactory.Create(config.Agents).ToList();

            _exchange = exchange;
            _agents = agents;
            _tick = 0;
            _current = null;
            _runCts?.Dispose();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var runCts = _runCts;
            _runTask = Task.Run(() => RunLoopAsync(config, runCts.Token), CancellationToken.None);

            return Task.CompletedTask;
        }
    }

    public async Task StopAsync()
    {
        Task? runTask;
        CancellationTokenSource? runCts;

        lock (_sync)
        {
            runTask = _runTask;
            runCts = _runCts;
        }

        if (runTask is null)
        {
            return;
        }

        runCts?.Cancel();

        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public SubmitResult SubmitOrder(SimulationDebugOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        SimulationTickResult tickResult;
        SubmitResult submitResult;

        lock (_sync)
        {
            if (_exchange is null)
            {
                throw new InvalidOperationException("Simulation is not running.");
            }

            var order = new Order(
                Guid.NewGuid(),
                string.IsNullOrWhiteSpace(request.AgentName) ? "Manual" : request.AgentName.Trim(),
                request.Side,
                request.Type,
                request.Type == OrderType.Market ? null : request.Price,
                request.Quantity,
                DateTimeOffset.UtcNow);

            var snapshot = _exchange.GetSnapshot();
            submitResult = FinancialOrderSubmission.Submit(_exchange, _agents, [order], snapshot);
            ApplyTradesToAgents(_agents, submitResult.Trades);
            tickResult = CreateTickResultLocked(submitResult.Snapshot);
        }

        OnTick?.Invoke(tickResult);

        return submitResult;
    }

    public AgentSnapshot AddAgent(AgentSpec spec)
    {
        SimulationTickResult tickResult;
        AgentSnapshot agentSnapshot;

        lock (_sync)
        {
            if (_exchange is null)
            {
                throw new InvalidOperationException("Simulation is not running.");
            }

            var agent = _agentFactory.Create([spec]).Single();
            agent.State.AgentName = GetUniqueAgentNameLocked(agent.State.AgentName);
            _agents.Add(agent);

            var marketSnapshot = _exchange.GetSnapshot();
            agentSnapshot = new AgentSnapshot(
                agent.State.AgentName,
                agent.State.AgentType,
                agent.State.Cash,
                agent.State.Position,
                agent.State.GetPortfolioValue(marketSnapshot.LastPrice));
            tickResult = CreateTickResultLocked(marketSnapshot);
        }

        OnTick?.Invoke(tickResult);

        return agentSnapshot;
    }

    private string GetUniqueAgentNameLocked(string agentName)
    {
        if (_agents.All(agent => !string.Equals(agent.State.AgentName, agentName, StringComparison.Ordinal)))
        {
            return agentName;
        }

        var index = 2;
        string candidate;
        do
        {
            candidate = $"{agentName}{index}";
            index++;
        }
        while (_agents.Any(agent => string.Equals(agent.State.AgentName, candidate, StringComparison.Ordinal)));

        return candidate;
    }

    private async Task RunLoopAsync(SimulationConfig config, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                SimulationTickResult tickResult;
                lock (_sync)
                {
                    if (_exchange is null)
                    {
                        return;
                    }

                    tickResult = RunTickLocked();
                }

                OnTick?.Invoke(tickResult);

                await Task.Delay(config.TickInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_sync)
            {
                _runCts?.Dispose();
                _runCts = null;
                _runTask = null;
                _exchange = null;
                _agents = [];
            }
        }
    }

    private SimulationTickResult RunTickLocked()
    {
        if (_exchange is null)
        {
            throw new InvalidOperationException("Simulation is not running.");
        }

        var snapshot = _exchange.GetSnapshot();
        var news = _pendingNews;
        _pendingNews = null;

        var allOrders = _agents
            .SelectMany(a => a.Decide(snapshot, news).Orders)
            .ToList();

        var newSnapshot = snapshot;
        if (allOrders.Count > 0)
        {
            var submitResult = FinancialOrderSubmission.Submit(_exchange, _agents, allOrders, snapshot);
            ApplyTradesToAgents(_agents, submitResult.Trades);
            newSnapshot = submitResult.Snapshot;
        }

        return CreateTickResultLocked(newSnapshot);
    }

    private SimulationTickResult CreateTickResultLocked(MarketSnapshot snapshot)
    {
        if (_exchange is null)
        {
            throw new InvalidOperationException("Simulation is not running.");
        }

        var tickResult = new SimulationTickResult(
            ++_tick,
            snapshot,
            _exchange.GetOrderBookSnapshot(),
            GetAgentSnapshots(_agents, snapshot.LastPrice)
        );

        _current = tickResult;

        return tickResult;
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
