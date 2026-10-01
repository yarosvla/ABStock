using System.Collections.Concurrent;
using ABStock.Agents;
using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation;
using ABStock.Application.Simulation.Diagnostics;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

public sealed class MultiAssetSimulationRunnerIntegrationTests
{
    [Fact]
    public async Task DependencyInjection_ResolvesSameRunnerForBothInterfaces()
    {
        await using var fixture = new Fixture();
        Assert.Same(fixture.Runner, fixture.Services.GetRequiredService<ISimulationRunner>());
        Assert.Same(fixture.Runner, fixture.Services.GetRequiredService<SimulationRunner>());
    }

    [Fact]
    public async Task StartSession_UsesSameAgentsAndPublishesSeparateMarketsWithCommonAccounts()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        var spec = new AgentSpec(AgentType.MarketMaker, 1_000m, 99m)
        {
            InitialPositions = new Dictionary<Guid, decimal> { [first] = 2m, [second] = 3m }
        };
        var legacyTicks = new ConcurrentQueue<SimulationTickResult>();
        fixture.Runner.OnTick += legacyTicks.Enqueue;

        var ticks = await fixture.StartAsync([first, second], [spec]);

        Assert.Equal(1, fixture.Factory.CreateCalls);
        var agent = Assert.Single(fixture.Factory.CreatedAgents);
        Assert.Equal(new[] { first, second }, agent.Calls.Select(call => call.Context.AssetId));
        Assert.Equal(new[] { 2m, 3m }, agent.Calls.Select(call => call.Position));
        Assert.All(agent.Calls, call => Assert.Equal(fixture.Runner.CurrentSessionId, call.Context.SessionId));
        Assert.Equal(2, ticks.Count);
        Assert.All(ticks, tick => Assert.Equal(1, tick.Tick));
        Assert.Equal(100m, ticks[0].Snapshot.LastPrice);
        Assert.Equal(200m, ticks[1].Snapshot.LastPrice);
        Assert.Same(ticks[0], fixture.Runner.Current);
        Assert.Same(ticks[1], fixture.Runner.GetCurrent(second));
        Assert.Equal("First", fixture.Runner.CurrentAssetName);
        Assert.Equal(first, Assert.Single(legacyTicks).AssetId);
        Assert.All(ticks, tick =>
        {
            var account = Assert.Single(tick.Accounts);
            Assert.Equal(1_800m, account.PortfolioValue);
            Assert.Equal(1_800m, Assert.Single(tick.Agents).PortfolioValue);
            Assert.NotNull(tick.Submission);
        });
        Assert.Equal(2m, agent.State.Position);
        Assert.Equal(1_000m, agent.State.Cash);
    }

    [Fact]
    public async Task InitialScalarPosition_IsAssignedToEveryMarketWithoutDuplicatingCash()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        await fixture.StartAsync([first, second], [new(AgentType.MarketMaker, 500m, 4m)]);

        var account = Assert.Single(fixture.Runner.GetAgentAccounts());
        Assert.Equal(4m, account.Positions[first].Quantity);
        Assert.Equal(4m, account.Positions[second].Quantity);
        Assert.Equal(500m, account.Cash);
        Assert.Equal(1_700m, account.InitialPortfolioValue);
        Assert.Equal(account.InitialPortfolioValue, account.PortfolioValue);
    }

    [Fact]
    public async Task RunCycle_ReservesCommonCashBeforeVisitingNextMarket()
    {
        await using var fixture = new Fixture((agent, _, _) =>
            Decision(agent, Limit(agent.State.AgentName, OrderSide.Buy, 100m)));
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 100m);
        var submissions = new ConcurrentQueue<MarketSubmitResult>();
        fixture.Runner.OnOrdersSubmitted += submissions.Enqueue;

        var ticks = await fixture.StartAsync([first, second], [new(AgentType.TrendFollowing, 150m)]);

        Assert.Single(fixture.Runner.GetOpenOrders(first));
        Assert.Empty(fixture.Runner.GetOpenOrders(second));
        Assert.Single(ticks[0].Submission!.AcceptedOrders);
        Assert.Single(ticks[1].Submission!.RejectedOrders);
        Assert.Equal(OrderExecutionStatus.Rejected, Assert.Single(ticks[1].Submission!.OrderReports).Status);
        Assert.Equal(2, submissions.Count);
        var account = Assert.Single(fixture.Runner.GetAgentAccounts());
        Assert.Equal(150m, account.Cash);
        Assert.Equal(100m, account.ReservedCash);
        Assert.Equal(50m, account.AvailableCash);
        var calls = Assert.Single(fixture.Factory.CreatedAgents).Calls.ToArray();
        Assert.Equal(150m, calls[0].AvailableCash);
        Assert.Equal(50m, calls[1].AvailableCash);
    }

    [Fact]
    public async Task ExplicitPositionOverridesDefaultIncludingZeroAndDoesNotChangeFutureDefault()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        await fixture.StartAsync([first, second], [new AgentSpec(AgentType.MarketMaker, 500m, 4m)
        {
            InitialPositions = new Dictionary<Guid, decimal> { [first] = 0m }
        }]);

        var third = fixture.CreateAsset("Third", 300m);
        var added = fixture.Runner.AddMarket(third);

        var account = Assert.Single(added.Accounts);
        Assert.Equal(0m, account.Positions[first].Quantity);
        Assert.Equal(4m, account.Positions[second].Quantity);
        Assert.Equal(4m, account.Positions[third].Quantity);
        Assert.Equal(2_500m, account.InitialPortfolioValue);
        Assert.Equal(500m, account.Cash);
    }

    [Fact]
    public async Task NewMarketUsesConfiguredPositionNotRemainingPositionAfterTrades()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        await fixture.StartAsync([first],
            [new(AgentType.MarketMaker, 0m, 5m), new(AgentType.TrendFollowing, 1_000m)]);
        var sellerName = fixture.Runner.GetAgentAccounts()
            .Single(account => account.AgentType == AgentType.MarketMaker).AgentName;
        var buyerName = fixture.Runner.GetAgentAccounts()
            .Single(account => account.AgentType == AgentType.TrendFollowing).AgentName;
        fixture.Runner.SubmitMany(first,
        [
            Limit(sellerName, OrderSide.Sell, 100m) with { Quantity = 3m },
            new(Guid.NewGuid(), buyerName, OrderSide.Buy, OrderType.Market, null, 3m, DateTimeOffset.UtcNow)
        ]);
        var second = fixture.CreateAsset("Second", 200m);

        var added = fixture.Runner.AddMarket(second);

        var seller = added.Accounts.Single(account => account.AgentName == sellerName);
        Assert.Equal(300m, seller.Cash);
        Assert.Equal(2m, seller.Positions[first].Quantity);
        Assert.Equal(5m, seller.Positions[second].Quantity);
        Assert.Equal(1_500m, seller.InitialPortfolioValue);
        Assert.Equal(seller.InitialPortfolioValue, seller.PortfolioValue);
        var buyer = added.Accounts.Single(account => account.AgentName == buyerName);
        Assert.Equal(700m, buyer.Cash);
        Assert.Equal(3m, buyer.Positions[first].Quantity);
        Assert.Equal(0m, buyer.Positions[second].Quantity);
        Assert.Equal(3m, fixture.Runner.GetCurrent(first)!.Snapshot.Volume);
        Assert.Equal(0m, added.Snapshot.Volume);
        Assert.Equal(1, fixture.Factory.CreateCalls);
    }

    [Fact]
    public async Task RealAgentsCanTradeOnNewMarketUsingInitialInventoryAndAddressedNews()
    {
        await using var services = new ServiceCollection().AddABStockApplication().BuildServiceProvider();
        var runner = services.GetRequiredService<IMultiAssetSimulationRunner>();
        var catalog = services.GetRequiredService<IAssetCatalog>();
        var first = catalog.Create(new(new AssetProfile("First", AssetType.Stock, "First asset.", [], 1m), 100m));
        var second = catalog.Create(new(new AssetProfile("Second", AssetType.Stock, "Second asset.", [], 1m), 200m));
        var quotes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var trade = new TaskCompletionSource<SimulationTickResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.OnMarketTick += tick =>
        {
            if (tick.AssetId != second.AssetId)
            {
                return;
            }

            if (tick.Snapshot.BestAsk is not null)
            {
                quotes.TrySetResult();
            }

            if (tick.Submission?.Trades.Count > 0)
            {
                trade.TrySetResult(tick);
            }
        };

        try
        {
            await runner.StartSessionAsync(new([], TimeSpan.FromMilliseconds(20),
                [new(AgentType.MarketMaker, 10_000m, 10m), new(AgentType.NewsDriven, 10_000m)]));
            runner.AddMarket(first.AssetId);
            runner.AddMarket(second.AssetId);
            await quotes.Task.WaitAsync(TimeSpan.FromSeconds(5));

            runner.SubmitNews(second.AssetId, new NewsSignal(SignalPolarity.Positive, 1m, 1m, "Second asset news."));
            var tick = await trade.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var executed = Assert.Single(tick.Submission!.Trades);
            Assert.Equal("MarketMaker", executed.SellerAgentName);
            Assert.Equal("NewsDriven", executed.BuyerAgentName);
            Assert.Equal(1m, tick.Snapshot.Volume);
            Assert.Equal(0m, runner.GetCurrent(first.AssetId)!.Snapshot.Volume);
            Assert.Equal(2, tick.Accounts.Count);
            var seller = tick.Accounts.Single(account => account.AgentType == AgentType.MarketMaker);
            Assert.Equal(10m, seller.Positions[first.AssetId].Quantity);
            Assert.Equal(9m, seller.Positions[second.AssetId].Quantity);
            Assert.Equal(13_000m, seller.InitialPortfolioValue);
        }
        finally
        {
            await runner.StopAsync();
        }
    }

    [Fact]
    public async Task RunCycle_SettlesTradesOnceAndKeepsPositionsPerAsset()
    {
        await using var fixture = new Fixture((agent, context, _) =>
            Decision(agent, Limit(agent.State.AgentName,
                agent.State.AgentType == AgentType.TrendFollowing ? OrderSide.Buy : OrderSide.Sell,
                context.Snapshot.LastPrice)));
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        var sellerSpec = new AgentSpec(AgentType.CounterTrend, 0m)
        {
            InitialPositions = new Dictionary<Guid, decimal> { [first] = 2m, [second] = 3m }
        };

        var ticks = await fixture.StartAsync([first, second],
            [new(AgentType.TrendFollowing, 1_000m), sellerSpec]);

        Assert.All(ticks, tick =>
        {
            Assert.Equal(1m, tick.Snapshot.Volume);
            Assert.Single(tick.Submission!.Trades);
            Assert.Single(tick.Snapshot.RecentTrades);
        });
        var buyer = fixture.Runner.GetAgentAccounts().Single(account => account.AgentType == AgentType.TrendFollowing);
        var seller = fixture.Runner.GetAgentAccounts().Single(account => account.AgentType == AgentType.CounterTrend);
        Assert.Equal(700m, buyer.Cash);
        Assert.Equal(1m, buyer.Positions[first].Quantity);
        Assert.Equal(1m, buyer.Positions[second].Quantity);
        Assert.Equal(1_000m, buyer.PortfolioValue);
        Assert.Equal(300m, seller.Cash);
        Assert.Equal(1m, seller.Positions[first].Quantity);
        Assert.Equal(2m, seller.Positions[second].Quantity);
        Assert.Equal(800m, seller.PortfolioValue);
        Assert.All(ticks, tick => Assert.Equal(700m,
            tick.Agents.Single(agent => agent.Type == AgentType.TrendFollowing).Cash));
        var actualBuyer = fixture.Factory.CreatedAgents.Single(agent => agent.State.AgentType == AgentType.TrendFollowing);
        Assert.Equal(700m, actualBuyer.State.Cash);
        Assert.Equal(1m, actualBuyer.State.Position);
    }

    [Fact]
    public async Task AddMarket_InitializesInventoryOnceWithoutRestartingAgentsOrResettingOtherMarkets()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        await fixture.StartAsync([first], [new(AgentType.MarketMaker, 500m, 2m)]);
        var sessionId = fixture.Runner.CurrentSessionId;
        var firstRunId = fixture.Runner.GetRunId(first);
        var agent = Assert.Single(fixture.Factory.CreatedAgents);
        var order = Limit(agent.State.AgentName, OrderSide.Buy, 90m);
        fixture.Runner.SubmitMany(first, [order]);
        var second = fixture.CreateAsset("Second", 200m);

        var added = fixture.Runner.AddMarket(second);

        Assert.Equal(sessionId, added.SessionId);
        Assert.Equal(firstRunId, fixture.Runner.GetRunId(first));
        Assert.Equal(first, fixture.Runner.PrimaryAssetId);
        Assert.Equal(1, fixture.Factory.CreateCalls);
        Assert.Same(agent, Assert.Single(fixture.Factory.CreatedAgents));
        Assert.Equal(order.Id, Assert.Single(fixture.Runner.GetOpenOrders(first)).Id);
        var account = Assert.Single(added.Accounts);
        Assert.Equal(500m, account.Cash);
        Assert.Equal(90m, account.ReservedCash);
        Assert.Equal(2m, account.Positions[first].Quantity);
        Assert.Equal(2m, account.Positions[second].Quantity);
        Assert.Equal(2m, account.Positions[second].InitialQuantity);
        Assert.Equal(1_100m, account.InitialPortfolioValue);
        Assert.Equal(account.InitialPortfolioValue, account.PortfolioValue);
        Assert.Same(added, fixture.Runner.AddMarket(second));
        Assert.Equal(account, Assert.Single(fixture.Runner.GetCurrent(second)!.Accounts));
        Assert.Equal(2, fixture.History.Runs.Count);
        Assert.Equal(2, fixture.Runner.GetCurrentMarkets().Count);
    }

    [Fact]
    public async Task StartSession_CanStartAgentsBeforeAddingAnyMarkets()
    {
        await using var fixture = new Fixture();
        await fixture.StartAsync([], [new(AgentType.NewsDriven, 500m, 3m)]);
        Assert.True(fixture.Runner.IsRunning);
        Assert.Equal(Guid.Empty, fixture.Runner.PrimaryAssetId);
        Assert.Null(fixture.Runner.Current);
        Assert.Empty(fixture.Runner.GetCurrentMarkets());
        Assert.Empty(fixture.History.Runs);
        var initial = Assert.Single(fixture.Runner.GetAgentAccounts());
        Assert.Equal(500m, initial.PortfolioValue);
        Assert.Empty(initial.Positions);

        var assetId = fixture.CreateAsset("Later", 100m);
        var tick = fixture.Runner.AddMarket(assetId);

        Assert.Equal(assetId, fixture.Runner.PrimaryAssetId);
        Assert.Same(tick, fixture.Runner.Current);
        var account = Assert.Single(tick.Accounts);
        Assert.Equal(3m, account.Positions[assetId].Quantity);
        Assert.Equal(3m, account.Positions[assetId].InitialQuantity);
        Assert.Equal(500m, account.Cash);
        Assert.Equal(800m, account.InitialPortfolioValue);
        Assert.Equal(account.InitialPortfolioValue, account.PortfolioValue);
        Assert.Equal(1, fixture.Factory.CreateCalls);
    }

    [Fact]
    public async Task ManualSubmissionsAndCancellation_RefreshAccountsInEveryMarket()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 100m);
        await fixture.StartAsync([first, second], [new(AgentType.TrendFollowing, 150m)]);
        var name = Assert.Single(fixture.Runner.GetAgentAccounts()).AgentName;
        var firstOrder = Limit(name, OrderSide.Buy, 60m);
        var secondOrder = Limit(name, OrderSide.Buy, 60m);

        fixture.Runner.SubmitMany(first, [firstOrder]);
        fixture.Runner.SubmitMany(second, [secondOrder]);
        Assert.All(fixture.Runner.GetCurrentMarkets(),
            tick => Assert.Equal(120m, Assert.Single(tick.Accounts).ReservedCash));
        Assert.True(fixture.Runner.CancelOrder(first, firstOrder.Id));
        Assert.All(fixture.Runner.GetCurrentMarkets(), tick =>
        {
            var account = Assert.Single(tick.Accounts);
            Assert.Equal(150m, account.Cash);
            Assert.Equal(60m, account.ReservedCash);
            Assert.Equal(90m, account.AvailableCash);
        });
        Assert.Equal(secondOrder.Id, Assert.Single(fixture.Runner.GetOpenOrders(second)).Id);
        var current = fixture.Runner.Current;
        Assert.False(fixture.Runner.CancelOrder(first, firstOrder.Id));
        Assert.Same(current, fixture.Runner.Current);
        Assert.Equal(1, fixture.Runner.CancelOrdersByAgent(second, name));
        Assert.Equal(0m, Assert.Single(fixture.Runner.GetAgentAccounts()).ReservedCash);
    }

    [Fact]
    public async Task DebugApi_DefaultsToPrimaryMarketAndAcceptsExplicitAssetId()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 100m);
        await fixture.StartAsync([first, second], []);
        var debug = fixture.Services.GetRequiredService<SimulationRunner>();
        var agent = debug.AddAgent(new AgentSpec(AgentType.MarketMaker, 300m, 2m));
        Assert.Equal(2m, agent.Position);
        Assert.Equal(700m, agent.PortfolioValue);
        var request = new SimulationDebugOrderRequest(agent.Name, OrderSide.Buy, OrderType.Limit, 90m, 1m);

        Assert.Single(debug.SubmitOrder(request).AcceptedOrders);
        Assert.Single(debug.SubmitOrder(request with { AssetId = second }).AcceptedOrders);
        Assert.Single(fixture.Runner.GetOpenOrders(first));
        Assert.Single(fixture.Runner.GetOpenOrders(second));
        Assert.Equal(180m, Assert.Single(fixture.Runner.GetAgentAccounts()).ReservedCash);
    }

    [Fact]
    public async Task AddSessionAgent_RegistersOneSharedAccountWithUniqueName()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        await fixture.StartAsync([first, second], [new(AgentType.MarketMaker, 500m)]);

        var added = fixture.Runner.AddSessionAgent(new AgentSpec(AgentType.MarketMaker, 300m)
        {
            InitialPositions = new Dictionary<Guid, decimal> { [second] = 2m }
        });

        Assert.Equal("MarketMaker2", added.AgentName);
        Assert.Equal(300m, added.Cash);
        Assert.Equal(0m, added.Positions[first].Quantity);
        Assert.Equal(2m, added.Positions[second].Quantity);
        Assert.Equal(700m, added.InitialPortfolioValue);
        Assert.Equal(2, fixture.Runner.GetAgentAccounts().Count);
        Assert.All(fixture.Runner.GetCurrentMarkets(), tick => Assert.Equal(2, tick.Agents.Count));
    }

    [Fact]
    public async Task News_IsQueuedForTargetMarketAndLegacyApiTargetsOnlyPrimary()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        var primaryNews = new NewsSignal(SignalPolarity.Positive, 1m, 1m, "Primary");
        var secondNews = new NewsSignal(SignalPolarity.Negative, 1m, -1m, "Second");
        var laterNews = secondNews with { Explanation = "Later" };
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runner.OnMarketTick += tick =>
        {
            if (tick.AssetId != second)
            {
                return;
            }

            if (tick.Tick == 1)
            {
                fixture.Runner.SubmitNews(primaryNews);
                fixture.Runner.SubmitNews(second, secondNews);
                fixture.Runner.SubmitNews(second, laterNews);
            }

            if (tick.Tick == 3)
            {
                completion.TrySetResult();
            }
        };

        await fixture.StartAsync([first, second], [new(AgentType.NewsDriven, 500m)], TimeSpan.FromMilliseconds(10));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Runner.StopAsync();

        var calls = Assert.Single(fixture.Factory.CreatedAgents).Calls.ToArray();
        var primarySignals = calls.Where(call => call.Context.AssetId == first && call.News is not null);
        Assert.Same(primaryNews, Assert.Single(primarySignals).News);
        var secondarySignals = calls.Where(call => call.Context.AssetId == second && call.News is not null).ToArray();
        Assert.Equal(2, secondarySignals.Length);
        Assert.Same(secondNews, secondarySignals[0].News);
        Assert.Same(laterNews, secondarySignals[1].News);
    }

    [Fact]
    public async Task AgentAddedDuringSessionReceivesDefaultsOnExistingAndFutureMarkets()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        await fixture.StartAsync([first, second], []);

        var added = fixture.Runner.AddSessionAgent(new AgentSpec(AgentType.MarketMaker, 300m, 3m)
        {
            InitialPositions = new Dictionary<Guid, decimal> { [first] = 1m }
        });
        var third = fixture.CreateAsset("Third", 300m);
        var tick = fixture.Runner.AddMarket(third);

        Assert.Equal(1m, added.Positions[first].Quantity);
        Assert.Equal(3m, added.Positions[second].Quantity);
        Assert.Equal(1_000m, added.InitialPortfolioValue);
        var account = Assert.Single(tick.Accounts);
        Assert.Equal(added.AgentName, account.AgentName);
        Assert.Equal(3m, account.Positions[third].Quantity);
        Assert.Equal(300m, account.Cash);
        Assert.Equal(1_900m, account.InitialPortfolioValue);
        Assert.Equal(2, fixture.Factory.CreateCalls);
    }

    [Fact]
    public async Task History_UsesSeparateRunsAndMatchingAssetForEverySavedTick()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        await fixture.StartAsync([first, second], [new(AgentType.TrendFollowing, 500m)]);
        var firstRun = fixture.Runner.GetRunId(first);
        var secondRun = fixture.Runner.GetRunId(second);
        Assert.NotEqual(Guid.Empty, firstRun);
        Assert.NotEqual(firstRun, secondRun);
        var agentName = Assert.Single(fixture.Runner.GetAgentAccounts()).AgentName;

        fixture.Runner.SubmitMany(second, [Limit(agentName, OrderSide.Buy, 190m)]);

        Assert.Equal(2, fixture.History.Runs.Count);
        Assert.Equal(firstRun, fixture.Runner.CurrentRunId);
        Assert.All(fixture.History.Ticks, item =>
        {
            Assert.Equal(item.RunId, item.Tick.RunId);
            Assert.Equal(fixture.Runner.CurrentSessionId, item.Tick.SessionId);
            Assert.Equal(item.Tick.AssetId == first ? firstRun : secondRun, item.RunId);
            Assert.Equal(item.Tick.AssetId == first ? 100m : 200m, item.Tick.Snapshot.LastPrice);
        });
        Assert.Equal(4, fixture.History.Ticks.Count);
        Assert.Equal(4, fixture.History.Ticks.Select(item => (item.RunId, item.Tick.Tick)).Distinct().Count());
        Assert.All(fixture.History.Runs,
            run => Assert.Equal(run.Config.AssetName == "First" ? first : second, run.Config.AssetId));
    }

    [Fact]
    public async Task StrategyCannotChangeAuthoritativeCashOrPositionByMutatingItsState()
    {
        await using var fixture = new Fixture((agent, _, _) =>
        {
            agent.State.Cash = 1_000_000m;
            agent.State.Position = 1_000m;
            return Decision(agent, Limit(agent.State.AgentName, OrderSide.Buy, 100m));
        });
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 100m);

        var ticks = await fixture.StartAsync([first, second], [new(AgentType.TrendFollowing, 50m)]);

        Assert.All(ticks, tick => Assert.Single(tick.Submission!.RejectedOrders));
        var account = Assert.Single(fixture.Runner.GetAgentAccounts());
        Assert.Equal(50m, account.Cash);
        Assert.All(account.Positions.Values, position => Assert.Equal(0m, position.Quantity));
        var agent = Assert.Single(fixture.Factory.CreatedAgents);
        Assert.Equal(50m, agent.State.Cash);
        Assert.Equal(0m, agent.State.Position);
        Assert.All(agent.Calls, call => Assert.Equal(50m, call.Cash));
    }

    [Fact]
    public async Task UnknownMarketAndUnknownAgent_CannotAffectOtherMarkets()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        await fixture.StartAsync([first], [new(AgentType.TrendFollowing, 500m)]);
        var result = fixture.Runner.SubmitMany(first, [Limit("Unknown", OrderSide.Buy, 100m)]);
        Assert.Single(result.Result.RejectedOrders);
        Assert.Throws<KeyNotFoundException>(() => fixture.Runner.SubmitMany(Guid.NewGuid(), []));
        Assert.Throws<KeyNotFoundException>(() => fixture.Runner.SubmitNews(
            Guid.NewGuid(), new NewsSignal(SignalPolarity.Positive, 1m, 1m, "Unknown")));
        Assert.Empty(fixture.Runner.GetOpenOrders(first));
        Assert.Equal(500m, Assert.Single(fixture.Runner.GetAgentAccounts()).Cash);
    }

    [Fact]
    public async Task StopAndRestart_ClearSessionMarketsAccountsAndPendingNews()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        await fixture.StartAsync([first], [new(AgentType.NewsDriven, 500m)]);
        var sessionId = fixture.Runner.CurrentSessionId;
        var runId = fixture.Runner.CurrentRunId;
        fixture.Runner.SubmitNews(first, new NewsSignal(SignalPolarity.Positive, 1m, 1m, "Old"));

        await fixture.Runner.StopAsync();

        Assert.False(fixture.Runner.IsRunning);
        Assert.Null(fixture.Runner.Current);
        Assert.Null(fixture.Runner.CurrentAssetName);
        Assert.Equal(Guid.Empty, fixture.Runner.CurrentSessionId);
        Assert.Equal(Guid.Empty, fixture.Runner.CurrentRunId);
        Assert.Empty(fixture.Runner.GetCurrentMarkets());
        Assert.Empty(fixture.Runner.GetAgentAccounts());
        Assert.Throws<InvalidOperationException>(() => fixture.Runner.AddMarket(first));
        var restarted = await fixture.StartAsync([first], [new(AgentType.NewsDriven, 100m)]);
        Assert.NotEqual(sessionId, fixture.Runner.CurrentSessionId);
        Assert.NotEqual(runId, fixture.Runner.CurrentRunId);
        Assert.Equal(100m, Assert.Single(restarted[0].Accounts).Cash);
        Assert.All(fixture.Factory.CreatedAgents.Last().Calls, call => Assert.Null(call.News));
    }

    [Fact]
    public async Task ExistingSnapshotsAndInitialPositionInput_AreNotMutatedByLaterOperations()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var positions = new Dictionary<Guid, decimal> { [first] = 2m };
        var ticks = await fixture.StartAsync([first],
            [new AgentSpec(AgentType.MarketMaker, 500m) { InitialPositions = positions }]);
        var original = ticks[0];
        positions[first] = 100m;
        var second = fixture.CreateAsset("Second", 200m);

        fixture.Runner.AddMarket(second);
        fixture.Runner.SubmitMany(first, [Limit("MarketMaker", OrderSide.Buy, 90m)]);

        var initialAccount = Assert.Single(original.Accounts);
        Assert.Single(initialAccount.Positions);
        Assert.Equal(2m, initialAccount.Positions[first].Quantity);
        Assert.Equal(0m, initialAccount.ReservedCash);
        var current = Assert.Single(fixture.Runner.GetAgentAccounts());
        Assert.Equal(2m, current.Positions[first].Quantity);
        Assert.Equal(90m, current.ReservedCash);
    }

    [Fact]
    public async Task LegacyStart_CanReuseCatalogAssetWithoutCreatingDuplicate()
    {
        await using var fixture = new Fixture();
        var assetId = fixture.CreateAsset("Catalog Asset", 100m);
        var completion = new TaskCompletionSource<SimulationTickResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runner.OnTick += tick => completion.TrySetResult(tick);
        var config = new SimulationConfig("Ignored", "Ignored", AssetType.Stock, 200m,
            TimeSpan.FromHours(1), [new(AgentType.MarketMaker, 500m)]) { AssetId = assetId };

        await fixture.Runner.StartAsync(config);
        var tick = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Runner.StartAsync(config with { AssetId = null });

        Assert.Equal(assetId, tick.AssetId);
        Assert.Equal(100m, tick.Snapshot.LastPrice);
        Assert.Equal("Catalog Asset", fixture.Runner.CurrentAssetName);
        Assert.Single(fixture.Catalog.GetAll());
        Assert.Equal(1, fixture.Factory.CreateCalls);
    }

    [Theory]
    [InlineData("zeroInterval")]
    [InlineData("negativeInterval")]
    [InlineData("duplicateAsset")]
    [InlineData("emptyAsset")]
    [InlineData("unknownAsset")]
    [InlineData("negativeCash")]
    [InlineData("negativePosition")]
    [InlineData("unknownPosition")]
    public async Task InvalidConfig_DoesNotStartSessionOrHistory(string scenario)
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        IReadOnlyList<Guid> assetIds = [asset];
        var spec = new AgentSpec(AgentType.MarketMaker, 500m);
        var interval = TimeSpan.FromHours(1);
        switch (scenario)
        {
            case "zeroInterval": interval = TimeSpan.Zero; break;
            case "negativeInterval": interval = TimeSpan.FromSeconds(-1); break;
            case "duplicateAsset": assetIds = [asset, asset]; break;
            case "emptyAsset": assetIds = [Guid.Empty]; break;
            case "unknownAsset": assetIds = [Guid.NewGuid()]; break;
            case "negativeCash": spec = spec with { InitialCash = -1m }; break;
            case "negativePosition": spec = spec with { InitialPosition = -1m }; break;
            case "unknownPosition":
                spec = spec with { InitialPositions = new Dictionary<Guid, decimal> { [Guid.NewGuid()] = 1m } };
                break;
        }

        var exception = await Record.ExceptionAsync(() =>
            fixture.Runner.StartSessionAsync(new(assetIds, interval, [spec])));

        Assert.True(exception is ArgumentException or KeyNotFoundException);
        Assert.False(fixture.Runner.IsRunning);
        Assert.Equal(Guid.Empty, fixture.Runner.CurrentSessionId);
        Assert.Empty(fixture.History.Runs);
        Assert.Empty(fixture.Runner.GetCurrentMarkets());
    }

    [Fact]
    public async Task AlreadyCancelledToken_DoesNotStartSession()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.Runner.StartSessionAsync(new([asset], TimeSpan.FromSeconds(1), []),
                new CancellationToken(canceled: true)));
        Assert.False(fixture.Runner.IsRunning);
        Assert.Empty(fixture.History.Runs);
    }

    private static Order Limit(string name, OrderSide side, decimal price) =>
        new(Guid.NewGuid(), name, side, OrderType.Limit, price, 1m, DateTimeOffset.UtcNow);

    private static AgentDecision Decision(ProbeAgent agent, params Order[] orders) =>
        new(agent.State.AgentName, TradeAction.Hold, "Test decision.", orders);

    private sealed class Fixture : IAsyncDisposable
    {
        public ServiceProvider Services { get; }
        public RecordingAgentFactory Factory { get; }
        public RecordingHistoryStore History { get; } = new();
        public IAssetCatalog Catalog { get; }
        public IMultiAssetSimulationRunner Runner { get; }

        public Fixture(Func<ProbeAgent, AgentMarketContext, NewsSignal?, AgentDecision>? decide = null)
        {
            Factory = new RecordingAgentFactory(decide);
            Services = new ServiceCollection()
                .AddSingleton<IAgentFactory>(Factory)
                .AddSingleton<IMarketHistoryStore>(History)
                .AddABStockApplication()
                .BuildServiceProvider();
            Catalog = Services.GetRequiredService<IAssetCatalog>();
            Runner = Services.GetRequiredService<IMultiAssetSimulationRunner>();
        }

        public Guid CreateAsset(string name, decimal price) =>
            Catalog.Create(new(new AssetProfile(name, AssetType.Stock, "Test asset.", [], 1m), price)).AssetId;

        public async Task<IReadOnlyList<SimulationTickResult>> StartAsync(
            IReadOnlyList<Guid> assetIds, IReadOnlyList<AgentSpec> specs, TimeSpan? interval = null)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<SimulationTickResult>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void HandleTick(SimulationTickResult tick)
            {
                if (assetIds.Count > 0 && tick.AssetId == assetIds[^1] && tick.Tick == 1)
                {
                    completion.TrySetResult(Runner.GetCurrentMarkets());
                }
            }

            Runner.OnMarketTick += HandleTick;
            try
            {
                await Runner.StartSessionAsync(new(assetIds, interval ?? TimeSpan.FromHours(1), specs));
                return assetIds.Count == 0 ? [] : await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                Runner.OnMarketTick -= HandleTick;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Runner.StopAsync();
            await Services.DisposeAsync();
        }
    }

    private sealed class RecordingAgentFactory(
        Func<ProbeAgent, AgentMarketContext, NewsSignal?, AgentDecision>? decide) : IAgentFactory
    {
        public int CreateCalls { get; private set; }
        public List<ProbeAgent> CreatedAgents { get; } = [];

        public IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs)
        {
            CreateCalls++;
            var agents = specs.Select(spec => new ProbeAgent(spec, decide)).ToArray();
            CreatedAgents.AddRange(agents);
            return agents;
        }
    }

    private sealed class ProbeAgent(AgentSpec spec,
        Func<ProbeAgent, AgentMarketContext, NewsSignal?, AgentDecision>? decide)
        : AgentBase(spec.Type.ToString(), spec.Type, spec.InitialCash, spec.InitialPosition)
    {
        public ConcurrentQueue<DecisionCall> Calls { get; } = new();

        public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal) =>
            HoldDecision("Legacy test decision.");

        public override AgentDecision Decide(AgentMarketContext context, NewsSignal? newsSignal)
        {
            Calls.Enqueue(new(context, newsSignal, State.Cash, State.Position,
                State.AvailableCash, State.AvailablePosition));
            return decide?.Invoke(this, context, newsSignal) ?? HoldDecision("Test hold.");
        }
    }

    private sealed record DecisionCall(AgentMarketContext Context, NewsSignal? News, decimal Cash,
        decimal Position, decimal AvailableCash, decimal AvailablePosition);

    private sealed class RecordingHistoryStore : IMarketHistoryStore
    {
        public ConcurrentQueue<(Guid RunId, SimulationConfig Config)> Runs { get; } = new();
        public ConcurrentQueue<(Guid RunId, SimulationTickResult Tick)> Ticks { get; } = new();

        public Guid StartRun(SimulationConfig config, DateTimeOffset startedAt)
        {
            var runId = Guid.NewGuid();
            Runs.Enqueue((runId, config));
            return runId;
        }

        public void SaveTick(Guid runId, SimulationTickResult tickResult, DateTimeOffset capturedAt) =>
            Ticks.Enqueue((runId, tickResult));
    }
}
