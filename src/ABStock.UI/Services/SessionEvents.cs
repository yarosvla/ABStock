using ABStock.Shared;

namespace ABStock.UI.Services;

public enum SessionEventKind
{
    News,
    System,
    Agents
}

/// <summary>
/// Событие хронологии сессии — строка левого рельса (DESIGN.md 9.21). Один
/// список на «Новостях» и «Торгах»: человек видит одну историю, а не две.
/// </summary>
/// <param name="At">Местное время — то же, что в часах шапки.</param>
/// <param name="Text">Текст новости целиком или фраза события: обрезает разметка, а не хранилище.</param>
/// <param name="Fan">Веер сигналов — только у новости.</param>
/// <param name="Strategy">Стратегия — только у события агентов: точка её цвета перед текстом.</param>
/// <param name="Symbol">Актив события агентов или созданный актив.</param>
/// <param name="PricesAtNews">
/// Цена каждого актива в торгах в момент новости — от неё считается «цена
/// после новости». Только у новости.
/// </param>
public sealed record SessionEvent(
    DateTimeOffset At,
    SessionEventKind Kind,
    string Text,
    NewsFan? Fan = null,
    AgentType? Strategy = null,
    string? Symbol = null,
    IReadOnlyDictionary<string, decimal>? PricesAtNews = null);

/// <summary>
/// Что сделали новостные агенты после новости: первая сделка-решение
/// (купить или продать) по какому-то активу в первые шаги после неё.
/// </summary>
public sealed record NewsReaction(string Symbol, TradeAction Action);

public interface ISessionEvents
{
    /// <summary>События от свежего к старому — так же, как они лежат в рельсе.</summary>
    IReadOnlyList<SessionEvent> Entries { get; }

    /// <summary>Только новости, от свежей к старой.</summary>
    IReadOnlyList<SessionEvent> News { get; }

    int Revision { get; }

    event Action? Changed;

    SessionEvent AddNews(string text, NewsFan fan);

    /// <summary>Первая реакция новостных агентов на новость или null, если её не было.</summary>
    NewsReaction? ReactionTo(SessionEvent news);

    /// <summary>
    /// Реакция новостных агентов на новость именно по этому активу. Новость
    /// задевает несколько активов, и агенты торгуют каждым из них.
    /// </summary>
    NewsReaction? ReactionTo(SessionEvent news, string symbol) =>
        ReactionTo(news) is { } reaction && reaction.Symbol == symbol ? reaction : null;
}

/// <summary>
/// Хронология живёт ровно столько, сколько сессия, события которой
/// показывает: новая сессия — новая хронология (раздел 10, «один период»).
/// После остановки история остановленной сессии остаётся на экране.
///
/// Системные события пишет сама хронология — по состоянию рынков и реестра.
/// События агентов она выводит из тиков: позиция стратегии в активе заметно
/// изменилась с прошлого упоминания. Журнала решений у бэкенда нет, и
/// выдумывать события сверх того, что видно по позициям, здесь нельзя.
/// </summary>
public sealed class SessionEvents : ISessionEvents, IDisposable
{
    /// <summary>
    /// Заметное изменение — пятая часть стартового запаса стратегии в активе:
    /// мелкие колебания маркет-мейкера вокруг нуля событием не считаются.
    /// </summary>
    private const decimal NoticeableShare = 0.2m;

    /// <summary>Не чаще одного события на стратегию и актив за этот срок: рельс — не лента сделок.</summary>
    private static readonly TimeSpan AgentEventCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Сколько шагов после новости ждать решения новостных агентов. Сигнал
    /// доставляется на следующем шаге; дальше реакция — уже не на эту новость.
    /// </summary>
    private const int ReactionWindow = 5;

