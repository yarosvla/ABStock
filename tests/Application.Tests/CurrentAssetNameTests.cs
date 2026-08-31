using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

/// <summary>
/// Имя актива идущего прогона. Его читает приветственная страница, чтобы
/// подписать кнопку «Вернуться к торгам», и вся ценность свойства в том, что
/// имя остановленного прогона наружу не выходит: иначе на титульном экране
/// стояло бы название актива, которым уже никто не торгует.
/// </summary>
public sealed class CurrentAssetNameTests
{
    [Fact]
    public async Task CurrentAssetName_IsNullBeforeStart()
    {
        var runner = BuildRunner();

        Assert.Null(runner.CurrentAssetName);
        Assert.False(runner.IsRunning);

        await Task.CompletedTask;
    }

    [Fact]
    public async Task CurrentAssetName_IsAssetOfRunningSession()
    {
        var runner = BuildRunner();

        try
        {
            await runner.StartAsync(CreateConfig("Гелиос Энерго"));

            Assert.True(runner.IsRunning);
            Assert.Equal("Гелиос Энерго", runner.CurrentAssetName);
        }
        finally
        {
            await runner.StopAsync();
        }
    }

    [Fact]
    public async Task CurrentAssetName_IsNullAfterStop()
    {
        var runner = BuildRunner();

        await runner.StartAsync(CreateConfig("Гелиос Энерго"));
        await runner.StopAsync();

        // Не пустая строка и не имя прошлого прогона: страница отличает
        // «торги идут вот этим активом» от «торги остановлены» одним чтением.
        Assert.False(runner.IsRunning);
        Assert.Null(runner.CurrentAssetName);
    }

    [Fact]
    public async Task CurrentAssetName_FollowsSecondSession()
    {
        var runner = BuildRunner();

        await runner.StartAsync(CreateConfig("Гелиос Энерго"));
        await runner.StopAsync();

        try
        {
            await runner.StartAsync(CreateConfig("Северная Руда"));

            Assert.Equal("Северная Руда", runner.CurrentAssetName);
        }
        finally
        {
            await runner.StopAsync();
        }
    }

    private static ISimulationRunner BuildRunner() =>
        new ServiceCollection()
            .AddABStockApplication()
            .BuildServiceProvider()
            .GetRequiredService<ISimulationRunner>();

    private static SimulationConfig CreateConfig(string assetName) =>
        new(
            assetName,
            "Актив прогона в тесте.",
            AssetType.Stock,
            124.35m,
            TimeSpan.FromMilliseconds(10),
            [
                new AgentSpec(AgentType.MarketMaker, 100_000m, 100m),
                new AgentSpec(AgentType.TrendFollowing, 100_000m, 50m)
            ]);
}
