
namespace ABStock.UI.Services;

/// <summary>Вид события. Каждым видом управляет свой переключатель на «Профиле».</summary>
public enum NotificationKind
{
    /// <summary>Запуск и остановка торгов, создание актива.</summary>
    System,

    /// <summary>Введена новость: заголовок, тональность и сила влияния.</summary>
    News,

    /// <summary>Агент открыл, закрыл или развернул позицию.</summary>
    Trade
}

/// <summary>
/// Лента уведомлений колокольчика: последние 50 событий прогона.
///
/// До неё список в шапке был пустой заглушкой, а три переключателя на
/// «Профиле» управляли бы ничем.
/// </summary>
public interface INotificationFeed
{
    /// <summary>События от свежего к старому — так же, как они лежат в списке.</summary>
    IReadOnlyList<NotificationEntry> Entries { get; }

    /// <summary>Лента изменилась — шапке пора перерисоваться.</summary>
    event Action? Changed;

    /// <summary>
    /// Актив создан. Зовётся со страницы, а не ловится подпиской на реестр:
    /// колокольчик — про то, что сделал человек, а не про любое изменение
    /// каталога.
    /// </summary>
    void NoteAssetCreated(string assetName, string symbol);

    void MarkAllRead();

    void Clear();
}

/// <summary>
/// Singleton и создаётся при старте приложения — тем же рассуждением, что и
/// <see cref="ISessionMarkets"/>: сюда пишет шаг сессии, значит подписка
/// должна существовать до первого шага, иначе начало сессии было бы потеряно.
///
/// Новая сессия — новая лента: иначе рядом окажутся события прошлого запуска
/// и счётчики нынешнего (раздел 10, «один период»).
/// </summary>
public sealed class NotificationFeed : INotificationFeed, IDisposable
{
    /// <summary>Потолок ленты. Дальше человек всё равно не листает.</summary>
    private const int Capacity = 50;

    private readonly Lock _sync = new();
    private readonly ISessionMarkets _markets;
    private readonly ISessionEvents _events;
    private readonly IAssetRegistry _assets;
    private readonly List<Item> _items = [];

    /// <summary>Позиция агента в активе на прошлом шаге — по ней ловится переход через ноль.</summary>
    private readonly Dictionary<(string Agent, Guid Asset), decimal> _positions = [];

    /// <summary>Уже показанные новости: хронология отдаёт весь список целиком.</summary>
    private readonly HashSet<DateTimeOffset> _seenNews = [];

    private Guid _sessionId = Guid.Empty;
    private bool _wasRunning;

    public NotificationFeed(ISessionMarkets markets, ISessionEvents events, IAssetRegistry assets)
    {
        _markets = markets;
        _events = events;
        _assets = assets;
        _markets.Changed += HandleMarketsChanged;
        _events.Changed += HandleEventsChanged;
    }

