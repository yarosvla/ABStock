using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation;
using ABStock.Shared;
using ABStock.UI.Services;
using System.Globalization;

namespace ABStock.UI.Tests;

/// <summary>
/// Лента уведомлений колокольчика. Проверяются два самых хрупких места:
/// переход позиции агента через ноль и сброс ленты при смене прогона.
///
/// Переход через ноль хрупок потому, что событие определяется не сделкой, а
/// сравнением двух соседних тиков, и точка отсчёта на первом тике агента
/// выбирается отдельным правилом. Сброс хрупок потому, что рядом иначе
/// окажутся события прошлого запуска и счётчики нынешнего.
/// </summary>
public class NotificationFeedTests
{
    private static AgentSnapshot Agent(
        string name,
        decimal position,
        decimal initialCash = 100_000m,
        decimal? initialPortfolio = null) =>
        new(
            name,
            AgentType.TrendFollowing,
            Cash: 100_000m,
            Position: position,
            PortfolioValue: 100_000m,
            InitialCash: initialCash,
            InitialPortfolioValue: initialPortfolio ?? initialCash);

    private static SimulationTickResult Tick(params AgentSnapshot[] agents) =>
        new(
            Tick: 1,
            Snapshot: new MarketSnapshot(100m, 99m, 101m, 0m, [], []),
            OrderBook: new OrderBookSnapshot([], []),
            Agents: agents,
            Decisions: []);

    private static (NotificationFeed Feed, FakeRunner Runner, FakeNewsFeed News) Build()
    {
        // Та же культура, что выставляет Program.cs: дробная часть отделяется
        // запятой (раздел 10). Без этого тест проверял бы формат, которого в
        // работающем приложении не бывает, — «1.41» вместо «1,41».
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

        var runner = new FakeRunner();
        var news = new FakeNewsFeed();
        var feed = new NotificationFeed(runner, news, new FakeHistoryReader());
        return (feed, runner, news);
    }

    // ─────────────────────── переход позиции через ноль ───────────────────────

    [Fact]
    public void Агент_вышедший_с_позицией_не_считается_открывшим_её()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        // Портфель на входе отличается от денег — агент заведён сразу с
        // позицией, значит он её не открывал.
        runner.Tick(Tick(Agent("a", position: 55m, initialCash: 100_000m, initialPortfolio: 106_800m)));

