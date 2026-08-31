using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation;

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
    /// Актив создан. Зовётся со страницы, а не ловится подпиской: у
    /// <see cref="IActiveAssetContext"/> нет события об изменении, и заводить
    /// его ради одной записи в ленту незачем — так же, как «Новости»
    /// сообщают ленте о введённой новости.
    /// </summary>
    void NoteAssetCreated(string assetName, string symbol);

    void MarkAllRead();

    void Clear();
}

/// <summary>
/// Singleton и создаётся при старте приложения — тем же рассуждением, что и
/// <see cref="IAgentEquityHistory"/>: сюда пишет тик, значит подписка на
/// OnTick должна существовать до первого тика. Ленивое создание отдало бы
/// сервису первый тик только после того, как кто-то откроет страницу, и
/// начало сессии было бы потеряно.
///
/// Сброс по смене прогона — как у <see cref="ISessionNewsFeed"/>: новый
/// прогон — новая хронология, иначе рядом окажутся события прошлого запуска
/// и счётчики нынешнего (раздел 10, «один период»).
/// </summary>
public sealed class NotificationFeed : INotificationFeed, IDisposable
{
    /// <summary>Потолок ленты. Дальше человек всё равно не листает.</summary>
    private const int Capacity = 50;

    private readonly Lock _sync = new();
    private readonly ISimulationRunner _runner;
    private readonly ISessionNewsFeed _news;
    private readonly ISimulationHistoryReader _history;

    private readonly List<Item> _items = [];

    /// <summary>Позиция агента на прошлом тике — по ней ловится переход через ноль.</summary>
    private readonly Dictionary<string, decimal> _positions = new(StringComparer.Ordinal);

    /// <summary>Уже показанные новости: лента новостей отдаёт весь список целиком.</summary>
    private readonly HashSet<(DateTimeOffset At, string Text)> _seenNews = [];

    private Guid _runId = Guid.Empty;
    private DateTimeOffset? _startedAt;

    /// <summary>
    /// Торги запущены, но состава ещё не видели. Число агентов и капитал
    /// приходят с первым тиком, а не с событием смены состояния.
    /// </summary>
    private bool _awaitingStartDetails;

    public NotificationFeed(
        ISimulationRunner runner,
        ISessionNewsFeed news,
        ISimulationHistoryReader history)
    {
        _runner = runner;
        _news = news;
        _history = history;

        _runner.OnTick += HandleTick;
        _runner.OnStateChanged += HandleStateChanged;
        _news.Changed += HandleNewsChanged;
    }

