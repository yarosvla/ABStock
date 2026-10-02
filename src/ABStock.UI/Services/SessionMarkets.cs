using ABStock.Application.Simulation;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Рынки сессии глазами интерфейса: по каждому активу — последний тик, цена
/// открытия, ход цены с открытия, сделки; по сессии — общие счета агентов,
/// время и состав. Источник — <see cref="IMultiAssetSimulationRunner"/>.
///
/// Обёртка нужна не ради ещё одного слоя. Раннер после остановки очищает всё,
/// а экраны «торги остановлены» обязаны показывать итог сессии: последние
/// цены, спарклайны, счета. И раннер шлёт тик на каждый рынок отдельно, а
/// странице нужна одна перерисовка на шаг, а не восемь.
/// </summary>
public interface ISessionMarkets
{
    bool IsRunning { get; }

    /// <summary>Сессия была: идёт сейчас или остановлена, и её итог ещё на экране.</summary>
    bool HasSession { get; }

    Guid SessionId { get; }

    DateTimeOffset? StartedAt { get; }

    DateTimeOffset? EndedAt { get; }

    /// <summary>Номер шага последней сессии — шаг равен секунде, это её длительность.</summary>
    int Tick { get; }

    /// <summary>Рынки сессии в порядке каталога (по дате создания актива).</summary>
    IReadOnlyList<AssetMarket> Markets { get; }

    /// <summary>Общие счета агентов на последнем шаге: один кошелёк, позиции по активам.</summary>
    IReadOnlyList<AgentAccountSnapshot> Accounts { get; }

    AssetMarket? Market(Guid assetId);

    bool Trades(Guid assetId);

    /// <summary>Шаг сессии отработан по всем рынкам — пора перерисоваться.</summary>
    event Action? Changed;

    Task StartAsync(IReadOnlyList<AgentSpec> agents);

    Task StopAsync();

    /// <summary>
    /// Новость веером: каждый задетый актив в торгах получает свой сигнал
    /// (<c>SubmitNews(assetId, signal)</c>). Отправки «на весь рынок» у бэкенда
    /// нет, и она не нужна: сила влияния у каждого актива своя.
    /// </summary>
    void SubmitNews(NewsFan fan);
}

/// <param name="Prices">
/// Цена на каждом шаге с открытия, прореженная до разумного числа точек —
/// ряд спарклайна «с открытия».
/// </param>
/// <param name="TradeCount">
/// Сделок по активу за сессию — счётчик биржи, а не подсчёт по окну
/// последних сделок, которое бывает уже числа сделок за шаг.
/// </param>
public sealed record AssetMarket(
    Guid AssetId,
    Guid RunId,
    decimal Open,
    SimulationTickResult Last,
    IReadOnlyList<decimal> Prices,
    long TradeCount)
{
    public decimal LastPrice => Last.Snapshot.LastPrice;

    public decimal Change => LastPrice - Open;

    public decimal ChangePercent => Open == 0m ? 0m : Change / Open * 100m;

    /// <summary>Объём за сессию, шт: рынок хранит его нарастающим итогом.</summary>
    public decimal Volume => Last.Snapshot.Volume;
}

/// <summary>
/// Singleton: сессия одна на сервер, как и раннер. Подписка на тики должна
/// существовать до первого тика, поэтому экземпляр создаётся при старте
/// приложения (Program.cs), а не при первом заходе на страницу.
/// </summary>
public sealed class SessionMarkets : ISessionMarkets, IDisposable
{
    /// <summary>Точек в ряду спарклайна: больше ширина ячейки всё равно не покажет.</summary>
    private const int MaxPricePoints = 240;

    private readonly IMultiAssetSimulationRunner _runner;
    private readonly IAssetRegistry _assets;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _tickInterval;
    private readonly Lock _sync = new();
    private readonly Dictionary<Guid, MarketState> _markets = [];
    private IReadOnlyList<AgentAccountSnapshot> _accounts = [];
    private Guid _sessionId;
    private DateTimeOffset? _startedAt;
    private DateTimeOffset? _endedAt;
    private int _tick;
    private int _roundTick = -1;
    private int _roundCount;
    private int _roundSize;

