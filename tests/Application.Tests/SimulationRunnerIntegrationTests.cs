using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

public sealed class SimulationRunnerIntegrationTests
{
    [Fact]
    public async Task StartAsync_EmitsTickWithExchangeSnapshot()
    {
        var serviceProvider = new ServiceCollection()
            .AddABStockApplication()
            .BuildServiceProvider();

        var runner = serviceProvider.GetRequiredService<ISimulationRunner>();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var tickSource = new TaskCompletionSource<SimulationTickResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleTick(SimulationTickResult result)
        {
            tickSource.TrySetResult(result);
            cancellationTokenSource.Cancel();
        }

        runner.OnTick += HandleTick;

        await runner.StartAsync(CreateConfig(), cancellationTokenSource.Token);

        try
        {
            var completedTask = await Task.WhenAny(tickSource.Task, Task.Delay(TimeSpan.FromSeconds(3)));

            Assert.Same(tickSource.Task, completedTask);

            var tick = await tickSource.Task;

            Assert.Equal(1, tick.Tick);
            Assert.True(tick.Snapshot.LastPrice > 0);
            Assert.Contains(tick.Agents, agent => agent.Type == AgentType.MarketMaker);
        }
        finally
        {
            runner.OnTick -= HandleTick;
            cancellationTokenSource.Cancel();
            await runner.StopAsync();
        }
    }

    [Fact]
    public async Task StartAsync_AppliesExecutedTradesToAgentStates()
    {
        var serviceProvider = new ServiceCollection()
            .AddABStockApplication()
            .BuildServiceProvider();

        var runner = serviceProvider.GetRequiredService<ISimulationRunner>();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var tickSource = new TaskCompletionSource<SimulationTickResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleTick(SimulationTickResult result)
        {
            if (result.Snapshot.Volume > 0)
            {
                tickSource.TrySetResult(result);
                cancellationTokenSource.Cancel();
            }
        }

        runner.OnTick += HandleTick;

        await runner.StartAsync(CreateConfig(), cancellationTokenSource.Token);

        try
        {
            var completedTask = await Task.WhenAny(tickSource.Task, Task.Delay(TimeSpan.FromSeconds(3)));

            Assert.Same(tickSource.Task, completedTask);

            var tick = await tickSource.Task;
            var marketMaker = Assert.Single(tick.Agents, agent => agent.Type == AgentType.MarketMaker);

            Assert.True(tick.Snapshot.Volume > 0);
            Assert.True(marketMaker.Cash > 100_000m);
            Assert.True(marketMaker.Position < 100m);
            Assert.Contains(tick.Agents, agent =>
                agent.Type != AgentType.MarketMaker &&
                agent.Cash < 100_000m &&
                agent.Position > 50m);
        }
        finally
        {
            runner.OnTick -= HandleTick;
            cancellationTokenSource.Cancel();
            await runner.StopAsync();
        }
    }

    [Fact]
    public async Task StartAsync_KeepsRunnerAliveUntilStopAsync()
    {
        var serviceProvider = new ServiceCollection()
            .AddABStockApplication()
            .BuildServiceProvider();

        var runner = serviceProvider.GetRequiredService<ISimulationRunner>();
        var tickSource = new TaskCompletionSource<SimulationTickResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleTick(SimulationTickResult result)
        {
            tickSource.TrySetResult(result);
        }

        runner.OnTick += HandleTick;

        await runner.StartAsync(CreateConfig());

        try
        {
            Assert.True(runner.IsRunning);

            var completedTask = await Task.WhenAny(tickSource.Task, Task.Delay(TimeSpan.FromSeconds(3)));

            Assert.Same(tickSource.Task, completedTask);
            Assert.Same(await tickSource.Task, runner.Current);
        }
        finally
        {
            runner.OnTick -= HandleTick;
            await runner.StopAsync();
        }

        Assert.False(runner.IsRunning);
    }

    private static SimulationConfig CreateConfig() =>
        new(
            "Integration Test Asset",
            "Asset used by the application integration test.",
            AssetType.Stock,
            124.35m,
            TimeSpan.FromMilliseconds(10),
            [
                new AgentSpec(AgentType.MarketMaker, 100_000m, 100m),
                new AgentSpec(AgentType.TrendFollowing, 100_000m, 50m),
                new AgentSpec(AgentType.CounterTrend, 100_000m, 50m),
                new AgentSpec(AgentType.NewsDriven, 100_000m, 50m)
            ]);

}
