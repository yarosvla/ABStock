using ABStock.Agents;
using ABStock.Agents.Models;
using ABStock.Application.Extensions;
using ABStock.Application.Simulation;
using ABStock.Exchange.Engine;
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
    public async Task StartAsync_UsesRegisteredFactoriesFromDependencyInjection()
    {
        var exchangeFactory = new RecordingExchangeEngineFactory();
        var agentFactory = new RecordingAgentFactory();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IExchangeEngineFactory>(exchangeFactory)
            .AddSingleton<IAgentFactory>(agentFactory)
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
            Assert.True(exchangeFactory.CreateWasCalled);
            Assert.True(agentFactory.CreateWasCalled);
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

    [Fact]
    public async Task StartAsync_DoesNotSubmitOrdersThatExceedAgentCashOrPosition()
    {
        var exchangeFactory = new RecordingExchangeEngineFactory();
        var agentFactory = new FixedDecisionAgentFactory(
        [
            new FixedDecisionAgent(
                "PoorBuyer",
                AgentType.TrendFollowing,
                cash: 10m,
                position: 0m,
                orders:
                [
                    CreateLimitOrder(CreateId(1), "PoorBuyer", OrderSide.Buy, price: 100m, quantity: 1m)
                ]),
            new FixedDecisionAgent(
                "EmptySeller",
                AgentType.CounterTrend,
                cash: 0m,
                position: 0m,
                orders:
                [
                    CreateLimitOrder(CreateId(2), "EmptySeller", OrderSide.Sell, price: 100m, quantity: 1m)
                ])
        ]);

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IExchangeEngineFactory>(exchangeFactory)
            .AddSingleton<IAgentFactory>(agentFactory)
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
            Assert.NotNull(exchangeFactory.CreatedEngine);
            Assert.Empty(exchangeFactory.CreatedEngine.SubmittedOrders);
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

    private sealed class RecordingExchangeEngineFactory : IExchangeEngineFactory
    {
        public bool CreateWasCalled { get; private set; }

        public RecordingExchangeEngine? CreatedEngine { get; private set; }

        public IExchangeEngine Create(decimal startPrice)
        {
            CreateWasCalled = true;
            CreatedEngine = new RecordingExchangeEngine(startPrice);
            return CreatedEngine;
        }
    }

    private sealed class RecordingExchangeEngine : IExchangeEngine
    {
        private readonly MarketSnapshot _snapshot;

        public List<Order> SubmittedOrders { get; } = [];

        public RecordingExchangeEngine(decimal startPrice)
        {
            _snapshot = new MarketSnapshot(startPrice, null, null, 0m, [startPrice], []);
        }

        public MarketSnapshot Submit(Order order)
        {
            SubmittedOrders.Add(order);
            return _snapshot;
        }

        public MarketSnapshot SubmitMany(IEnumerable<Order> orders)
        {
            SubmittedOrders.AddRange(orders);
            return _snapshot;
        }

        public SubmitResult SubmitWithResult(Order order) => SubmitManyWithResult([order]);

        public SubmitResult SubmitManyWithResult(IEnumerable<Order> orders)
        {
            var orderArray = orders.ToArray();
            SubmittedOrders.AddRange(orderArray);
            return new SubmitResult(_snapshot, [], orderArray, []);
        }

        public bool CancelOrder(Guid orderId) => false;

        public int CancelOrdersByAgent(string agentName) => 0;

        public MarketSnapshot GetSnapshot() => _snapshot;

        public OrderBookSnapshot GetOrderBookSnapshot(int depth = 5) => new([], []);
    }

    private sealed class RecordingAgentFactory : IAgentFactory
    {
        public bool CreateWasCalled { get; private set; }

        public IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs)
        {
            CreateWasCalled = true;
            return [new HoldAgent()];
        }
    }

    private sealed class HoldAgent : ITradeAgent
    {
        public AgentState State { get; } = new()
        {
            AgentName = "TestAgent",
            AgentType = AgentType.MarketMaker,
            Cash = 0m,
            Position = 0m
        };

        public AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal) =>
            new(State.AgentName, TradeAction.Hold, "Test agent holds.", []);
    }

    private sealed class FixedDecisionAgentFactory(IReadOnlyList<ITradeAgent> agents) : IAgentFactory
    {
        public IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs) => agents;
    }

    private sealed class FixedDecisionAgent : ITradeAgent
    {
        private readonly IReadOnlyList<Order> _orders;

        public AgentState State { get; }

        public FixedDecisionAgent(
            string agentName,
            AgentType agentType,
            decimal cash,
            decimal position,
            IReadOnlyList<Order> orders)
        {
            State = new AgentState
            {
                AgentName = agentName,
                AgentType = agentType,
                Cash = cash,
                Position = position
            };
            _orders = orders;
        }

        public AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal) =>
            new(State.AgentName, TradeAction.Hold, "Fixed test decision.", _orders);
    }

    private static Order CreateLimitOrder(
        Guid id,
        string agentName,
        OrderSide side,
        decimal price,
        decimal quantity) =>
        new(
            id,
            agentName,
            side,
            OrderType.Limit,
            price,
            quantity,
            DateTimeOffset.UtcNow
        );

    private static Guid CreateId(int id) =>
        Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
}