        Assert.DoesNotContain(feed.Entries, entry => entry.Kind == NotificationKind.Trade);
    }

    [Fact]
    public void Агент_вышедший_пустым_и_купивший_на_первом_тике_открыл_позицию()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        // Портфель на входе равен деньгам — агент вышел пустым. Позицию на
        // первом же тике он именно открыл, и проглатывать это событие вместе
        // с точкой отсчёта нельзя.
        runner.Tick(Tick(Agent("a", position: 40m, initialCash: 100_000m, initialPortfolio: 100_000m)));

        var entry = Assert.Single(feed.Entries, e => e.Kind == NotificationKind.Trade);
        Assert.Equal("Агент открыл позицию", entry.Title);
        Assert.Contains("длинная", entry.Detail);
    }

    [Fact]
    public void Закрытие_позиции_даёт_событие()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        runner.Tick(Tick(Agent("a", 55m, initialPortfolio: 106_800m)));
        runner.Tick(Tick(Agent("a", 0m, initialPortfolio: 106_800m)));

        var entry = Assert.Single(feed.Entries, e => e.Kind == NotificationKind.Trade);
        Assert.Equal("Агент закрыл позицию", entry.Title);
    }

    [Fact]
    public void Разворот_через_ноль_даёт_одно_событие_а_не_два()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        runner.Tick(Tick(Agent("a", 55m, initialPortfolio: 106_800m)));
        runner.Tick(Tick(Agent("a", -30m, initialPortfolio: 106_800m)));

        var entry = Assert.Single(feed.Entries, e => e.Kind == NotificationKind.Trade);
        Assert.Equal("Агент развернул позицию", entry.Title);
    }

    [Fact]
    public void Движение_позиции_без_перехода_через_ноль_событием_не_является()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        // Именно ради этого событие привязано к переходу через ноль, а не к
        // сделке: за сессию таких движений сотни.
        runner.Tick(Tick(Agent("a", 55m, initialPortfolio: 106_800m)));
        runner.Tick(Tick(Agent("a", 80m, initialPortfolio: 106_800m)));
        runner.Tick(Tick(Agent("a", 42m, initialPortfolio: 106_800m)));
        runner.Tick(Tick(Agent("a", 91m, initialPortfolio: 106_800m)));

        Assert.DoesNotContain(feed.Entries, entry => entry.Kind == NotificationKind.Trade);
    }

    // ────────────────────────── смена прогона ──────────────────────────

    [Fact]
    public void Смена_прогона_очищает_ленту()
    {
        var (feed, runner, _) = Build();
        runner.Start();
        runner.Tick(Tick(Agent("a", 40m)));

        Assert.NotEmpty(feed.Entries);

        runner.Start();

        Assert.Empty(feed.Entries);
    }

    [Fact]
    public void После_смены_прогона_позиция_отсчитывается_заново()
    {
        var (feed, runner, _) = Build();

        runner.Start();
        runner.Tick(Tick(Agent("a", 55m, initialPortfolio: 106_800m)));
        runner.Start();

        // Тот же агент с той же позицией в новом прогоне: это не закрытие и
        // не открытие, а начало новой хронологии.
        runner.Tick(Tick(Agent("a", 55m, initialPortfolio: 106_800m)));

        Assert.DoesNotContain(feed.Entries, entry => entry.Kind == NotificationKind.Trade);
    }

    // ────────────────────────── системные и новости ──────────────────────────

    [Fact]
    public void Запуск_торгов_сообщает_число_агентов_и_капитал()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        runner.Tick(Tick(
            Agent("a", 0m, initialCash: 70_000m),
            Agent("b", 0m, initialCash: 70_000m),
            Agent("c", 0m, initialCash: 100_000m)));

        var entry = Assert.Single(feed.Entries, e => e.Title == "Торги запущены");
        Assert.Equal(NotificationKind.System, entry.Kind);
        Assert.Contains("3 агента", entry.Detail);
        Assert.Contains("240", entry.Detail);
    }

    [Fact]
    public void Запуск_сообщается_один_раз_а_не_на_каждом_тике()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        runner.Tick(Tick(Agent("a", 0m)));
        runner.Tick(Tick(Agent("a", 0m)));
        runner.Tick(Tick(Agent("a", 0m)));

        Assert.Single(feed.Entries, e => e.Title == "Торги запущены");
    }

    [Fact]
    public void Введённая_новость_попадает_в_ленту_один_раз()
    {
        var (feed, runner, news) = Build();
        runner.Start();

        news.Add("Третий энергоблок введён", SignalPolarity.Positive, 1.41m);

        // Лента новостей отдаёт весь список целиком на каждое изменение —
        // вторая новость не должна продублировать первую.
        news.Add("Тариф повышен", SignalPolarity.Positive, 1.05m);

        var newsEntries = feed.Entries.Where(e => e.Kind == NotificationKind.News).ToArray();
        Assert.Equal(2, newsEntries.Length);
        Assert.Contains("Позитивная", newsEntries[0].Detail);
        Assert.Contains("1,41", newsEntries[1].Detail);
    }

    // ────────────────────────── потолок и прочтение ──────────────────────────

    [Fact]
    public void Лента_не_растёт_дальше_пятидесяти_событий()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        // Каждый тик переворачивает позицию, то есть даёт событие.
        for (var i = 0; i < 80; i++)
        {
            runner.Tick(Tick(Agent("a", i % 2 == 0 ? 10m : -10m, initialPortfolio: 101_000m)));
        }

        Assert.Equal(50, feed.Entries.Count);
    }

    [Fact]
    public void Свежее_событие_стоит_первым()
    {
        var (feed, runner, _) = Build();
        runner.Start();

        runner.Tick(Tick(Agent("a", 55m, initialPortfolio: 106_800m)));
        runner.Tick(Tick(Agent("a", 0m, initialPortfolio: 106_800m)));

        Assert.Equal("Агент закрыл позицию", feed.Entries[0].Title);
    }

    [Fact]
    public void Прочтение_помечает_все_события()
    {
        var (feed, runner, _) = Build();
        runner.Start();
        runner.Tick(Tick(Agent("a", 40m)));

        Assert.Contains(feed.Entries, entry => !entry.IsRead);

        feed.MarkAllRead();

        Assert.All(feed.Entries, entry => Assert.True(entry.IsRead));
    }

    [Fact]
    public void Создание_актива_попадает_в_системные()
    {
        var (feed, _, _) = Build();

        feed.NoteAssetCreated("Гелиос Энерго", "HLEN");

        var entry = Assert.Single(feed.Entries);
        Assert.Equal(NotificationKind.System, entry.Kind);
        Assert.Contains("Гелиос Энерго", entry.Detail);
        Assert.Contains("HLEN", entry.Detail);
    }

    // ────────────────────────────── дублёры ──────────────────────────────

    private sealed class FakeRunner : ISimulationRunner
    {
        public event Action<SimulationTickResult>? OnTick;
        public event Action? OnStateChanged;

        public SimulationTickResult? Current { get; private set; }
        public Guid CurrentRunId { get; private set; } = Guid.Empty;
        public bool IsRunning { get; private set; }

        public void Start()
        {
            CurrentRunId = Guid.NewGuid();
            IsRunning = true;
            OnStateChanged?.Invoke();
        }

        public void Stop()
        {
            IsRunning = false;
            OnStateChanged?.Invoke();
        }

        public void Tick(SimulationTickResult tick)
        {
            Current = tick;
            OnTick?.Invoke(tick);
        }

        public Task StartAsync(SimulationConfig config, CancellationToken ct = default)
        {
            Start();
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Stop();
            return Task.CompletedTask;
        }

        public void SubmitNews(NewsSignal signal) { }
    }

    private sealed class FakeNewsFeed : ISessionNewsFeed
    {
        private readonly List<SessionNewsEntry> _entries = [];

        public IReadOnlyList<SessionNewsEntry> Entries => _entries.ToArray();
        public int Revision { get; private set; }
        public event Action? Changed;

        public void Add(string text, SignalPolarity polarity, decimal impact)
        {
            _entries.Insert(0, new SessionNewsEntry(DateTimeOffset.Now, text, polarity, 0.86m, impact));
            Revision++;
            Changed?.Invoke();
        }

        public SessionNewsEntry Add(string text, NewsSignal signal)
        {
            var entry = new SessionNewsEntry(
                DateTimeOffset.Now, text, signal.Polarity, signal.Confidence, signal.ImpactScore);
            _entries.Insert(0, entry);
            Revision++;
            Changed?.Invoke();
            return entry;
        }

        public void Clear()
        {
            _entries.Clear();
            Revision++;
            Changed?.Invoke();
        }
    }

    private sealed class FakeHistoryReader : ISimulationHistoryReader
    {
        public SimulationRunSummary? GetRun(Guid runId) => null;

        public SimulationHistoryOverview GetOverview(int recentRuns = 10) =>
            new(0, 0, 0, 0, []);
    }
}
