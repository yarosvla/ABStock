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

        var runTask = runner.StartAsync(CreateConfig(), cancellationTokenSource.Token);

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
            await IgnoreCancellationAsync(runTask);
        }
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

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
