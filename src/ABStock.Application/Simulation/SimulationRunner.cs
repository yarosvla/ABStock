using ABStock.Agents;
using ABStock.Application.Accounts;
using ABStock.Application.Assets;
using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation.Diagnostics;
using ABStock.Shared;

namespace ABStock.Application.Simulation;

public sealed class SimulationRunner : IMultiAssetSimulationRunner, ISimulationDebugControl
{
    private readonly object _sync = new();
    private readonly ITradingSessionFactory _sessionFactory;
    private readonly IAssetCatalog _assetCatalog;
    private readonly IAgentFactory _agentFactory;
    private readonly IMarketHistoryStore _marketHistoryStore;
    private readonly List<SessionAgent> _agents = [];
    private readonly List<Guid> _marketOrder = [];
    private readonly Dictionary<Guid, MarketRun> _markets = [];
    private readonly Dictionary<Guid, SimulationTickResult> _currentMarkets = [];
    private readonly Dictionary<Guid, Queue<NewsSignal>> _pendingNews = [];
    private ITradingSession? _session;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private IReadOnlyList<AgentSpec> _agentSpecs = [];
    private Guid _primaryAssetId;
    private TimeSpan _tickInterval;
    private int _tick;

    public event Action<SimulationTickResult>? OnTick;
    public event Action<SimulationTickResult>? OnMarketTick;
    public event Action<MarketSubmitResult>? OnOrdersSubmitted;
    public event Action? OnStateChanged;

    public SimulationTickResult? Current
    {
        get { lock (_sync) { return _currentMarkets.GetValueOrDefault(_primaryAssetId); } }
    }

    public bool IsRunning
    {
        get { lock (_sync) { return _runTask is { IsCompleted: false }; } }
    }

    public string? CurrentAssetName
    {
        get { lock (_sync) { return _markets.GetValueOrDefault(_primaryAssetId)?.Asset.Name; } }
    }

    public Guid CurrentRunId
    {
        get { lock (_sync) { return _markets.GetValueOrDefault(_primaryAssetId)?.RunId ?? Guid.Empty; } }
    }

    public Guid CurrentSessionId
    {
        get { lock (_sync) { return _session?.SessionId ?? Guid.Empty; } }
    }

    public Guid PrimaryAssetId
    {
        get { lock (_sync) { return _primaryAssetId; } }
    }

    public SimulationRunner(
        ITradingSessionFactory sessionFactory,
        IAssetCatalog assetCatalog,
        IAgentFactory agentFactory,
        IMarketHistoryStore marketHistoryStore)
    {
        _sessionFactory = sessionFactory;
        _assetCatalog = assetCatalog;
        _agentFactory = agentFactory;
        _marketHistoryStore = marketHistoryStore;
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

            ct.ThrowIfCancellationRequested();
            ValidateInterval(config.TickInterval);
            var specs = CopySpecs(config.Agents);
            var asset = config.AssetId is { } assetId
                ? GetAsset(assetId)
                : _assetCatalog.Create(new CreateAssetRequest(
                    new AssetProfile(config.AssetName, config.AssetType, config.AssetDescription, [], 0m)
                    {
                        Source = ProfileSource.Fallback
                    },
                    config.StartPrice));

            StartSessionLocked([asset], config.TickInterval, specs, ct);
        }

        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public Task StartSessionAsync(MultiAssetSimulationConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_sync)
        {
            if (_runTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            ct.ThrowIfCancellationRequested();
            ValidateInterval(config.TickInterval);
            ArgumentNullException.ThrowIfNull(config.AssetIds);
            var assetIds = config.AssetIds.ToArray();
            if (assetIds.Distinct().Count() != assetIds.Length)
            {
                throw new ArgumentException("Asset ids must be unique within the session.", nameof(config));
            }

            var assets = assetIds.Select(GetAsset).ToArray();
            var specs = CopySpecs(config.Agents);
            StartSessionLocked(assets, config.TickInterval, specs, ct);
        }

        OnStateChanged?.Invoke();
        return Task.CompletedTask;
    }