    private readonly ISessionMarkets _markets;
    private readonly IAssetRegistry _assets;
    private readonly TimeProvider _clock;
    private readonly Lock _sync = new();
    private readonly List<SessionEvent> _entries = [];
    private readonly Dictionary<(AgentType, Guid), (decimal Position, DateTimeOffset At)> _mentioned = [];
    private readonly Dictionary<DateTimeOffset, List<NewsReaction>> _reactions = [];
    private (SessionEvent News, int Tick)? _awaitingReaction;
    private Guid _sessionId;
    private bool _wasRunning;

    public SessionEvents(ISessionMarkets markets, IAssetRegistry assets)
        : this(markets, assets, TimeProvider.System)
    {
    }

    public SessionEvents(ISessionMarkets markets, IAssetRegistry assets, TimeProvider clock)
    {
        _markets = markets;
        _assets = assets;
        _clock = clock;
        _markets.Changed += HandleMarketsChanged;
        _assets.Added += HandleAssetAdded;
    }

    public IReadOnlyList<SessionEvent> Entries
    {
        get { lock (_sync) { return _entries.ToArray(); } }
    }

    public IReadOnlyList<SessionEvent> News
    {
        get { lock (_sync) { return _entries.Where(entry => entry.Kind == SessionEventKind.News).ToArray(); } }
    }

    public int Revision { get; private set; }

    public event Action? Changed;

    public SessionEvent AddNews(string text, NewsFan fan)
    {
        ArgumentNullException.ThrowIfNull(fan);

        var prices = _markets.Markets
            .Select(market => (Asset: _assets.Find(market.AssetId), market.LastPrice))
            .Where(item => item.Asset is not null)
            .ToDictionary(item => item.Asset!.Symbol, item => item.LastPrice, StringComparer.OrdinalIgnoreCase);

        var entry = new SessionEvent(
            _clock.GetLocalNow(), SessionEventKind.News, text.Trim(), fan, PricesAtNews: prices);

        lock (_sync)
        {
            _entries.Insert(0, entry);
            _awaitingReaction = (entry, _markets.Tick);
            Revision++;
        }

        Changed?.Invoke();
        return entry;
    }

    public NewsReaction? ReactionTo(SessionEvent news)
    {
        lock (_sync)
        {
            return _reactions.GetValueOrDefault(news.At)?.FirstOrDefault();
        }
    }

    public NewsReaction? ReactionTo(SessionEvent news, string symbol)
    {
        lock (_sync)
        {
            return _reactions.GetValueOrDefault(news.At)?.FirstOrDefault(reaction => reaction.Symbol == symbol);
        }
    }

    public void Dispose()
    {
        _markets.Changed -= HandleMarketsChanged;
        _assets.Added -= HandleAssetAdded;
    }

    private void HandleAssetAdded(SessionAsset asset)
    {
        if (!_markets.IsRunning)
        {
            // Хронология — про сессию; актив, созданный до запуска, в ней
            // появится строкой «Торги запущены · N активов».
            return;
        }

        Append(new SessionEvent(
            _clock.GetLocalNow(),
            SessionEventKind.System,
            $"Создан актив {asset.Symbol} — вступил в торги",
            Symbol: asset.Symbol));
    }

