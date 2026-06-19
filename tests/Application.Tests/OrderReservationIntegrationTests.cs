using ABStock.Agents;
using ABStock.Agents.Models;
using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

public sealed class OrderReservationIntegrationTests
{
    [Fact]
    public async Task RestingOrderReservesCash_PreventingReuseOnLaterTicks()
    {
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IAgentFactory>(new RepeatingBuyerAgentFactory())
            .AddABStockApplication()
            .BuildServiceProvider();

        var runner = serviceProvider.GetRequiredService<ISimulationRunner>();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var tickSource = new TaskCompletionSource<SimulationTickResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        void HandleTick(SimulationTickResult result)
        {
            if (result.Tick >= 3)
            {
                tickSource.TrySetResult(result);
                cancellationTokenSource.Cancel();
            }
        }

        runner.OnTick += HandleTick;

        await runner.StartAsync(CreateConfig(), cancellationTokenSource.Token);

        try
        {
            var completedTask = await Task.WhenAny(tickSource.Task, Task.Delay(TimeSpan.FromSeconds(5)));

            Assert.Same(tickSource.Task, completedTask);

            var tick = await tickSource.Task;

            var bid = Assert.Single(tick.OrderBook.Bids);
            Assert.Equal(100m, bid.Price);
            Assert.Equal(1m, bid.Quantity);
            Assert.Equal(1, bid.OrdersCount);
        }
        finally
        {
            runner.OnTick -= HandleTick;
            cancellationTokenSource.Cancel();
            await runner.StopAsync();
        }
    }

    private static SimulationConfig CreateConfig() =>
        new(
            "Reservation Test Asset",
            "Asset used by the reservation integration test.",
            AssetType.Stock,
            124.35m,
            TimeSpan.FromMilliseconds(10),
            [new AgentSpec(AgentType.TrendFollowing, 150m, 0m)]);

    private sealed class RepeatingBuyerAgentFactory : IAgentFactory
    {
        public IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs) =>
            [new RepeatingBuyerAgent()];
    }

    private sealed class RepeatingBuyerAgent : ITradeAgent
    {
        public AgentState State { get; } = new()
        {
            AgentName = "RepeatBuyer",
            AgentType = AgentType.TrendFollowing,
            Cash = 150m,
            Position = 0m
        };

        public AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal) =>
            new(
                State.AgentName,
                TradeAction.Buy,
                "Repeating limit buy",
                [
                    new Order(
                        Guid.NewGuid(),
                        State.AgentName,
                        OrderSide.Buy,
                        OrderType.Limit,
                        100m,
                        1m,
                        DateTimeOffset.UtcNow)
                ]);
    }
}