    private void StartSessionLocked(
        IReadOnlyList<Asset> assets,
        TimeSpan interval,
        IReadOnlyList<AgentSpec> specs,
        CancellationToken ct)
    {
        var session = _sessionFactory.Create();
        foreach (var asset in assets)
        {
            session.AddMarket(asset.AssetId);
        }

        var primaryAssetId = assets.FirstOrDefault()?.AssetId ?? Guid.Empty;
        var createdAgents = _agentFactory.Create(specs);
        var registeredAgents = new List<SessionAgent>(createdAgents.Count);
        for (var index = 0; index < createdAgents.Count; index++)
        {
            var agent = createdAgents[index];
            agent.State.AgentName = GetUniqueAgentName(
                registeredAgents.Select(item => item.Name), agent.State.AgentName);
            var spec = index < specs.Count ? specs[index] : null;
            session.AddAgent(CreateAccountSpec(agent, spec, primaryAssetId));
            registeredAgents.Add(new SessionAgent(agent.State.AgentName, agent));
        }

        var startedAt = DateTimeOffset.UtcNow;
        _marketHistoryStore.StartSession(session.SessionId, interval, startedAt);
        MarketRun[] marketRuns;
        try
        {
            marketRuns = assets.Select(asset => new MarketRun(asset,
                _marketHistoryStore.StartRun(
                    CreateHistoryConfig(asset, session.SessionId, interval, specs), startedAt))).ToArray();
        }
        catch
        {
            _marketHistoryStore.EndSession(session.SessionId, DateTimeOffset.UtcNow);
            throw;
        }

        _session = session;
        _agents.AddRange(registeredAgents);
        foreach (var market in marketRuns)
        {
            _markets.Add(market.Asset.AssetId, market);
            _marketOrder.Add(market.Asset.AssetId);
        }

        _primaryAssetId = primaryAssetId;
        _tickInterval = interval;
        _agentSpecs = specs;
        _tick = 0;
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var runCts = _runCts;
        _runTask = Task.Run(() => RunLoopAsync(session.SessionId, runCts), CancellationToken.None);
    }

    public async Task StopAsync()
    {
        Task? runTask;
        lock (_sync)
        {
            runTask = _runTask;
            _runCts?.Cancel();
        }

        if (runTask is not null)
        {
            await runTask.ConfigureAwait(false);
        }
    }

    public SimulationTickResult? GetCurrent(Guid assetId)
    {
        lock (_sync)
        {
            return _currentMarkets.GetValueOrDefault(assetId);
        }
    }

