using ABStock.Agents;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

public sealed class SimulationRunner : ISimulationRunner
{
    private readonly object _sync = new();
    private readonly IExchangeEngineFactory _exchangeEngineFactory;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
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

    public SimulationRunner(IExchangeEngineFactory exchangeEngineFactory)
    {
        _exchangeEngineFactory = exchangeEngineFactory;
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
            var agents = new AgentFactory().Create(config.Agents);

            _tick = 0;
            _current = null;
            _runCts?.Dispose();
            _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var runCts = _runCts;
            _runTask = Task.Run(() => RunLoopAsync(config, exchange, agents, runCts.Token), CancellationToken.None);

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

    private async Task RunLoopAsync(
        SimulationConfig config,
        IExchangeEngine exchange,
        IReadOnlyList<ITradeAgent> agents,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var snapshot = exchange.GetSnapshot();
                var news = TakePendingNews();

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

                var tickResult = new SimulationTickResult(
                    GetNextTick(),
                    newSnapshot,
                    exchange.GetOrderBookSnapshot(),
                    GetAgentSnapshots(agents, newSnapshot.LastPrice)
                );

                SetCurrent(tickResult);
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
            }
        }
    }

    private NewsSignal? TakePendingNews()
    {
        lock (_sync)
        {
            var news = _pendingNews;
            _pendingNews = null;
            return news;
        }
    }

    private int GetNextTick()
    {
        lock (_sync)
        {
            return ++_tick;
        }
    }

    private void SetCurrent(SimulationTickResult tickResult)
    {
        lock (_sync)
        {
            _current = tickResult;
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