    private void HandleMarketsChanged()
    {
        var changed = false;

        lock (_sync)
        {
            var running = _markets.IsRunning;

            if (running && _markets.SessionId != _sessionId && _markets.SessionId != Guid.Empty)
            {
                _sessionId = _markets.SessionId;
                _entries.Clear();
                _mentioned.Clear();
                _reactions.Clear();
                _awaitingReaction = null;
                changed = true;
            }

            if (running && !_wasRunning)
            {
                var assets = _assets.Assets.Count;
                _entries.Insert(0, new SessionEvent(
                    _markets.StartedAt ?? _clock.GetLocalNow(),
                    SessionEventKind.System,
                    $"Торги запущены · {Counted(assets, "актив", "актива", "активов")} · {Counted(_markets.Accounts.Count, "агент", "агента", "агентов")}"));
                changed = true;
            }
            else if (!running && _wasRunning)
            {
                _entries.Insert(0, new SessionEvent(
                    _markets.EndedAt ?? _clock.GetLocalNow(),
                    SessionEventKind.System,
                    "Торги остановлены"));
                changed = true;
            }

            _wasRunning = running;

            if (running)
            {
                changed |= NoteAgentsLocked();
                changed |= NoteReactionLocked();
            }

            if (changed)
            {
                Revision++;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Позиция стратегии в активе ушла от последнего упоминания на заметную
    /// долю — событие «Трендовые нарастили GLEN до 184 шт».
    /// </summary>
    private bool NoteAgentsLocked()
    {
        var accounts = _markets.Accounts;

        if (accounts.Count == 0)
        {
            return false;
        }

        var now = _clock.GetLocalNow();
        var added = false;

        foreach (var group in accounts.GroupBy(account => account.AgentType).OrderBy(group => group.Key))
        {
            foreach (var asset in _assets.Assets)
            {
                var positions = group
                    .Select(account => account.Positions.GetValueOrDefault(asset.Id))
                    .Where(position => position is not null)
                    .ToArray();

                if (positions.Length == 0)
                {
                    continue;
                }

                var position = positions.Sum(item => item!.Quantity);
                var initial = positions.Sum(item => item!.InitialQuantity);
                var key = (group.Key, asset.Id);

                if (!_mentioned.TryGetValue(key, out var last))
                {
                    // Первое наблюдение — точка отсчёта, а не событие.
                    _mentioned[key] = (initial, now - AgentEventCooldown);
                    last = _mentioned[key];
                }

                var threshold = Math.Max(1m, initial * NoticeableShare);

                if (Math.Abs(position - last.Position) < threshold || now - last.At < AgentEventCooldown)
                {
                    continue;
                }

                var verb = position > last.Position ? "нарастили" : "сократили";
                _entries.Insert(0, new SessionEvent(
                    now,
                    SessionEventKind.Agents,
                    $"{AgentDisplay.GetGroupLabel(group.Key)} {verb} {asset.Symbol} до {NumberFormat.Count0(position)} шт",
                    Strategy: group.Key,
                    Symbol: asset.Symbol));
                _mentioned[key] = (position, now);
                added = true;
            }
        }

        return added;
    }

    /// <summary>
    /// Первая сделка-решение новостных агентов после последней новости — по
    /// каждому рынку, который она задела. Смотрит решения последнего шага:
    /// агентов, которые после новости купили или продали, интерфейс не
    /// выдумывает, а видит.
    /// </summary>
    private bool NoteReactionLocked()
    {
        if (_awaitingReaction is not { } awaiting)
        {
            return false;
        }

        if (_markets.Tick - awaiting.Tick > ReactionWindow)
        {
            _awaitingReaction = null;
            return false;
        }

        var newsAgents = _markets.Accounts
            .Where(account => account.AgentType == AgentType.NewsDriven)
            .Select(account => account.AgentName)
            .ToHashSet(StringComparer.Ordinal);

        if (!_reactions.TryGetValue(awaiting.News.At, out var reactions))
        {
            reactions = [];
            _reactions[awaiting.News.At] = reactions;
        }

        var noted = false;
        foreach (var market in _markets.Markets)
        {
            var decision = market.Last.Decisions.FirstOrDefault(decision =>
                newsAgents.Contains(decision.AgentName) && decision.Action is TradeAction.Buy or TradeAction.Sell);
            var asset = _assets.Find(market.AssetId);

            if (decision is null || asset is null || reactions.Any(reaction => reaction.Symbol == asset.Symbol))
            {
                continue;
            }

            reactions.Add(new NewsReaction(asset.Symbol, decision.Action));
            noted = true;
        }

        return noted;
    }

    private static string Counted(int count, string one, string few, string many) =>
        $"{count} {NumberFormat.Plural(count, one, few, many)}";

    private void Append(SessionEvent entry)
    {
        lock (_sync)
        {
            _entries.Insert(0, entry);
            Revision++;
        }

        Changed?.Invoke();
    }
}
