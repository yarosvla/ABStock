using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using ABStock.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.UI.Tests;

/// <summary>
/// Рынки сессии на настоящем раннере бэкенда: шаг короткий, агенты и биржа
/// настоящие. Проверяется то, ради чего обёртка существует, — одна
/// перерисовка на шаг, итог после остановки и вход актива в идущие торги.
/// </summary>
public sealed class SessionMarketsTests : IAsyncDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(40);

    private readonly ServiceProvider _services;
    private readonly IMultiAssetSimulationRunner _runner;
    private readonly AssetRegistry _registry;
    private readonly SessionMarkets _markets;

    public SessionMarketsTests()
    {
        _services = new ServiceCollection().AddABStockApplication().BuildServiceProvider();
        _runner = _services.GetRequiredService<IMultiAssetSimulationRunner>();
        _registry = new AssetRegistry(_services.GetRequiredService<IAssetCatalog>());
        _markets = new SessionMarkets(_runner, _registry, TimeProvider.System, Step);
    }

    [Fact]
    public async Task Шаг_по_трём_рынкам_даёт_одну_перерисовку()
    {
        AddAssets("Гелиос Энерго", "КвантЭнерго", "Балтийский Транзит");
        var rounds = 0;
        var ticks = 0;
        _runner.OnMarketTick += _ => Interlocked.Increment(ref ticks);
        _markets.Changed += () => Interlocked.Increment(ref rounds);

        await _markets.StartAsync(Agents());
        await WaitAsync(() => _markets.Tick >= 4);
        await _markets.StopAsync();

        // Тиков по рынкам втрое больше, чем шагов, а перерисовок — по одной
        // на шаг (плюс запуск и остановка).
        Assert.True(rounds <= ticks / 3 + 2, $"тиков {ticks}, перерисовок {rounds}");
    }

    [Fact]
    public async Task После_остановки_итог_сессии_остаётся_на_экране()
    {
        AddAssets("Гелиос Энерго", "КвантЭнерго");

        await _markets.StartAsync(Agents());
        await WaitAsync(() => _markets.Markets.Count == 2 && _markets.Tick >= 2);
        await _markets.StopAsync();

        Assert.False(_markets.IsRunning);
        Assert.True(_markets.HasSession);
        Assert.NotNull(_markets.EndedAt);
        Assert.Equal(["GLEN", "KVAN"], _markets.Markets.Select(market => _registry.Find(market.AssetId)!.Symbol));
        Assert.NotEmpty(_markets.Accounts);
    }

    [Fact]
    public async Task Цена_открытия_это_стартовая_цена_актива()
    {
        var asset = AddAssets("Гелиос Энерго")[0];

        await _markets.StartAsync(Agents());
        await WaitAsync(() => _markets.Market(asset.Id) is not null);
        await _markets.StopAsync();

        var market = _markets.Market(asset.Id)!;
        Assert.Equal(asset.StartPrice, market.Open);
        Assert.Equal(asset.StartPrice, market.Prices[0]);
    }

    [Fact]
    public async Task Актив_созданный_во_время_торгов_вступает_в_них_сразу()
    {
        AddAssets("Гелиос Энерго");
        await _markets.StartAsync(Agents());
        await WaitAsync(() => _markets.Tick >= 1);

        var late = AddAssets("КвантЭнерго")[0];
        await WaitAsync(() => _markets.Market(late.Id) is not null);

        Assert.True(_markets.Trades(late.Id));
        // У каждого агента появилась позиция в новом активе — стартовый запас.
        Assert.All(_markets.Accounts, account => Assert.True(account.Positions.ContainsKey(late.Id)));
        await _markets.StopAsync();
    }

    [Fact]
    public async Task Новая_сессия_не_тащит_рынки_прошлой()
    {
        var first = AddAssets("Гелиос Энерго")[0];
        await _markets.StartAsync(Agents());
        await WaitAsync(() => _markets.Market(first.Id) is not null);
        await _markets.StopAsync();
        var previous = _markets.SessionId;

        _registry.Archive(first.Symbol);
        var second = AddAssets("КвантЭнерго")[0];
        await _markets.StartAsync(Agents());
        await WaitAsync(() => _markets.Market(second.Id) is not null);
        await _markets.StopAsync();

        Assert.NotEqual(previous, _markets.SessionId);
        Assert.Null(_markets.Market(first.Id));
    }

    public async ValueTask DisposeAsync()
    {
        await _runner.StopAsync();
        _markets.Dispose();
        await _services.DisposeAsync();
    }

    private SessionAsset[] AddAssets(params string[] names) =>
        names.Select(name => _registry.Add(AssetRegistryTests.Draft(name), AssetRegistryTests.Profile())).ToArray();

    private static AgentSpec[] Agents() =>
    [
        new(AgentType.MarketMaker, 100_000m, 50m),
        new(AgentType.TrendFollowing, 100_000m, 50m),
        new(AgentType.CounterTrend, 100_000m, 50m)
    ];

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
