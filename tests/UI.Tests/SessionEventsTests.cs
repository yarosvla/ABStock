using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using ABStock.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.UI.Tests;

/// <summary>
/// Хронология сессии — один список на «Новостях» и «Торгах» (DESIGN.md 9.21).
/// Системные события она пишет сама по состоянию рынков и реестра.
/// </summary>
public sealed class SessionEventsTests : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly IMultiAssetSimulationRunner _runner;
    private readonly AssetRegistry _registry;
    private readonly SessionMarkets _markets;
    private readonly SessionEvents _events;

    public SessionEventsTests()
    {
        _services = new ServiceCollection().AddABStockApplication().BuildServiceProvider();
        _runner = _services.GetRequiredService<IMultiAssetSimulationRunner>();
        _registry = new AssetRegistry(_services.GetRequiredService<IAssetCatalog>());
        _markets = new SessionMarkets(_runner, _registry, TimeProvider.System, TimeSpan.FromMilliseconds(40));
        _events = new SessionEvents(_markets, _registry);
    }

    [Fact]
    public async Task Запуск_и_остановка_пишутся_системными_событиями()
    {
        Add("Гелиос Энерго");
        Add("КвантЭнерго");

        await StartAsync();
        await _markets.StopAsync();
        await WaitAsync(() => _events.Entries.Count >= 2);

        Assert.Equal(
            ["Торги остановлены", "Торги запущены · 2 актива · 3 агента"],
            _events.Entries.Where(entry => entry.Kind == SessionEventKind.System).Select(entry => entry.Text));
    }

    [Fact]
    public async Task Актив_созданный_во_время_торгов_попадает_в_хронологию()
    {
        Add("Гелиос Энерго");
        await StartAsync();

        Add("КвантЭнерго");

        Assert.Contains(_events.Entries, entry => entry.Text == "Создан актив KVAN — вступил в торги");
        await _markets.StopAsync();
    }

    [Fact]
    public void Актив_созданный_до_запуска_хронологию_не_засоряет()
    {
        Add("Гелиос Энерго");

        Assert.Empty(_events.Entries);
    }

    [Fact]
    public async Task Новая_сессия_начинает_хронологию_заново()
    {
        Add("Гелиос Энерго");
        await StartAsync();
        _events.AddNews("Минэнерго продлило субсидии", NewsFan.FromSignals(_registry.Assets, new Dictionary<string, NewsSignal>()));
        await _markets.StopAsync();

        await StartAsync();
        await _markets.StopAsync();

        Assert.Empty(_events.News);
        Assert.Equal(2, _events.Entries.Count);
    }

    [Fact]
    public async Task Новость_стоит_первой_и_несёт_веер()
    {
        Add("Гелиос Энерго");
        await StartAsync();
        var fan = NewsFan.FromSignals(
            _registry.Assets,
            new Dictionary<string, NewsSignal> { ["GLEN"] = new(SignalPolarity.Positive, 0.46m, 0.38m, "") { MatchScore = 0.5m } });

        _events.AddNews("  Минэнерго продлило субсидии  ", fan);

        var first = _events.Entries[0];
        Assert.Equal(SessionEventKind.News, first.Kind);
        Assert.Equal("Минэнерго продлило субсидии", first.Text);
        Assert.Equal("GLEN", first.Fan?.Lead?.Symbol);
        await _markets.StopAsync();
    }

    [Fact]
    public async Task Реакция_новостных_агентов_пишется_по_каждому_задетому_активу()
    {
        // Регрессия: реакция запоминалась одна на новость — по первому рынку в
        // списке, и полоса связи на «Торгах» второго актива писала «торговали
        // другим активом», хотя новостные агенты купили и его.
        Add("Гелиос Энерго");
        Add("КвантЭнерго");
        await _markets.StartAsync(
        [
            new(AgentType.MarketMaker, 100_000m, 50m),
            new(AgentType.NewsDriven, 100_000m, 50m)
        ]);
        await WaitAsync(() => _markets.Accounts.Count > 0 && _events.Entries.Count > 0);
        var signal = new NewsSignal(SignalPolarity.Positive, 0.9m, 1m, "") { MatchScore = 0.9m };
        var fan = NewsFan.FromSignals(_registry.Assets, _registry.Assets.ToDictionary(asset => asset.Symbol, _ => signal));

        var news = _events.AddNews("Энергетика растёт", fan);
        _markets.SubmitNews(fan);
        await WaitAsync(() => _events.ReactionTo(news, "GLEN") is not null && _events.ReactionTo(news, "KVAN") is not null);

        Assert.Equal(TradeAction.Buy, _events.ReactionTo(news, "GLEN")!.Action);
        Assert.Equal(TradeAction.Buy, _events.ReactionTo(news, "KVAN")!.Action);
        await _markets.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _runner.StopAsync();
        _events.Dispose();
        _markets.Dispose();
        await _services.DisposeAsync();
    }

    private void Add(string name) => _registry.Add(AssetRegistryTests.Draft(name), AssetRegistryTests.Profile());

    private async Task StartAsync()
    {
        await _markets.StartAsync(
        [
            new(AgentType.MarketMaker, 100_000m, 50m),
            new(AgentType.TrendFollowing, 100_000m, 50m),
            new(AgentType.CounterTrend, 100_000m, 50m)
        ]);
        await WaitAsync(() => _markets.Accounts.Count > 0 && _events.Entries.Count > 0);
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Условие не наступило за 5 секунд.");
            }

            await Task.Delay(10);
        }
    }
}