    public IReadOnlyList<SimulationTickResult> GetCurrentMarkets()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_marketOrder.Where(_currentMarkets.ContainsKey)
                .Select(assetId => _currentMarkets[assetId]).ToArray());
        }
    }

    public Guid GetRunId(Guid assetId)
    {
        lock (_sync)
        {
            return _markets.GetValueOrDefault(assetId)?.RunId ?? Guid.Empty;
        }
    }

    public IReadOnlyList<AgentAccountSnapshot> GetAgentAccounts()
    {
        lock (_sync)
        {
            return _session?.GetAgentAccounts() ?? [];
        }
    }

    public IReadOnlyList<Order> GetOpenOrders(Guid assetId)
    {
        lock (_sync)
        {
            return RequireSessionLocked().GetOpenOrders(assetId);
        }
    }

    public SimulationTickResult AddMarket(Guid assetId)
    {
        IReadOnlyList<SimulationTickResult> ticks;
        SimulationTickResult added;
        lock (_sync)
        {
            var session = RequireSessionLocked();
            if (_markets.ContainsKey(assetId))
            {
                return _currentMarkets.GetValueOrDefault(assetId)
                    ?? CreateMarketTickLocked(assetId, session.GetAgentAccounts());
            }

            var asset = GetAsset(assetId);
            var runId = _marketHistoryStore.StartRun(
                CreateHistoryConfig(asset, session.SessionId, _tickInterval, _agentSpecs), DateTimeOffset.UtcNow);
            session.AddMarket(assetId);
            _markets.Add(assetId, new MarketRun(asset, runId));
            _marketOrder.Add(assetId);
            if (_primaryAssetId == Guid.Empty)
            {
                _primaryAssetId = assetId;
            }

            ticks = CaptureTicksLocked();
            added = _currentMarkets[assetId];
        }

        Publish(ticks, []);
        return added;
    }

    public AgentAccountSnapshot AddSessionAgent(AgentSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        IReadOnlyList<SimulationTickResult> ticks;
        AgentAccountSnapshot account;
        lock (_sync)
        {
            var session = RequireSessionLocked();
            var copiedSpec = CopySpecs([spec])[0];
            var agent = _agentFactory.Create([copiedSpec]).Single();
            agent.State.AgentName = GetUniqueAgentName(_agents.Select(item => item.Name), agent.State.AgentName);
            account = session.AddAgent(CreateAccountSpec(agent, copiedSpec, _primaryAssetId));
            _agents.Add(new SessionAgent(account.AgentName, agent));
            _agentSpecs = Array.AsReadOnly(_agentSpecs.Append(copiedSpec).ToArray());
            ticks = CaptureTicksLocked();
        }

        Publish(ticks, []);
        return account;
    }

    public AgentSnapshot AddAgent(AgentSpec spec)
    {
        Guid assetId;
        lock (_sync)
        {
            RequireSessionLocked();
            assetId = _primaryAssetId;
            if (assetId == Guid.Empty)
            {
                throw new InvalidOperationException("Add a market before using the single-asset debug API.");
            }
        }

        return ToAgentSnapshot(AddSessionAgent(spec), assetId);
    }

    public MarketSubmitResult SubmitMany(Guid assetId, IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var batch = orders.ToArray();
        MarketSubmitResult result;
        IReadOnlyList<SimulationTickResult> ticks;
        lock (_sync)
        {
            result = RequireSessionLocked().SubmitMany(assetId, batch);
            ticks = CaptureTicksLocked(submissions: new Dictionary<Guid, SubmitResult> { [assetId] = result.Result });
        }

        Publish(ticks, [result]);
        return result;
    }

    public SubmitResult SubmitOrder(SimulationDebugOrderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid assetId;
        lock (_sync)
        {
            RequireSessionLocked();
            assetId = request.AssetId ?? _primaryAssetId;
        }

        var order = new Order(
            Guid.NewGuid(),
            string.IsNullOrWhiteSpace(request.AgentName) ? "Manual" : request.AgentName.Trim(),
            request.Side,
            request.Type,
            request.Type == OrderType.Market ? null : request.Price,
            request.Quantity,
            DateTimeOffset.UtcNow);
        return SubmitMany(assetId, [order]).Result;
    }

    public bool CancelOrder(Guid assetId, Guid orderId)
    {
        IReadOnlyList<SimulationTickResult> ticks;
        lock (_sync)
        {
            if (!RequireSessionLocked().CancelOrder(assetId, orderId))
            {
                return false;
            }

            ticks = CaptureTicksLocked();
        }

        Publish(ticks, []);
        return true;
    }

    public int CancelOrdersByAgent(Guid assetId, string agentName)
    {
        int cancelled;
        IReadOnlyList<SimulationTickResult> ticks;
        lock (_sync)
        {
            cancelled = RequireSessionLocked().CancelOrdersByAgent(assetId, agentName);
            if (cancelled == 0)
            {
                return 0;
            }

            ticks = CaptureTicksLocked();
        }

        Publish(ticks, []);
        return cancelled;
    }

    public void SubmitNews(Guid assetId, NewsSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        lock (_sync)
        {
            RequireSessionLocked().GetMarket(assetId);
            EnqueueNewsLocked(assetId, signal);
        }
    }

    public void SubmitNews(NewsSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        lock (_sync)
        {
            if (_session is not null && _primaryAssetId != Guid.Empty)
            {
                EnqueueNewsLocked(_primaryAssetId, signal);
            }
        }
    }

    private void EnqueueNewsLocked(Guid assetId, NewsSignal signal)
    {
        if (!_pendingNews.TryGetValue(assetId, out var queue))
        {
            queue = new Queue<NewsSignal>();
            _pendingNews.Add(assetId, queue);
        }

        queue.Enqueue(signal);
    }

    private async Task RunLoopAsync(Guid sessionId, CancellationTokenSource runCts)
    {
        var ct = runCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                IReadOnlyList<SimulationTickResult> ticks;
                List<MarketSubmitResult> submissions;
                lock (_sync)
                {
                    if (_session?.SessionId != sessionId)
                    {
                        return;
                    }

                    var decisionsByAsset = new Dictionary<Guid, IReadOnlyList<AgentDecision>>();
                    submissions = [];
                    foreach (var assetId in _marketOrder)
                    {
                        var snapshot = _session.GetMarket(assetId).Snapshot;
                        var news = _pendingNews.TryGetValue(assetId, out var queue) && queue.Count > 0
                            ? queue.Dequeue()
                            : null;
                        var decisions = new List<AgentDecision>(_agents.Count);
                        foreach (var registered in _agents)
                        {
                            var account = _session.GetAgentAccount(registered.Name);
                            AgentAccountProjection.Apply(registered.Agent, account, assetId);
                            decisions.Add(registered.Agent.Decide(
                                new AgentMarketContext(sessionId, assetId, snapshot, account), news));
                        }

                        decisionsByAsset.Add(assetId, decisions.AsReadOnly());
                        submissions.Add(_session.SubmitMany(assetId, decisions.SelectMany(decision => decision.Orders)));
                    }

                    ticks = CaptureTicksLocked(decisionsByAsset,
                        submissions.ToDictionary(result => result.AssetId, result => result.Result));
                }

                Publish(ticks, submissions);
                await Task.Delay(_tickInterval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            try
            {
                lock (_sync)
                {
                    try
                    {
                        if (_session?.SessionId == sessionId)
                        {
                            _marketHistoryStore.EndSession(sessionId, DateTimeOffset.UtcNow);
                        }
                    }
                    finally
                    {
                        if (_session?.SessionId == sessionId)
                        {
                            _session = null;
                            _runCts = null;
                            _runTask = null;
                            _agents.Clear();
                            _markets.Clear();
                            _marketOrder.Clear();
                            _currentMarkets.Clear();
                            _pendingNews.Clear();
                            _agentSpecs = [];
                            _primaryAssetId = Guid.Empty;
                        }

                        runCts.Dispose();
                    }
                }
            }
            finally
            {
                OnStateChanged?.Invoke();
            }
        }
    }

    private IReadOnlyList<SimulationTickResult> CaptureTicksLocked(
        IReadOnlyDictionary<Guid, IReadOnlyList<AgentDecision>>? decisions = null,
        IReadOnlyDictionary<Guid, SubmitResult>? submissions = null)
    {
        var session = RequireSessionLocked();
        var accounts = session.GetAgentAccounts();
        _tick++;
        var ticks = _marketOrder.Select(assetId => CreateMarketTickLocked(assetId, accounts,
            decisions?.GetValueOrDefault(assetId), submissions?.GetValueOrDefault(assetId))).ToArray();
        var capturedAt = DateTimeOffset.UtcNow;
        foreach (var tick in ticks)
        {
            if (tick.RunId != Guid.Empty)
            {
                _marketHistoryStore.SaveTick(tick.RunId, tick, capturedAt);
            }

            _currentMarkets[tick.AssetId] = tick;
        }

        if (_primaryAssetId != Guid.Empty)
        {
            var accountsByName = accounts.ToDictionary(account => account.AgentName, StringComparer.Ordinal);
            foreach (var registered in _agents)
            {
                AgentAccountProjection.Apply(registered.Agent, accountsByName[registered.Name], _primaryAssetId);
            }
        }

        return Array.AsReadOnly(ticks);
    }

    private SimulationTickResult CreateMarketTickLocked(
        Guid assetId,
        IReadOnlyList<AgentAccountSnapshot> accounts,
        IReadOnlyList<AgentDecision>? decisions = null,
        SubmitResult? submission = null)
    {
        var session = RequireSessionLocked();
        var market = session.GetMarket(assetId, depth: 8);
        return new SimulationTickResult(
            _tick, market.Snapshot, market.OrderBook,
            Array.AsReadOnly(accounts.Select(account => ToAgentSnapshot(account, assetId)).ToArray()),
            decisions ?? [])
        {
            SessionId = session.SessionId,
            AssetId = assetId,
            RunId = _markets[assetId].RunId,
            Accounts = accounts,
            Submission = submission
        };
    }

    private void Publish(
        IReadOnlyList<SimulationTickResult> ticks,
        IReadOnlyList<MarketSubmitResult> submissions)
    {
        foreach (var submission in submissions)
        {
            OnOrdersSubmitted?.Invoke(submission);
        }

        foreach (var tick in ticks)
        {
            OnMarketTick?.Invoke(tick);
            if (tick.AssetId == PrimaryAssetId)
            {
                OnTick?.Invoke(tick);
            }
        }
    }

    private ITradingSession RequireSessionLocked() =>
        _session ?? throw new InvalidOperationException("Simulation is not running.");

    private Asset GetAsset(Guid assetId)
    {
        if (assetId == Guid.Empty)
        {
            throw new ArgumentException("Asset id must not be empty.", nameof(assetId));
        }

        return _assetCatalog.Get(assetId)
            ?? throw new KeyNotFoundException($"Asset '{assetId}' is not registered in the catalog.");
    }

    private static AgentAccountSpec CreateAccountSpec(ITradeAgent agent, AgentSpec? spec, Guid primaryAssetId)
    {
        var positions = spec is null
            ? new Dictionary<Guid, decimal>()
            : new Dictionary<Guid, decimal>(spec.InitialPositions);
        if (agent.State.Position != 0m && !positions.ContainsKey(primaryAssetId))
        {
            if (primaryAssetId == Guid.Empty)
            {
                throw new ArgumentException("An initial position requires an existing market.");
            }

            positions.Add(primaryAssetId, agent.State.Position);
        }

        return new AgentAccountSpec(agent.State.AgentName, agent.State.AgentType, agent.State.Cash)
        {
            InitialPositions = positions
        };
    }

    private static AgentSnapshot ToAgentSnapshot(AgentAccountSnapshot account, Guid assetId) =>
        new(account.AgentName, account.AgentType, account.Cash, account.Positions[assetId].Quantity,
            account.PortfolioValue, account.InitialCash, account.InitialPortfolioValue);

    private static SimulationConfig CreateHistoryConfig(
        Asset asset, Guid sessionId, TimeSpan interval, IReadOnlyList<AgentSpec> specs) =>
        new(asset.Name, asset.Description, asset.AssetType, asset.StartPrice, interval, specs)
        {
            AssetId = asset.AssetId,
            SessionId = sessionId
        };

    private static IReadOnlyList<AgentSpec> CopySpecs(IReadOnlyList<AgentSpec> specs)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var copied = specs.Select(spec =>
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(spec.InitialPositions);
            if (!Enum.IsDefined(spec.Type) || spec.InitialCash < 0m || spec.InitialPosition < 0m
                || spec.InitialPositions.Any(position => position.Key == Guid.Empty || position.Value < 0m))
            {
                throw new ArgumentException("Agent type, cash and positions must be valid.", nameof(specs));
            }

            return spec with
            {
                InitialPositions = new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, decimal>(
                    new Dictionary<Guid, decimal>(spec.InitialPositions))
            };
        }).ToArray();
        return Array.AsReadOnly(copied);
    }

    private static void ValidateInterval(TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero || interval.TotalMilliseconds > uint.MaxValue - 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Tick interval must be positive and supported by Task.Delay.");
        }
    }

    private static string GetUniqueAgentName(IEnumerable<string> existing, string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        var names = existing.ToHashSet(StringComparer.Ordinal);
        var name = agentName.Trim();
        var candidate = name;
        var index = 2;
        while (names.Contains(candidate))
        {
            candidate = $"{name}{index++}";
        }

        return candidate;
    }

    private sealed record SessionAgent(string Name, ITradeAgent Agent);
    private sealed record MarketRun(Asset Asset, Guid RunId);
}
