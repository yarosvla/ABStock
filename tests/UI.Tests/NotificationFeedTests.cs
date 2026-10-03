using ABStock.Application.Assets;
using ABStock.Shared;
using ABStock.UI.Services;
using System.Globalization;

namespace ABStock.UI.Tests;

/// <summary>
/// Лента уведомлений колокольчика. Самые хрупкие места: переход позиции
/// агента через ноль — теперь по каждому активу — и сброс ленты при новой
/// сессии.
///
/// Переход через ноль хрупок потому, что событие определяется не сделкой, а
/// сравнением соседних шагов, и точка отсчёта — стартовый запас агента.
/// </summary>
public class NotificationFeedTests
{
    private static readonly AssetRegistry Registry = BuildRegistry();
    private static readonly Guid Glen = Registry.Find("GLEN")!.Id;
    private static readonly Guid Kvan = Registry.Find("KVAN")!.Id;

    // ─────────────────────── переход позиции через ноль ───────────────────────

    [Fact]
    public void Агент_вышедший_со_стартовым_запасом_не_считается_открывшим_позицию()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (50m, 50m)));

        markets.Step(Account("Trend", glen: (48m, 50m)));

        Assert.DoesNotContain(feed.Entries, entry => entry.Kind == NotificationKind.Trade);
    }

    [Fact]
    public void Агент_вышедший_без_бумаг_и_купивший_открыл_позицию_в_этом_активе()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (0m, 0m)));

        markets.Step(Account("Trend", glen: (12m, 0m)));

        var entry = Assert.Single(feed.Entries, item => item.Kind == NotificationKind.Trade);
        Assert.Equal("Агент открыл позицию", entry.Title);
        Assert.Equal("GLEN", entry.Symbol);
        Assert.Contains("Трендовый 1", entry.Detail);
    }

    [Fact]
    public void Закрытие_позиции_даёт_событие()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (50m, 50m)));

        markets.Step(Account("Trend", glen: (0m, 50m)));

        Assert.Equal("Агент закрыл позицию", feed.Entries[0].Title);
    }

    [Fact]
    public void Разворот_через_ноль_даёт_одно_событие_а_не_два()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (5m, 5m)));

        markets.Step(Account("Trend", glen: (-3m, 5m)));

        var entry = Assert.Single(feed.Entries, item => item.Kind == NotificationKind.Trade);
        Assert.Equal("Агент развернул позицию", entry.Title);
    }

    [Fact]
    public void Позиции_в_разных_активах_считаются_отдельно()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (50m, 50m), kvan: (0m, 0m)));

        markets.Step(Account("Trend", glen: (60m, 50m), kvan: (4m, 0m)));

        var entry = Assert.Single(feed.Entries, item => item.Kind == NotificationKind.Trade);
        Assert.Equal("KVAN", entry.Symbol);
    }

    // ─────────────────────────────── сессия ───────────────────────────────

    [Fact]
    public void Новая_сессия_очищает_ленту()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (0m, 0m)));
        markets.Step(Account("Trend", glen: (3m, 0m)));
        markets.Stop();

        markets.Start(Account("Trend", glen: (0m, 0m)));

        Assert.Equal("Торги запущены", Assert.Single(feed.Entries).Title);
    }

    [Fact]
    public void Запуск_торгов_сообщает_активы_агентов_и_капитал()
    {
        var (feed, markets, _) = Build();

        markets.Start(Account("Trend", glen: (50m, 50m)), Account("Maker", glen: (50m, 50m)));

        var entry = Assert.Single(feed.Entries);
        Assert.Equal("Торги запущены", entry.Title);
        Assert.Equal("2 актива · 2 агента · капитал 200 000 ₽", entry.Detail);
    }

    [Fact]
    public void Запуск_сообщается_один_раз_а_не_на_каждом_шаге()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (50m, 50m)));

        markets.Step(Account("Trend", glen: (50m, 50m)));
        markets.Step(Account("Trend", glen: (50m, 50m)));

        Assert.Single(feed.Entries, entry => entry.Title == "Торги запущены");
    }

    [Fact]
    public void Остановка_торгов_пишется_системным_событием()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (50m, 50m)));

        markets.Stop();

        Assert.Equal("Торги остановлены", feed.Entries[0].Title);
    }

    // ─────────────────────────────── новости ───────────────────────────────

    [Fact]
    public void Введённая_новость_попадает_в_ленту_один_раз_с_самым_задетым_активом()
    {
        var (feed, markets, events) = Build();
        markets.Start(Account("Trend", glen: (50m, 50m)));

        events.Add("Минэнерго продлило субсидии", ("GLEN", 0.38m), ("KVAN", 0.74m));
        events.Add("Минэнерго продлило субсидии", ("GLEN", 0.38m), ("KVAN", 0.74m), sameMoment: true);

        var entry = Assert.Single(feed.Entries, item => item.Kind == NotificationKind.News);
        Assert.Equal("KVAN", entry.Symbol);
        Assert.Contains("задела 2 из 2 · сильнее всего KVAN 0,74", entry.Detail);
    }

    // ─────────────────────────────── общее ───────────────────────────────

    [Fact]
    public void Лента_не_растёт_дальше_пятидесяти_событий()
    {
        var (feed, _, _) = Build();

        for (var i = 0; i < 60; i++)
        {
            feed.NoteAssetCreated($"Актив {i}", "GLEN");
        }

        Assert.Equal(50, feed.Entries.Count);
    }

    [Fact]
    public void Свежее_событие_стоит_первым()
    {
        var (feed, _, _) = Build();

        feed.NoteAssetCreated("Первый", "GLEN");
        feed.NoteAssetCreated("Второй", "KVAN");

        Assert.Equal("Второй", feed.Entries[0].Detail);
    }

    [Fact]
    public void Прочтение_помечает_все_события()
    {
        var (feed, _, _) = Build();
        feed.NoteAssetCreated("Гелиос Энерго", "GLEN");
        feed.NoteAssetCreated("КвантЭнерго", "KVAN");

        feed.MarkAllRead();

        Assert.All(feed.Entries, entry => Assert.True(entry.IsRead));
    }

    [Fact]
    public void Создание_актива_во_время_торгов_говорит_что_он_вступил_в_торги()
    {
        var (feed, markets, _) = Build();
        markets.Start(Account("Trend", glen: (50m, 50m)));

        feed.NoteAssetCreated("Аквамарин Агро", "AKAG");

        var entry = feed.Entries[0];
        Assert.Equal(NotificationKind.System, entry.Kind);
        Assert.Equal("AKAG", entry.Symbol);
        Assert.Equal("Аквамарин Агро · вступил в торги", entry.Detail);
    }

    // ─────────────────────────────── опоры ───────────────────────────────

    private static (NotificationFeed Feed, FakeMarkets Markets, FakeEvents Events) Build()
    {
        // Та же культура, что выставляет Program.cs: дробная часть — запятая
        // (раздел 10), разряды — узкий неразрывный пробел.
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
        culture.NumberFormat.NumberGroupSeparator = " ";
        CultureInfo.CurrentCulture = culture;

        var markets = new FakeMarkets();
        var events = new FakeEvents();
        return (new NotificationFeed(markets, events, Registry), markets, events);
    }

    /// <param name="glen">Позиция и стартовый запас в GLEN.</param>
    private static AgentAccountSnapshot Account(
        string name,
        (decimal Quantity, decimal Initial) glen,
        (decimal Quantity, decimal Initial)? kvan = null)
    {
        var positions = new Dictionary<Guid, AgentPositionSnapshot>
        {
            [Glen] = new(Glen, glen.Quantity, 0m, glen.Initial, 100m)
        };

        if (kvan is { } k)
        {
            positions[Kvan] = new(Kvan, k.Quantity, 0m, k.Initial, 100m);
        }

        var type = name.StartsWith("Maker", StringComparison.Ordinal) ? AgentType.MarketMaker : AgentType.TrendFollowing;
        return new(name, type, 100_000m, 0m, 100_000m, 100_000m, 100_000m, positions);
    }

    private static AssetRegistry BuildRegistry()
    {
        var registry = new AssetRegistry(new InMemoryAssetCatalog());
        registry.Add(AssetRegistryTests.Draft("Гелиос Энерго"), AssetRegistryTests.Profile());
        registry.Add(AssetRegistryTests.Draft("КвантЭнерго"), AssetRegistryTests.Profile());
        return registry;
    }

    private sealed class FakeMarkets : ISessionMarkets
    {
        public bool IsRunning { get; private set; }
        public bool HasSession => SessionId != Guid.Empty;
        public Guid SessionId { get; private set; }
        public DateTimeOffset? StartedAt { get; private set; }
        public DateTimeOffset? EndedAt { get; private set; }
        public int Tick { get; private set; }
        public IReadOnlyList<AssetMarket> Markets => [];
        public IReadOnlyList<AgentAccountSnapshot> Accounts { get; private set; } = [];

        public event Action? Changed;

        public AssetMarket? Market(Guid assetId) => null;
        public bool Trades(Guid assetId) => IsRunning;
        public Task StartAsync(IReadOnlyList<AgentSpec> agents) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public void SubmitNews(NewsFan fan) { }

        public void Start(params AgentAccountSnapshot[] accounts)
        {
            IsRunning = true;
            SessionId = Guid.NewGuid();
            StartedAt = DateTimeOffset.Now;
            EndedAt = null;
            Tick = 0;
            Accounts = accounts;
            Changed?.Invoke();
        }

        public void Step(params AgentAccountSnapshot[] accounts)
        {
            Tick++;
            Accounts = accounts;
            Changed?.Invoke();
        }

        public void Stop()
        {
            IsRunning = false;
            EndedAt = DateTimeOffset.Now;
            Changed?.Invoke();
        }
    }

    private sealed class FakeEvents : ISessionEvents
    {
        private readonly List<SessionEvent> _news = [];
        private DateTimeOffset _last = DateTimeOffset.Now;

        public IReadOnlyList<SessionEvent> Entries => _news.ToArray();
        public IReadOnlyList<SessionEvent> News => _news.ToArray();
        public int Revision => _news.Count;

        public event Action? Changed;

        public SessionEvent AddNews(string text, NewsFan fan) => throw new NotSupportedException();
        public NewsReaction? ReactionTo(SessionEvent news) => null;

        /// <param name="sameMoment">Повтор той же записи — лента отдаёт список целиком на каждом изменении.</param>
        public void Add(string text, (string Symbol, decimal Impact) first, (string Symbol, decimal Impact) second, bool sameMoment = false)
        {
            if (!sameMoment)
            {
                _last = _last.AddSeconds(1);
                var signals = new Dictionary<string, NewsSignal>
                {
                    [first.Symbol] = new(SignalPolarity.Positive, 0.46m, first.Impact, "") { MatchScore = 0.5m },
                    [second.Symbol] = new(SignalPolarity.Positive, 0.46m, second.Impact, "") { MatchScore = 0.5m }
                };
                _news.Insert(0, new SessionEvent(_last, SessionEventKind.News, text, NewsFan.FromSignals(Registry.Assets, signals)));
            }

            Changed?.Invoke();
        }
    }
}