    public SessionMarkets(IMultiAssetSimulationRunner runner, IAssetRegistry assets)
        : this(runner, assets, TimeProvider.System, TimeSpan.FromSeconds(1))
    {
    }

    /// <param name="tickInterval">
    /// Шаг симуляции. В продукте — ровно секунда: на этом стоит «число шагов =
    /// длительность в секундах» (SessionClock). Другой шаг — только для тестов.
    /// </param>
    public SessionMarkets(IMultiAssetSimulationRunner runner, IAssetRegistry assets, TimeProvider clock, TimeSpan tickInterval)
    {
        _runner = runner;
        _assets = assets;
        _clock = clock;
        _tickInterval = tickInterval;
        _runner.OnMarketTick += HandleMarketTick;
        _runner.OnStateChanged += HandleStateChanged;
        _assets.Added += HandleAssetAdded;
    }

    public bool IsRunning => _runner.IsRunning;

    public bool HasSession
    {
        get { lock (_sync) { return _sessionId != Guid.Empty; } }
    }

    public Guid SessionId
    {
        get { lock (_sync) { return _sessionId; } }
    }

    public DateTimeOffset? StartedAt
    {
        get { lock (_sync) { return _startedAt; } }
    }

    public DateTimeOffset? EndedAt
    {
        get { lock (_sync) { return _endedAt; } }
    }

    public int Tick
    {
        get { lock (_sync) { return _tick; } }
    }

    public IReadOnlyList<AssetMarket> Markets
    {
        get
        {
            lock (_sync)
            {
                // Порядок — каталога, а не добавления рынков: один порядок на
                // всех экранах (DESIGN.md 16).
                var order = _assets.Assets.Select((asset, index) => (asset.Id, index))
                    .ToDictionary(pair => pair.Id, pair => pair.index);

                return _markets.Values
                    .Where(market => market.Last is not null)
                    .OrderBy(market => order.GetValueOrDefault(market.AssetId, int.MaxValue))
                    .Select(market => market.ToView(MaxPricePoints))
                    .ToArray();
            }
        }
    }

    public IReadOnlyList<AgentAccountSnapshot> Accounts
    {
        get { lock (_sync) { return _accounts; } }
    }

    public event Action? Changed;

    public AssetMarket? Market(Guid assetId)
    {
        lock (_sync)
        {
            return _markets.TryGetValue(assetId, out var market) && market.Last is not null
                ? market.ToView(MaxPricePoints)
                : null;
        }
    }

    public bool Trades(Guid assetId)
    {
        lock (_sync)
        {
            return _markets.ContainsKey(assetId);
        }
    }

    public Task StartAsync(IReadOnlyList<AgentSpec> agents)
    {
        // В сессию идут все действующие активы реестра — в его порядке.
        var assetIds = _assets.Assets.Select(asset => asset.Id).ToArray();
        return _runner.StartSessionAsync(new MultiAssetSimulationConfig(assetIds, _tickInterval, agents));
    }

    public Task StopAsync() => _runner.StopAsync();

    public void SubmitNews(NewsFan fan)
    {
        ArgumentNullException.ThrowIfNull(fan);

        if (!_runner.IsRunning)
        {
            return;
        }

        foreach (var row in fan.Hit)
        {
            var asset = _assets.Find(row.Symbol);

            if (asset is null || !Trades(asset.Id))
            {
                continue;
            }

            try
            {
                _runner.SubmitNews(asset.Id, row.Signal!);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                // Торги остановились между проверкой и отправкой — сигнал некому
                // доставлять, а разбор новости на экране остаётся правдой.
            }
        }
    }

    public void Dispose()
    {
        _runner.OnMarketTick -= HandleMarketTick;
        _runner.OnStateChanged -= HandleStateChanged;
        _assets.Added -= HandleAssetAdded;
    }

    private void HandleAssetAdded(SessionAsset asset)
    {
        // Актив, созданный во время торгов, вступает в них сразу. Стартовый
        // запас агентам бэкенд выдаёт сам и записывает его вкладом капитала,
        // поэтому P/L при этом не подскакивает.
        if (!_runner.IsRunning)
        {
            return;
        }

        try
        {
            _runner.AddMarket(asset.Id);
        }
        catch (InvalidOperationException)
        {
            // Торги остановились, пока создавался актив: он войдёт в следующую сессию.
        }
    }