    public IReadOnlyList<NotificationEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _items
                    .Select(item => new NotificationEntry(item.At, item.Kind, item.Title, item.Detail, item.IsRead))
                    .ToArray();
            }
        }
    }

    public event Action? Changed;

    public void Dispose()
    {
        _runner.OnTick -= HandleTick;
        _runner.OnStateChanged -= HandleStateChanged;
        _news.Changed -= HandleNewsChanged;
    }

    public void NoteAssetCreated(string assetName, string symbol)
    {
        Add(NotificationKind.System, "Создан актив", $"{assetName} · {symbol}");
    }

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

    // ────────────────────────────── системные ──────────────────────────────

    private void HandleStateChanged()
    {
        if (_runner.IsRunning)
        {
            lock (_sync)
            {
                DropOtherRunLocked();
                _startedAt = DateTimeOffset.Now;
                _awaitingStartDetails = true;
            }

            return;
        }

        // Остановка. Длительность и число сделок относятся к одному периоду —
        // к только что закончившейся сессии (раздел 10).
        DateTimeOffset? startedAt;
        Guid runId;

        lock (_sync)
        {
            startedAt = _startedAt;
            runId = _runId;
            _startedAt = null;
            _awaitingStartDetails = false;
            _positions.Clear();
        }

        if (startedAt is null)
        {
            return;
        }

        var elapsed = (DateTimeOffset.Now - startedAt.Value).ToString(@"hh\:mm\:ss");
        var trades = TryReadTradeCount(runId);

        var detail = trades is null
            ? $"сессия {elapsed}"
            : $"сессия {elapsed} · сделок {NumberFormat.Count0(trades.Value)}";

        Add(NotificationKind.System, "Торги остановлены", detail);
    }

    /// <summary>
    /// Число сделок берётся из той же сводки прогона, что у «Торгов» и
    /// «Агентов». Обращение к хранилищу может не удаться — тогда событие
    /// выходит без счётчика, но выходит: пропавшая остановка торгов хуже,
    /// чем остановка без числа.
    /// </summary>
    private int? TryReadTradeCount(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            return null;
        }

        try
        {
            return _history.GetRun(runId)?.TradeCount;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ──────────────────────────────── тик ────────────────────────────────

    private void HandleTick(SimulationTickResult tick)
    {
        var pending = new List<(NotificationKind Kind, string Title, string Detail)>();

        lock (_sync)
        {
            DropOtherRunLocked();

            if (_awaitingStartDetails && tick.Agents.Count > 0)
            {
                _awaitingStartDetails = false;

                var capital = tick.Agents.Sum(agent => agent.InitialCash);
                pending.Add((
                    NotificationKind.System,
                    "Торги запущены",
                    $"{NumberFormat.Count0(tick.Agents.Count)} " +
                    $"{NumberFormat.Plural(tick.Agents.Count, "агент", "агента", "агентов")} · " +
                    $"капитал {NumberFormat.Money0(capital)} ₽"));
            }

            CollectPositionCrossingsLocked(tick, pending);
        }

        foreach (var (kind, title, detail) in pending)
        {
            Add(kind, title, detail);
        }
    }

    /// <summary>
    /// Событие возникает, когда позиция агента переходит через ноль, то есть
    /// когда он открывает, закрывает или разворачивает позицию.
    ///
    /// Не каждая сделка: за сессию их порядка полутора сотен, и колокольчик
    /// с числом 148 бесполезен. Переходов через ноль за сессию единицы, и
    /// каждый из них осмыслен.
    /// </summary>
    private void CollectPositionCrossingsLocked(
        SimulationTickResult tick,
        List<(NotificationKind, string, string)> pending)
    {
        var names = AgentDisplay.BuildInstanceNames(tick.Agents);

        foreach (var agent in tick.Agents)
        {
            var current = agent.Position;

            if (!_positions.TryGetValue(agent.Name, out var previous))
            {
                // Первый тик агента. Взять за точку отсчёта то, что видно
                // сейчас, нельзя: агенты открывают позицию на первом же тике,
                // и самое интересное событие сессии — первое открытие — было
                // бы проглочено вместе с точкой отсчёта.
                //
                // Агент, вышедший в сессию пустым (портфель равен деньгам),
                // начинал с нуля, и точка отсчёта — ноль: переход поймается
                // тут же. Агент, заведённый сразу с позицией, ничего не
                // открывал — для него отсчёт от того, что есть.
                previous = agent.InitialPortfolioValue == agent.InitialCash
                    ? 0m
                    : current;
            }

            _positions[agent.Name] = current;

            var was = Math.Sign(previous);
            var now = Math.Sign(current);

            if (was == now)
            {
                continue;
            }

            var name = names.GetValueOrDefault(agent.Name, agent.Name);

            var (title, detail) = (was, now) switch
            {
                (0, > 0) => ("Агент открыл позицию", $"{name} · длинная {NumberFormat.Position0(current)}"),
                (0, < 0) => ("Агент открыл позицию", $"{name} · короткая {NumberFormat.Position0(current)}"),
                (_, 0) => ("Агент закрыл позицию", $"{name} · было {NumberFormat.Position0(previous)}"),
                _ => ("Агент развернул позицию", $"{name} · {NumberFormat.Position0(previous)} → {NumberFormat.Position0(current)}")
            };

            pending.Add((NotificationKind.Trade, title, detail));
        }
    }

    // ─────────────────────────────── новости ───────────────────────────────

    private void HandleNewsChanged()
    {
        var pending = new List<(DateTimeOffset At, string Title, string Detail)>();

        lock (_sync)
        {
            DropOtherRunLocked();

            foreach (var entry in _news.Entries.Reverse())
            {
                if (!_seenNews.Add((entry.At, entry.Text)))
                {
                    continue;
                }

                pending.Add((
                    entry.At,
                    "Введена новость",
                    $"{PolarityName(entry.Polarity)} · влияние {entry.ImpactScore:F2} · {Shorten(entry.Text)}"));
            }
        }

        foreach (var (at, title, detail) in pending)
        {
            Add(NotificationKind.News, title, detail, at);
        }
    }

    private static string PolarityName(ABStock.Shared.SignalPolarity polarity) => polarity switch
    {
        ABStock.Shared.SignalPolarity.Positive => "Позитивная",
        ABStock.Shared.SignalPolarity.Negative => "Негативная",
        _ => "Нейтральная"
    };

    /// <summary>
    /// Текст новости в строке уведомления — одной строкой. Обрезает разметка,
    /// но многоточие в самом конце длинного текста лучше поставить здесь:
    /// строка уведомления узкая (360px), и CSS-обрезка съела бы и тональность.
    /// </summary>
    private static string Shorten(string text)
    {
        const int Limit = 80;
        var single = text.Replace('\n', ' ').Replace('\r', ' ').Trim();

        return single.Length <= Limit ? single : single[..Limit].TrimEnd() + "…";
    }

    // ─────────────────────────────── общее ───────────────────────────────

    private void Add(NotificationKind kind, string title, string detail, DateTimeOffset? at = null)
    {
        lock (_sync)
        {
            DropOtherRunLocked();

            _items.Insert(0, new Item(at ?? DateTimeOffset.Now, kind, title, detail));

            if (_items.Count > Capacity)
            {
                _items.RemoveRange(Capacity, _items.Count - Capacity);
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Новый прогон — новая лента. Событие отсюда не шлётся: метод зовётся
    /// изнутри уже захваченного замка и из геттера во время отрисовки.
    /// </summary>
    private void DropOtherRunLocked()
    {
        var runId = _runner.CurrentRunId;

        if (runId == _runId)
        {
            return;
        }

        _runId = runId;
        _items.Clear();
        _positions.Clear();
        _seenNews.Clear();
    }

    private sealed class Item(DateTimeOffset at, NotificationKind kind, string title, string detail)
    {
        public DateTimeOffset At { get; } = at;
        public NotificationKind Kind { get; } = kind;
        public string Title { get; } = title;
        public string Detail { get; } = detail;
        public bool IsRead { get; set; }
    }
}

/// <param name="At">Когда событие произошло — время местное, то же, что в часах шапки.</param>
public sealed record NotificationEntry(
    DateTimeOffset At,
    NotificationKind Kind,
    string Title,
    string Detail,
    bool IsRead);
