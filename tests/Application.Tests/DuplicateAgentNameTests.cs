using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

/// <summary>
/// Имя агента зашито константой в классе стратегии, поэтому несколько агентов
/// одного типа получают одинаковое имя. По имени агенты сопоставляются в
/// ApplyTradesToAgents, OrderFinancialGuard и AgentReservations — на дубликатах
/// ToDictionary бросает исключение прямо на первом тике со сделками.
/// </summary>
public sealed class DuplicateAgentNameTests
{
    [Fact]
    public async Task ManyAgentsOfSameType_StartAndTradeWithoutNameCollisions()
    {
        // Состав из задачи: 3 / 2 / 1 / 3 = девять агентов.
        var config = new SimulationConfig(
            "Состав из девяти агентов",
            "Проверка уникальности имён.",
            AssetType.Stock,
            124.35m,
            TimeSpan.FromMilliseconds(10),
            [
                new AgentSpec(AgentType.TrendFollowing, 110_000m, 60m),
                new AgentSpec(AgentType.TrendFollowing, 110_000m, 60m),
                new AgentSpec(AgentType.TrendFollowing, 110_000m, 60m),
                new AgentSpec(AgentType.CounterTrend, 110_000m, 60m),
                new AgentSpec(AgentType.CounterTrend, 110_000m, 60m),
                new AgentSpec(AgentType.MarketMaker, 140_000m, 160m),
                new AgentSpec(AgentType.NewsDriven, 105_000m, 55m),
                new AgentSpec(AgentType.NewsDriven, 105_000m, 55m),
                new AgentSpec(AgentType.NewsDriven, 105_000m, 55m)
            ]);

        var runner = new ServiceCollection()
            .AddABStockApplication()
            .BuildServiceProvider()
            .GetRequiredService<ISimulationRunner>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tradesSeen = new TaskCompletionSource<SimulationTickResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? loopFailure = null;

        void HandleTick(SimulationTickResult result)
        {
            try
            {
                // Ждём тик, где сделки реально прошли: именно на нём падал
                // ToDictionary по дублирующимся именам.
                if (result.Snapshot.RecentTrades.Count > 0)
                {
                    tradesSeen.TrySetResult(result);
                }
            }
            catch (Exception ex)
            {
                loopFailure = ex;
                tradesSeen.TrySetException(ex);
            }
        }

        runner.OnTick += HandleTick;

        try
        {
            await runner.StartAsync(config, cts.Token);

            var finished = await Task.WhenAny(tradesSeen.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.Null(loopFailure);
            Assert.Same(tradesSeen.Task, finished);

            var tick = await tradesSeen.Task;

            Assert.Equal(9, tick.Agents.Count);

            var names = tick.Agents.Select(a => a.Name).ToArray();
            Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());

            // Тип сохраняется — группировка в таблице идёт по нему, не по имени.
            Assert.Equal(3, tick.Agents.Count(a => a.Type == AgentType.TrendFollowing));
            Assert.Equal(2, tick.Agents.Count(a => a.Type == AgentType.CounterTrend));
            Assert.Equal(1, tick.Agents.Count(a => a.Type == AgentType.MarketMaker));
            Assert.Equal(3, tick.Agents.Count(a => a.Type == AgentType.NewsDriven));

            Assert.True(tick.Snapshot.RecentTrades.Count > 0, "сделки должны идти");
        }
        finally
        {
            runner.OnTick -= HandleTick;
            await cts.CancelAsync();
            await runner.StopAsync();
        }
    }
}