    public IReadOnlyList<NotificationEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _items
                    .Select(item => new NotificationEntry(item.At, item.Kind, item.Title, item.Detail, item.IsRead, item.Symbol))
                    .ToArray();
            }
        }
    }

    public event Action? Changed;

    public void Dispose()
    {
        _markets.Changed -= HandleMarketsChanged;
        _events.Changed -= HandleEventsChanged;
    }

    public void NoteAssetCreated(string assetName, string symbol) =>
        Add([(NotificationKind.System, "Создан актив", _markets.IsRunning ? $"{assetName} · вступил в торги" : assetName, symbol)]);

    public void MarkAllRead()
    {
        lock (_sync)
        {
            var changed = false;

            foreach (var item in _items.Where(item => !item.IsRead))
            {
                item.IsRead = true;
                changed = true;
            }

            if (!changed)
            {
                return;
            }
        }

        Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (_items.Count == 0)
            {
                return;
            }

            _items.Clear();
        }

        Changed?.Invoke();
    }

    // ─────────────────────────── шаг сессии ───────────────────────────

    private void HandleMarketsChanged()
    {
        var pending = new List<(NotificationKind, string, string, string?)>();

        lock (_sync)
        {
            var running = _markets.IsRunning;

            if (running && _markets.SessionId != _sessionId)
            {
                _sessionId = _markets.SessionId;
                _items.Clear();
                _positions.Clear();
                _seenNews.Clear();
            }

            if (running && !_wasRunning)
            {
                var accounts = _markets.Accounts;
                var assets = _assets.Assets.Count;
                pending.Add((
                    NotificationKind.System,
                    "Торги запущены",
                    $"{assets} {NumberFormat.Plural(assets, "актив", "актива", "активов")} · " +
                    $"{accounts.Count} {NumberFormat.Plural(accounts.Count, "агент", "агента", "агентов")} · " +
                    $"капитал {NumberFormat.Money0(accounts.Sum(account => account.InitialCash))} ₽",
                    null));
            }
            else if (!running && _wasRunning)
            {
                // Длительность и число сделок — об одном периоде: о только что
                // закончившейся сессии (раздел 10).
                var trades = _markets.Markets.Sum(market => market.TradeCount);
                pending.Add((
                    NotificationKind.System,
                    "Торги остановлены",
                    $"сессия {SessionClock.Format(_markets) ?? "—"} · сделок {NumberFormat.Count0(trades)}",
                    null));
                _positions.Clear();
            }

            _wasRunning = running;

            if (running)
            {
                CollectPositionCrossingsLocked(pending);
            }
        }

        Add(pending);
    }

    /// <summary>
    /// Событие возникает, когда позиция агента в активе переходит через ноль:
    /// агент открыл, закрыл или развернул позицию в этом активе.
    ///
    /// Не каждая сделка: их за сессию сотни, и колокольчик с числом 148
    /// бесполезен. Переходов через ноль — единицы, и каждый осмыслен. Точка
    /// отсчёта — стартовый запас: агент, вышедший в сессию с бумагами,
    /// ничего не «открывал».
    /// </summary>
    private void CollectPositionCrossingsLocked(List<(NotificationKind, string, string, string?)> pending)
    {
        var accounts = _markets.Accounts;
        var names = AgentNames(accounts);

        foreach (var account in accounts)
        {
            foreach (var (assetId, position) in account.Positions)
            {
                var key = (account.AgentName, assetId);
                var current = position.Quantity;
                var previous = _positions.TryGetValue(key, out var known) ? known : position.InitialQuantity;
                _positions[key] = current;

                var was = Math.Sign(previous);
                var now = Math.Sign(current);

                if (was == now)
                {
                    continue;
                }

                var symbol = _assets.Find(assetId)?.Symbol;
                var name = names.GetValueOrDefault(account.AgentName, account.AgentName);
                var (title, detail) = (was, now) switch
                {
                    (0, > 0) => ("Агент открыл позицию", $"{name} · длинная {NumberFormat.Position0(current)}"),
                    (0, < 0) => ("Агент открыл позицию", $"{name} · короткая {NumberFormat.Position0(current)}"),
                    (_, 0) => ("Агент закрыл позицию", $"{name} · было {NumberFormat.Position0(previous)}"),
                    _ => ("Агент развернул позицию", $"{name} · {NumberFormat.Position0(previous)} → {NumberFormat.Position0(current)}")
                };

                pending.Add((NotificationKind.Trade, title, detail, symbol));
            }
        }
    }

    /// <summary>«Трендовый 1» — та же нумерация, что на «Агентах»: тип, затем порядок счёта.</summary>
    private static Dictionary<string, string> AgentNames(IReadOnlyList<ABStock.Shared.AgentAccountSnapshot> accounts)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in accounts.GroupBy(account => account.AgentType).OrderBy(group => group.Key))
        {
            var index = 1;

            foreach (var account in group)
            {
                names[account.AgentName] = $"{AgentDisplay.GetTypeLabel(group.Key)} {index++}";
            }
        }

        return names;
    }

    // ─────────────────────────────── новости ───────────────────────────────

    private void HandleEventsChanged()
    {
        var pending = new List<(NotificationKind, string, string, string?)>();

        lock (_sync)
        {
            foreach (var entry in _events.News.Reverse())
            {
                if (entry.Fan is not { } fan || !_seenNews.Add(entry.At))
                {
                    continue;
                }

                var analyzed = fan.Assets.Count(row => row.WasAnalyzed);
                var hit = fan.Hit.Count();
                var lead = fan.Lead;
                var reach = lead is null
                    ? "не задела ни один актив"
                    : $"задела {hit} из {analyzed} · сильнее всего {lead.Symbol} {lead.Strength:F2}";

                pending.Add((
                    NotificationKind.News,
                    "Введена новость",
                    $"{PolarityName(fan.Polarity)} · {reach} · {Shorten(entry.Text)}",
                    lead?.Symbol));
            }
        }

        Add(pending);
    }

    private static string PolarityName(ABStock.Shared.SignalPolarity polarity) => polarity switch
    {
        ABStock.Shared.SignalPolarity.Positive => "Позитивная",
        ABStock.Shared.SignalPolarity.Negative => "Негативная",
        _ => "Нейтральная"
    };

    /// <summary>
    /// Текст новости — одной строкой. Многоточие ставится здесь: строка
    /// уведомления узкая (360px), и CSS-обрезка съела бы и тональность.
    /// </summary>
    private static string Shorten(string text)
    {
        const int Limit = 80;
        var single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return single.Length <= Limit ? single : single[..Limit].TrimEnd() + "…";
    }

    // ─────────────────────────────── общее ───────────────────────────────

    private void Add(IReadOnlyList<(NotificationKind Kind, string Title, string Detail, string? Symbol)> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            foreach (var (kind, title, detail, symbol) in pending)
            {
                _items.Insert(0, new Item(DateTimeOffset.Now, kind, title, detail, symbol));
            }

            if (_items.Count > Capacity)
            {
                _items.RemoveRange(Capacity, _items.Count - Capacity);
            }
        }

        Changed?.Invoke();
    }

    private sealed class Item(DateTimeOffset at, NotificationKind kind, string title, string detail, string? symbol)
    {
        public DateTimeOffset At { get; } = at;
        public NotificationKind Kind { get; } = kind;
        public string Title { get; } = title;
        public string Detail { get; } = detail;
        public string? Symbol { get; } = symbol;
        public bool IsRead { get; set; }
    }
}

/// <param name="At">Когда событие произошло — время местное, то же, что в часах шапки.</param>
/// <param name="Symbol">Актив события — тикером рядом с заголовком; null у событий всей сессии.</param>
public sealed record NotificationEntry(
    DateTimeOffset At,
    NotificationKind Kind,
    string Title,
    string Detail,
    bool IsRead,
    string? Symbol = null);