    private void HandleStateChanged()
    {
        lock (_sync)
        {
            if (_runner.IsRunning)
            {
                var sessionId = _runner.CurrentSessionId;

                if (sessionId != _sessionId)
                {
                    // Новая сессия — новый период: итог прошлой уходит целиком,
                    // иначе рядом оказались бы цены разных запусков (раздел 10).
                    _sessionId = sessionId;
                    _startedAt = _clock.GetLocalNow();
                    _endedAt = null;
                    _tick = 0;
                    _roundTick = -1;
                    _roundCount = 0;
                    _markets.Clear();
                    // Счета есть с момента запуска, до первого тика: без них
                    // «Торги запущены · 0 агентов» соврало бы о составе.
                    _accounts = _runner.GetAgentAccounts();
                }
            }
            else if (_sessionId != Guid.Empty && _endedAt is null)
            {
                _endedAt = _clock.GetLocalNow();
            }
        }

        Changed?.Invoke();
    }

    private void HandleMarketTick(SimulationTickResult tick)
    {
        bool roundComplete;

        lock (_sync)
        {
            if (tick.SessionId != _sessionId)
            {
                // Тик пришёл раньше OnStateChanged: сессия началась только что.
                _sessionId = tick.SessionId;
                _startedAt ??= _clock.GetLocalNow();
                _endedAt = null;
                _markets.Clear();
                _roundTick = -1;
            }

            if (!_markets.TryGetValue(tick.AssetId, out var market))
            {
                market = new MarketState(tick.AssetId, tick.RunId, Open(tick));
                _markets[tick.AssetId] = market;
            }

            market.Apply(tick);
            _accounts = tick.Accounts;
            _tick = Math.Max(_tick, tick.Tick);

            // Раннер шлёт тик на каждый рынок подряд, с одним номером шага.
            // Шаг закончен, когда пришли тики всех рынков сессии. Сколько их,
            // знает раннер, а не этот словарь: на первом шаге рынки в нём
            // появляются по одному, и каждый казался бы последним.
            if (tick.Tick != _roundTick)
            {
                _roundTick = tick.Tick;
                _roundCount = 0;
                _roundSize = Math.Max(1, _runner.GetCurrentMarkets().Count);
            }

            _roundCount++;
            roundComplete = _roundCount == _roundSize;
        }

        if (roundComplete)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Цена открытия — стартовая цена актива: с неё рынок начинает торги. Для
    /// актива, которого нет в реестре (архив посреди сессии), — первая цена.
    /// </summary>
    private decimal Open(SimulationTickResult tick) =>
        _assets.Find(tick.AssetId)?.StartPrice ?? tick.Snapshot.LastPrice;

    private sealed class MarketState(Guid assetId, Guid runId, decimal open)
    {
        /// <summary>Сутки торгов по шагу в секунду — с запасом больше любой демонстрации.</summary>
        private const int MaxStored = 86_400;

        private readonly List<decimal> _prices = [];

        public Guid AssetId { get; } = assetId;

        public SimulationTickResult? Last { get; private set; }

        public void Apply(SimulationTickResult tick)
        {
            Last = tick;
            _prices.Add(tick.Snapshot.LastPrice);

            if (_prices.Count > MaxStored)
            {
                _prices.RemoveAt(0);
            }
        }

        /// <summary>
        /// Ряд спарклайна: цена открытия, потом равномерная выборка, последняя
        /// цена всегда на месте — её показывает точка.
        /// </summary>
        public AssetMarket ToView(int maxPoints)
        {
            var sampled = new List<decimal>(Math.Min(_prices.Count, maxPoints) + 1) { open };
            var step = Math.Max(1d, (double)_prices.Count / maxPoints);

            for (var position = 0d; position < _prices.Count - 1; position += step)
            {
                sampled.Add(_prices[(int)position]);
            }

            if (_prices.Count > 0)
            {
                sampled.Add(_prices[^1]);
            }

            return new AssetMarket(AssetId, runId, open, Last!, sampled, Last!.Snapshot.TotalTradeCount);
        }
    }
}
