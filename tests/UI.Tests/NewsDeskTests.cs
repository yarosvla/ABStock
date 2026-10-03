using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using ABStock.UI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.UI.Tests;

/// <summary>
/// Новость одна на весь рынок: разбирается по профилю каждого актива в
/// торгах и уходит сигналом в каждый задетый (DESIGN.md 16.3).
/// </summary>
public sealed class NewsDeskTests : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly IMultiAssetSimulationRunner _runner;
    private readonly AssetRegistry _registry;
    private readonly SessionMarkets _markets;
    private readonly SessionEvents _events;
    private readonly ProfileAnalyzer _analyzer = new();

    public NewsDeskTests()
    {
        _services = new ServiceCollection().AddABStockApplication().BuildServiceProvider();
        _runner = _services.GetRequiredService<IMultiAssetSimulationRunner>();
        _registry = new AssetRegistry(_services.GetRequiredService<IAssetCatalog>());
        _markets = new SessionMarkets(_runner, _registry, TimeProvider.System, TimeSpan.FromMilliseconds(40));
        _events = new SessionEvents(_markets, _registry);
    }

    [Fact]
    public async Task Новость_разбирается_по_профилю_каждого_актива_в_торгах()
    {
        Add("Гелиос Энерго");
        Add("КвантЭнерго");
        await StartAsync();

        var entry = await Desk().AnalyzeAsync("Минэнерго продлило субсидии на накопители энергии");

        Assert.Equal(["Гелиос Энерго", "КвантЭнерго"], _analyzer.Profiles.Order());
        Assert.Equal(["GLEN", "KVAN"], entry.Fan!.Assets.Where(row => row.WasAnalyzed).Select(row => row.Symbol));
        Assert.Same(entry, _events.News[0]);
        Assert.Contains("GLEN", entry.PricesAtNews!.Keys);
        await _markets.StopAsync();
    }

    [Fact]
    public async Task Актив_без_рынка_не_разбирается()
    {
        Add("Гелиос Энерго");
        await StartAsync();
        await _markets.StopAsync();
        Add("КвантЭнерго");

        var entry = await Desk().AnalyzeAsync("Минэнерго продлило субсидии на накопители энергии");

        Assert.False(entry.Fan!.For("KVAN")!.WasAnalyzed);
    }

    [Fact]
    public async Task Короткий_текст_не_разбирается()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Desk().AnalyzeAsync("   коротко   "));
        Assert.Empty(_analyzer.Profiles);
    }

    public async ValueTask DisposeAsync()
    {
        await _runner.StopAsync();
        _events.Dispose();
        _markets.Dispose();
        await _services.DisposeAsync();
    }

    private NewsDesk Desk() => new(_analyzer, _registry, _markets, _events);

    private void Add(string name) => _registry.Add(AssetRegistryTests.Draft(name), AssetRegistryTests.Profile() with { Name = name });

    private async Task StartAsync()
    {
        await _markets.StartAsync([new(AgentType.MarketMaker, 100_000m, 50m), new(AgentType.NewsDriven, 100_000m, 50m)]);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_markets.Markets.Count < _registry.Assets.Count && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    /// <summary>Анализатор, который запоминает, по каким профилям его звали.</summary>
    private sealed class ProfileAnalyzer : INewsProcessingService
    {
        public List<string> Profiles { get; } = [];

        public Task<NewsSignal> AnalyzeAsync(NewsAnalysisRequest request, CancellationToken ct = default)
        {
            lock (Profiles)
            {
                Profiles.Add(request.Profile.Name);
            }

            return Task.FromResult(new NewsSignal(SignalPolarity.Positive, 0.46m, 0.4m, "") { MatchScore = 0.5m });
        }
    }
}
