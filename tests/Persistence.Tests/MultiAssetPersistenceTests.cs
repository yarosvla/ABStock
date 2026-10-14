using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation;
using ABStock.Persistence.Entities;
using ABStock.Persistence.Extensions;
using ABStock.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Persistence.Tests;

public sealed class MultiAssetPersistenceTests
{
    [Theory]
    [InlineData(ProfileSource.Ai)]
    [InlineData(ProfileSource.Fallback)]
    public async Task Catalog_PreservesIdsPricesAndFullProfileAcrossProviders(ProfileSource source)
    {
        await using var fixture = new Fixture();
        var catalog = fixture.Services.GetRequiredService<IAssetCatalog>();
        var profile = new AssetProfile("  First  ", AssetType.Crypto, "  AI profile  ",
            [
                new("Спрос", true, 0.75m, [0.1f, 0.2f])
                {
                    NameEn = "Demand"
                },
                new("Риск", false, 0.5m, [-0.3f])
                {
                    NameEn = "Risk"
                }
            ], 1.25m)
        {
            Source = source
        };
        var asset = catalog.Create(new(profile, 123.456m));

        var restored = fixture.NewProvider().GetRequiredService<IAssetCatalog>().Get(asset.AssetId);

        Assert.NotNull(restored);
        Assert.Equal(asset.AssetId, restored.AssetId);
        Assert.Equal(asset.CreatedAt, restored.CreatedAt);
        Assert.Equal(123.456m, restored.StartPrice);
        Assert.Equal("First", restored.Name);
        Assert.Equal("AI profile", restored.Description);
        Assert.Equal(AssetType.Crypto, restored.AssetType);
        Assert.Equal(source, restored.Profile.Source);
        Assert.Equal(1.25m, restored.Profile.NewsSensitivity);
        Assert.Equal(2, restored.Profile.Factors.Count);
        Assert.Equal(0.75m, restored.Profile.Factors[0].Importance);
        Assert.Equal(new[] { 0.1f, 0.2f }, restored.Profile.Factors[0].Embedding);
        Assert.Equal("Риск", Assert.Single(restored.Profile.NegativeFactors).Name);
        Assert.Equal("Risk", Assert.Single(restored.Profile.NegativeFactors).NameEn);
        Assert.Equal("Спрос", restored.Profile.Factors[0].Name);
        Assert.Equal("Demand", restored.Profile.Factors[0].NameEn);
    }

    [Fact]
    public async Task Catalog_DoesNotExposeStoredProfileToMutation()
    {
        await using var fixture = new Fixture();
        var catalog = fixture.Services.GetRequiredService<IAssetCatalog>();
        var vector = new[] { 0.1f, 0.2f };
        var factors = new List<AssetFactor> { new("Demand", true, 1m, vector) };
        var asset = catalog.Create(new(new AssetProfile("First", AssetType.Stock, "Description", factors, 1m), 100m));
        factors.Clear();
        vector[0] = 10f;
        asset.Profile.Factors[0].Embedding[0] = 20f;
        catalog.Get(asset.AssetId)!.Profile.Factors[0].Embedding[0] = 30f;

        var restored = catalog.Get(asset.AssetId)!;

        Assert.Equal(0.1f, Assert.Single(restored.Profile.Factors).Embedding[0]);
        Assert.Equal(0.1f, Assert.Single(catalog.GetAll()).Profile.Factors[0].Embedding[0]);
    }

    [Fact]
    public async Task Catalog_DistinguishesAssetsWithSameName()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("Same", 100m);
        var second = fixture.CreateAsset("Same", 200m);
        Assert.NotEqual(first.AssetId, second.AssetId);
        Assert.Equal(2, fixture.Services.GetRequiredService<IAssetCatalog>().GetAll().Count);
        Assert.Equal(2, fixture.Services.GetRequiredService<ISimulationHistoryReader>().GetOverview().DistinctAssetCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Catalog_RejectsInvalidPriceWithoutWritingAsset(int price)
    {
        await using var fixture = new Fixture();
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.CreateAsset("Invalid", price));
        Assert.Empty(fixture.Services.GetRequiredService<IAssetCatalog>().GetAll());
    }

    [Fact]
    public async Task HistoryReaders_WorkBeforeFirstAssetOrSession()
    {
        await using var fixture = new Fixture();
        var history = fixture.Services.GetRequiredService<ISimulationHistoryReader>();
        var sessions = fixture.Services.GetRequiredService<ITradingSessionHistoryReader>();
        Assert.Equal(0, history.GetOverview().RunCount);
        Assert.Null(history.GetRun(Guid.NewGuid()));
        Assert.Null(sessions.GetSession(Guid.NewGuid()));
        Assert.Empty(sessions.GetRecentSessions());
        Assert.Empty(sessions.GetAssetRuns(Guid.NewGuid()));
        Assert.Empty(fixture.Services.GetRequiredService<IMarketCandleReader>()
            .GetCandles(Guid.NewGuid(), TimeSpan.FromSeconds(10), 10));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServiceRegistration_UsesPersistentCatalogInEitherOrder(bool persistenceFirst)
    {
        await using var fixture = new Fixture();
        var provider = fixture.NewProvider(persistenceFirst);
        var catalog = provider.GetRequiredService<IAssetCatalog>();
        var asset = catalog.Create(new(new AssetProfile("First", AssetType.Stock, "Profile", [], 1m), 100m));
        Assert.NotNull(fixture.Services.GetRequiredService<IAssetCatalog>().Get(asset.AssetId));
        Assert.Same(provider.GetRequiredService<ISimulationRunner>(),
            provider.GetRequiredService<IMultiAssetSimulationRunner>());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(300)]
    [InlineData(900)]
    [InlineData(3600)]
    public async Task Candles_AreIsolatedByMarketInSameSession(int intervalSeconds)
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 1_000m);
        var sessionId = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        var startedAt = DateTimeOffset.UnixEpoch;
        store.StartSession(sessionId, TimeSpan.FromMilliseconds(200), startedAt);
        var firstRun = store.StartRun(Config(first, sessionId), startedAt);
        var secondRun = store.StartRun(Config(second, sessionId), startedAt);
        store.SaveTick(firstRun, Tick(firstRun, sessionId, first.AssetId, 1, 100m), startedAt);
        store.SaveTick(secondRun, Tick(secondRun, sessionId, second.AssetId, 1, 1_000m), startedAt);
        store.SaveTick(firstRun, Tick(firstRun, sessionId, first.AssetId, 2, 105m,
            [Trade(105m, 2m, startedAt.AddSeconds(5))]), startedAt.AddSeconds(5));
        store.SaveTick(secondRun, Tick(secondRun, sessionId, second.AssetId, 2, 995m,
            [Trade(995m, 3m, startedAt.AddSeconds(5))]), startedAt.AddSeconds(5));

        var restored = fixture.NewProvider();
        var reader = restored.GetRequiredService<IMarketCandleReader>();
        var firstCandle = Assert.Single(reader.GetCandles(firstRun, TimeSpan.FromSeconds(intervalSeconds), 10));
        var secondCandle = Assert.Single(reader.GetCandles(secondRun, TimeSpan.FromSeconds(intervalSeconds), 10));

        Assert.Equal(100m, firstCandle.Open);
        Assert.Equal(105m, firstCandle.Close);
        Assert.Equal(105m, firstCandle.High);
        Assert.Equal(100m, firstCandle.Low);
        Assert.Equal(2m, firstCandle.Volume);
        Assert.Equal(1_000m, secondCandle.Open);
        Assert.Equal(995m, secondCandle.Close);
        Assert.Equal(3m, secondCandle.Volume);
        var session = restored.GetRequiredService<ITradingSessionHistoryReader>().GetSession(sessionId)!;
        Assert.Equal(2, session.Markets.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(200), session.TickInterval);
        Assert.All(session.Markets, market =>
        {
            Assert.Equal(sessionId, market.SessionId);
            Assert.Equal(2, market.TickCount);
            Assert.Equal(1, market.TradeCount);
        });
        var overview = restored.GetRequiredService<ISimulationHistoryReader>().GetOverview();
        Assert.Equal(1, overview.RunCount);
        Assert.Equal(2, overview.DistinctAssetCount);
        Assert.Equal(2, overview.TotalTradeCount);
        Assert.Equal(4, overview.TotalTickCount);
    }

    [Fact]
    public async Task StartRun_IsIdempotentWithinSessionAndUsesCatalogMetadata()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("Catalog name", 100m);
        var sessionId = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        store.StartSession(sessionId, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        var config = Config(asset, sessionId) with { AssetName = "Wrong", StartPrice = 999m };
        var runId = store.StartRun(config, DateTimeOffset.UnixEpoch);

        Assert.Equal(runId, store.StartRun(config, DateTimeOffset.UnixEpoch.AddMinutes(1)));
        var run = fixture.Services.GetRequiredService<ISimulationHistoryReader>().GetRun(runId)!;
        Assert.Equal("Catalog name", run.AssetName);
        Assert.Equal(100m, run.LastPrice);
        Assert.Equal(asset.AssetId, run.AssetId);
        Assert.Equal(sessionId, run.SessionId);
        using var db = fixture.CreateContext();
        Assert.Equal(1, db.SimulationRuns.Count());
        Assert.Equal(1, db.SessionMarkets.Count());
    }

    [Fact]
    public async Task SameAsset_CanHaveIndependentRunsInDifferentSessions()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        store.StartSession(firstSession, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        var firstRun = store.StartRun(Config(asset, firstSession), DateTimeOffset.UnixEpoch);
        store.EndSession(firstSession, DateTimeOffset.UnixEpoch.AddMinutes(1));
        store.StartSession(secondSession, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch.AddMinutes(2));
        var secondRun = store.StartRun(Config(asset, secondSession), DateTimeOffset.UnixEpoch.AddMinutes(2));

        var runs = fixture.Services.GetRequiredService<ITradingSessionHistoryReader>().GetAssetRuns(asset.AssetId);

        Assert.Equal(2, runs.Count);
        Assert.Equal(secondRun, runs[0].RunId);
        Assert.Equal(secondSession, runs[0].SessionId);
        Assert.Equal(firstRun, runs[1].RunId);
        Assert.All(runs, run => Assert.Equal(asset.AssetId, run.AssetId));
        Assert.Single(fixture.Services.GetRequiredService<ITradingSessionHistoryReader>().GetAssetRuns(asset.AssetId, 1));
    }

    [Theory]
    [InlineData("asset")]
    [InlineData("session")]
    [InlineData("run")]
    public async Task SaveTick_RejectsWrongMarketMetadata(string field)
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var sessionId = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        store.StartSession(sessionId, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        var runId = store.StartRun(Config(asset, sessionId), DateTimeOffset.UnixEpoch);
        var tick = Tick(runId, sessionId, asset.AssetId, 1, 100m);
        tick = field switch
        {
            "asset" => tick with { AssetId = Guid.NewGuid() },
            "session" => tick with { SessionId = Guid.NewGuid() },
            _ => tick with { RunId = Guid.NewGuid() }
        };

        Assert.Throws<ArgumentException>(() => store.SaveTick(runId, tick, DateTimeOffset.UnixEpoch));
        using var db = fixture.CreateContext();
        Assert.Empty(db.MarketTicks);
        Assert.Empty(db.Trades);
    }

    [Fact]
    public async Task UnknownReferences_DoNotCreateOrphanMarketRuns()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var sessionId = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        store.StartSession(sessionId, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        Assert.Throws<KeyNotFoundException>(() =>
            store.StartRun(Config(asset, Guid.NewGuid()), DateTimeOffset.UnixEpoch));
        Assert.Throws<KeyNotFoundException>(() =>
            store.StartRun(Config(asset, sessionId) with { AssetId = Guid.NewGuid() }, DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() =>
            store.StartRun(Config(asset, sessionId) with { SessionId = null }, DateTimeOffset.UnixEpoch));
        using var db = fixture.CreateContext();
        Assert.Empty(db.SimulationRuns);
        Assert.Empty(db.SessionMarkets);
    }

    [Fact]
    public async Task CompletedSession_CannotReceiveNewMarketsOrBeRestartedWithSameId()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var sessionId = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        store.StartSession(sessionId, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        store.EndSession(sessionId, DateTimeOffset.UnixEpoch.AddSeconds(10));
        store.EndSession(sessionId, DateTimeOffset.UnixEpoch.AddSeconds(20));

        Assert.Throws<InvalidOperationException>(() =>
            store.StartRun(Config(asset, sessionId), DateTimeOffset.UnixEpoch.AddSeconds(30)));
        Assert.Throws<InvalidOperationException>(() =>
            store.StartSession(sessionId, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch));
        var session = fixture.Services.GetRequiredService<ITradingSessionHistoryReader>().GetSession(sessionId)!;
        Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(10), session.EndedAt);
        Assert.Empty(session.Markets);
    }

    [Fact]
    public async Task DatabaseConstraints_PreventDeletingAssetWithRecordedMarket()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var sessionId = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        store.StartSession(sessionId, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        store.StartRun(Config(asset, sessionId), DateTimeOffset.UnixEpoch);
        using var db = fixture.CreateContext();
        db.Assets.Remove(db.Assets.Single());
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.NotNull(fixture.Services.GetRequiredService<IAssetCatalog>().Get(asset.AssetId));
    }

    [Fact]
    public async Task SubmissionTrades_AreSavedEvenWhenRecentHistoryIsShorter()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var sessionId = Guid.NewGuid();
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        var time = DateTimeOffset.UnixEpoch;
        store.StartSession(sessionId, TimeSpan.FromSeconds(1), time);
        var runId = store.StartRun(Config(asset, sessionId), time);
        var trades = Enumerable.Range(0, 80).Select(_ => Trade(100m, 1m, time)).ToArray();
        var tick = Tick(runId, sessionId, asset.AssetId, 1, 100m, trades.TakeLast(3).ToArray());
        tick = tick with
        {
            Snapshot = tick.Snapshot with { Volume = 80m },
            Submission = new SubmitResult(tick.Snapshot with { Volume = 80m }, trades, [], [])
        };
        store.SaveTick(runId, tick, time);
        store.SaveTick(runId, tick with { Tick = 2 }, time.AddSeconds(1));

        using var db = fixture.CreateContext();
        Assert.Equal(80, db.Trades.Count());
        Assert.Equal(80m, Assert.Single(fixture.Services.GetRequiredService<IMarketCandleReader>()
            .GetCandles(runId, TimeSpan.FromSeconds(10), 10)).Volume);
    }

    [Fact]
    public async Task Runner_PersistsSessionDynamicMarketAndHistoryAcrossProviderRestart()
    {
        await using var fixture = new Fixture();
        var first = fixture.CreateAsset("First", 100m);
        var second = fixture.CreateAsset("Second", 200m);
        var runner = fixture.Services.GetRequiredService<IMultiAssetSimulationRunner>();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.OnMarketTick += tick =>
        {
            if (tick.AssetId == second.AssetId)
            {
                completion.TrySetResult();
            }
        };
        await runner.StartSessionAsync(new([first.AssetId, second.AssetId], TimeSpan.FromHours(1), []));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var sessionId = runner.CurrentSessionId;
        var firstRun = runner.GetRunId(first.AssetId);
        var secondRun = runner.GetRunId(second.AssetId);
        var seller = runner.AddSessionAgent(new AgentSpec(AgentType.MarketMaker, 0m)
        {
            InitialPositions = new Dictionary<Guid, decimal> { [first.AssetId] = 2m }
        });
        var buyer = runner.AddSessionAgent(new AgentSpec(AgentType.TrendFollowing, 500m));
        runner.SubmitMany(first.AssetId,
            [new(Guid.NewGuid(), seller.AgentName, OrderSide.Sell, OrderType.Limit, 100m, 1m, DateTimeOffset.UtcNow)]);
        var submitted = runner.SubmitMany(first.AssetId,
            [new(Guid.NewGuid(), buyer.AgentName, OrderSide.Buy, OrderType.Market, null, 1m, DateTimeOffset.UtcNow)]);
        Assert.Single(submitted.Result.Trades);
        var third = fixture.CreateAsset("Later", 300m);
        runner.AddMarket(third.AssetId);
        Assert.Equal(firstRun, runner.GetRunId(first.AssetId));
        await runner.StopAsync();

        var restarted = fixture.NewProvider();
        Assert.False(restarted.GetRequiredService<IMultiAssetSimulationRunner>().IsRunning);
        Assert.Equal(3, restarted.GetRequiredService<IAssetCatalog>().GetAll().Count);
        var session = restarted.GetRequiredService<ITradingSessionHistoryReader>().GetSession(sessionId)!;
        Assert.NotNull(session.EndedAt);
        Assert.Equal(3, session.Markets.Count);
        var firstHistory = session.Markets.Single(market => market.AssetId == first.AssetId);
        var secondHistory = session.Markets.Single(market => market.AssetId == second.AssetId);
        Assert.Equal(firstRun, firstHistory.RunId);
        Assert.Equal(secondRun, secondHistory.RunId);
        Assert.Equal(1, firstHistory.TradeCount);
        Assert.Equal(0, secondHistory.TradeCount);
        Assert.Equal(100m, firstHistory.LastPrice);
        Assert.Equal(200m, secondHistory.LastPrice);
        Assert.Equal(3, session.Markets.Select(market => market.RunId).Distinct().Count());
        var reader = restarted.GetRequiredService<IMarketCandleReader>();
        Assert.Equal(1m, reader.GetCandles(firstRun, TimeSpan.FromSeconds(10), 10).Sum(candle => candle.Volume));
        Assert.Equal(0m, reader.GetCandles(secondRun, TimeSpan.FromSeconds(10), 10).Sum(candle => candle.Volume));
    }

    [Fact]
    public async Task Runner_CanPersistEmptySessionAndAddFirstMarketLater()
    {
        await using var fixture = new Fixture();
        var runner = fixture.Services.GetRequiredService<IMultiAssetSimulationRunner>();
        await runner.StartSessionAsync(new([], TimeSpan.FromHours(1), [new(AgentType.NewsDriven, 500m, 3m)]));
        var sessionId = runner.CurrentSessionId;
        var reader = fixture.Services.GetRequiredService<ITradingSessionHistoryReader>();
        Assert.Empty(reader.GetSession(sessionId)!.Markets);
        var asset = fixture.CreateAsset("Later", 100m);
        var tick = runner.AddMarket(asset.AssetId);
        var account = Assert.Single(tick.Accounts);
        Assert.Equal(3m, account.Positions[asset.AssetId].Quantity);
        Assert.Equal(800m, account.InitialPortfolioValue);
        Assert.Equal(500m, account.Cash);
        Assert.Equal(asset.AssetId, Assert.Single(reader.GetSession(sessionId)!.Markets).AssetId);
        await runner.StopAsync();
        Assert.NotNull(reader.GetSession(sessionId)!.EndedAt);
    }

    [Fact]
    public async Task LegacyDatabaseUpgrade_PreservesRunsTicksTradesAndIsRepeatable()
    {
        await using var fixture = new Fixture();
        var runId = Guid.NewGuid();
        var trade = Trade(105m, 2m, DateTimeOffset.UnixEpoch.AddSeconds(5));
        using (var legacy = new LegacyDbContext(new DbContextOptionsBuilder<LegacyDbContext>()
            .UseSqlite(fixture.ConnectionString).Options))
        {
            legacy.Database.EnsureCreated();
            var run = new SimulationRunEntity
            {
                Id = runId, AssetName = "Legacy", AssetDescription = "Old asset", AssetType = AssetType.Stock,
                StartPrice = 100m, StartedAt = DateTimeOffset.UnixEpoch,
                MarketTicks =
                [
                    new() { Tick = 1, CapturedAt = DateTimeOffset.UnixEpoch, LastPrice = 100m },
                    new() { Tick = 2, CapturedAt = DateTimeOffset.UnixEpoch.AddSeconds(5), LastPrice = 105m, TotalVolume = 2m }
                ],
                Trades =
                [
                    new() { Id = trade.Id, BuyOrderId = trade.BuyOrderId, SellOrderId = trade.SellOrderId,
                        BuyerAgentName = trade.BuyerAgentName, SellerAgentName = trade.SellerAgentName,
                        Price = trade.Price, Quantity = trade.Quantity, ExecutedAt = trade.ExecutedAt }
                ]
            };
            legacy.Add(run);
            legacy.SaveChanges();
        }

        fixture.CreateAsset("New", 200m);
        var newProvider = fixture.NewProvider();
        var summary = newProvider.GetRequiredService<ISimulationHistoryReader>().GetRun(runId)!;
        Assert.Equal("Legacy", summary.AssetName);
        Assert.Equal(2, summary.TickCount);
        Assert.Equal(1, summary.TradeCount);
        Assert.Null(summary.SessionId);
        Assert.Null(summary.AssetId);
        var candle = Assert.Single(newProvider.GetRequiredService<IMarketCandleReader>()
            .GetCandles(runId, TimeSpan.FromSeconds(10), 10));
        Assert.Equal(100m, candle.Open);
        Assert.Equal(105m, candle.Close);
        Assert.Equal(2m, candle.Volume);
        using var db = fixture.CreateContext();
        Assert.Equal(1, db.SimulationRuns.Count());
        Assert.Equal(2, db.MarketTicks.Count());
        Assert.Equal(trade.Id, Assert.Single(db.Trades).Id);
        Assert.Empty(db.SessionMarkets);
        Assert.Empty(db.TradingSessions);
        Assert.Equal(2L, fixture.ExecuteScalar("SELECT COUNT(*) FROM StorageSchemaVersions;"));
    }

    [Fact]
    public async Task Initializer_RejectsUnknownDatabaseWithoutChangingItsTables()
    {
        await using var fixture = new Fixture();
        fixture.ExecuteSql("CREATE TABLE Unrelated (Id INTEGER PRIMARY KEY); INSERT INTO Unrelated VALUES (42);");
        Assert.Throws<InvalidOperationException>(() =>
            fixture.Services.GetRequiredService<IAssetCatalog>().GetAll());
        Assert.Equal(42L, fixture.ExecuteScalar("SELECT Id FROM Unrelated;"));
        Assert.Equal(1L, fixture.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';"));
    }

    [Fact]
    public async Task Initializer_RejectsNewerSchemaWithoutRemovingStoredAssets()
    {
        await using var fixture = new Fixture();
        fixture.CreateAsset("First", 100m);
        fixture.ExecuteSql("INSERT INTO StorageSchemaVersions (Version, AppliedAt) VALUES (99, '2026-01-01');");
        var newerProvider = fixture.NewProvider();
        Assert.Throws<InvalidOperationException>(() =>
            newerProvider.GetRequiredService<IAssetCatalog>().GetAll());
        Assert.Equal(1L, fixture.ExecuteScalar("SELECT COUNT(*) FROM Assets;"));
        Assert.Equal(99L, fixture.ExecuteScalar("SELECT MAX(Version) FROM StorageSchemaVersions;"));
    }

    private static SimulationConfig Config(Asset asset, Guid sessionId) =>
        new(asset.Name, asset.Description, asset.AssetType, asset.StartPrice, TimeSpan.FromSeconds(1), [])
        {
            SessionId = sessionId,
            AssetId = asset.AssetId
        };

    [Fact]
    public async Task CatalogPersistsFormParametersUpdatesAndArchiveAcrossRestarts()
    {
        await using var fixture = new Fixture();
        var catalog = fixture.Services.GetRequiredService<IAssetCatalog>();
        var profile = new AssetProfile("First", AssetType.Stock, "Description", [new("Demand", true, 1m, [0.1f])], 1m);
        var created = catalog.Create(new(profile)
        {
            Ticker = " glen ", Industry = "Energy", IncludeGovernmentSupport = true, GrowthPotential = 50
        });
        var restoredCatalog = fixture.NewProvider().GetRequiredService<IAssetCatalog>();
        var restored = restoredCatalog.Get(created.AssetId)!;
        Assert.Equal("GLEN", restored.Ticker);
        Assert.Equal("Energy", restored.Industry);
        Assert.True(restored.IncludeGovernmentSupport);
        Assert.Equal(50, restored.GrowthPotential);
        Assert.Equal(131m, restored.StartPrice);
        var updated = restoredCatalog.Update(created.AssetId,
            new(profile with { Name = "Renamed" }, "NEW", "Technology", false, null));
        var changed = fixture.NewProvider().GetRequiredService<IAssetCatalog>().Get(created.AssetId)!;
        Assert.Equal("Renamed", changed.Name);
        Assert.Equal("NEW", changed.Ticker);
        Assert.Equal("Technology", changed.Industry);
        Assert.False(changed.IncludeGovernmentSupport);
        Assert.Null(changed.GrowthPotential);
        Assert.Equal(created.StartPrice, changed.StartPrice);
        Assert.Equal(created.CreatedAt, changed.CreatedAt);
        Assert.Equal(updated.UpdatedAt, changed.UpdatedAt);
        var archived = catalog.Archive(created.AssetId);
        Assert.Equal(archived.ArchivedAt, restoredCatalog.Archive(created.AssetId).ArchivedAt);
        var lastCatalog = fixture.NewProvider().GetRequiredService<IAssetCatalog>();
        Assert.Empty(lastCatalog.GetAll());
        Assert.True(Assert.Single(lastCatalog.GetAll(includeArchived: true)).IsArchived);
        Assert.Equal(archived.ArchivedAt, lastCatalog.Get(created.AssetId)!.ArchivedAt);
        Assert.Throws<InvalidOperationException>(() => lastCatalog.Update(created.AssetId,
            new(profile, "NEW", "", false, null)));
    }

    [Fact]
    public async Task DuplicateTickersAreRejectedForCreateAndUpdateIncludingArchivedAssets()
    {
        await using var fixture = new Fixture();
        var catalog = fixture.Services.GetRequiredService<IAssetCatalog>();
        var profile = new AssetProfile("First", AssetType.Stock, "Description", [], 1m);
        var first = catalog.Create(new(profile) { Ticker = "ONE" });
        var second = catalog.Create(new(profile with { Name = "Second" }) { Ticker = "TWO" });
        var otherCatalog = fixture.NewProvider().GetRequiredService<IAssetCatalog>();
        Assert.Throws<ArgumentException>(() => otherCatalog.Create(new(profile) { Ticker = " one " }));
        Assert.Throws<ArgumentException>(() => otherCatalog.Update(second.AssetId, new(profile, "ONE", "", false, null)));
        Assert.Equal("TWO", catalog.Get(second.AssetId)!.Ticker);
        catalog.Archive(first.AssetId);
        Assert.Throws<ArgumentException>(() => otherCatalog.Create(new(profile) { Ticker = "one" }));
        Assert.Equal(2, otherCatalog.GetAll(includeArchived: true).Count);
        Assert.Throws<KeyNotFoundException>(() => catalog.Archive(Guid.NewGuid()));
    }

    [Fact]
    public async Task VersionOneUpgradePreservesCatalogLinksAndHistoryAndAssignsStableTickers()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("Old asset", 100m);
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        var sessionId = Guid.NewGuid();
        store.StartSession(sessionId, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        var runId = store.StartRun(Config(asset, sessionId), DateTimeOffset.UnixEpoch);
        store.SaveTick(runId, Tick(runId, sessionId, asset.AssetId, 1, 105m,
            [Trade(105m, 2m, DateTimeOffset.UnixEpoch)]), DateTimeOffset.UnixEpoch);
        fixture.ExecuteSql("""
            DROP INDEX IX_Assets_Ticker;
            ALTER TABLE Assets DROP COLUMN Ticker;
            ALTER TABLE Assets DROP COLUMN Industry;
            ALTER TABLE Assets DROP COLUMN IncludeGovernmentSupport;
            ALTER TABLE Assets DROP COLUMN GrowthPotential;
            ALTER TABLE Assets DROP COLUMN UpdatedAt;
            ALTER TABLE Assets DROP COLUMN ArchivedAt;
            ALTER TABLE MarketTicks DROP COLUMN TotalTradeCount;
            DELETE FROM StorageSchemaVersions WHERE Version = 2;
            """);

        var upgraded = fixture.NewProvider();
        var restored = upgraded.GetRequiredService<IAssetCatalog>().Get(asset.AssetId)!;

        Assert.Equal(asset.AssetId, restored.AssetId);
        Assert.Equal(asset.Profile.Name, restored.Profile.Name);
        Assert.Equal(asset.StartPrice, restored.StartPrice);
        Assert.Equal(asset.CreatedAt, restored.CreatedAt);
        Assert.NotEmpty(restored.Ticker);
        Assert.Equal("", restored.Industry);
        Assert.False(restored.IncludeGovernmentSupport);
        Assert.Null(restored.GrowthPotential);
        Assert.Null(restored.ArchivedAt);
        Assert.Equal(restored.Ticker, fixture.NewProvider().GetRequiredService<IAssetCatalog>().Get(asset.AssetId)!.Ticker);
        var session = upgraded.GetRequiredService<ITradingSessionHistoryReader>().GetSession(sessionId)!;
        Assert.Equal(runId, Assert.Single(session.Markets).RunId);
        Assert.Equal(1, session.Markets[0].TradeCount);
        Assert.Equal(2m, Assert.Single(upgraded.GetRequiredService<IMarketCandleReader>()
            .GetCandles(runId, TimeSpan.FromSeconds(10), 10)).Volume);
        Assert.Equal(2L, fixture.ExecuteScalar("SELECT MAX(Version) FROM StorageSchemaVersions;"));
        using var db = fixture.CreateContext();
        Assert.Null(Assert.Single(db.MarketTicks).TotalTradeCount);
    }

    [Fact]
    public async Task ArchivedAssetsKeepHistoryAndCannotStartNewRuns()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var store = fixture.Services.GetRequiredService<IMarketHistoryStore>();
        var session = Guid.NewGuid();
        store.StartSession(session, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        var run = store.StartRun(Config(asset, session), DateTimeOffset.UnixEpoch);
        fixture.Services.GetRequiredService<IAssetCatalog>().Archive(asset.AssetId);
        Assert.Equal(run, store.StartRun(Config(asset, session), DateTimeOffset.UnixEpoch));
        store.SaveTick(run, Tick(run, session, asset.AssetId, 1, 100m), DateTimeOffset.UnixEpoch);
        var anotherSession = Guid.NewGuid();
        store.StartSession(anotherSession, TimeSpan.FromSeconds(1), DateTimeOffset.UnixEpoch);
        Assert.Throws<InvalidOperationException>(() => store.StartRun(Config(asset, anotherSession), DateTimeOffset.UnixEpoch));
        Assert.Single(fixture.Services.GetRequiredService<ITradingSessionHistoryReader>().GetAssetRuns(asset.AssetId));
        Assert.Single(fixture.Services.GetRequiredService<IMarketCandleReader>().GetCandles(run, TimeSpan.FromSeconds(10), 10));
        using var db = fixture.CreateContext();
        Assert.Single(db.SimulationRuns);
    }

    [Fact]
    public async Task RunnerPersistsExactTradeCountBeyondRecentTradeWindow()
    {
        await using var fixture = new Fixture();
        var asset = fixture.CreateAsset("First", 100m);
        var runner = fixture.Services.GetRequiredService<IMultiAssetSimulationRunner>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.OnMarketTick += _ => ready.TrySetResult();
        await runner.StartSessionAsync(new([asset.AssetId], TimeSpan.FromHours(1), []));
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var runId = runner.GetRunId(asset.AssetId);
        var seller = runner.AddSessionAgent(new(AgentType.CounterTrend, 0m, 70m));
        var buyer = runner.AddSessionAgent(new(AgentType.TrendFollowing, 10_000m));
        runner.SubmitMany(asset.AssetId,
            [new(Guid.NewGuid(), seller.AgentName, OrderSide.Sell, OrderType.Limit, 100m, 65m, DateTimeOffset.UtcNow)]);

        var result = runner.SubmitMany(asset.AssetId, Enumerable.Range(0, 65)
            .Select(_ => new Order(Guid.NewGuid(), buyer.AgentName, OrderSide.Buy, OrderType.Market, null, 1m, DateTimeOffset.UtcNow)));

        Assert.Equal(65L, result.Result.Snapshot.TotalTradeCount);
        Assert.Equal(50, result.Result.Snapshot.RecentTrades.Count);
        Assert.Equal(65, result.Result.Trades.Count);
        Assert.Equal(65L, runner.GetCurrent(asset.AssetId)!.Snapshot.TotalTradeCount);
        await runner.StopAsync();
        var restarted = fixture.NewProvider();
        Assert.Equal(65, restarted.GetRequiredService<ISimulationHistoryReader>().GetRun(runId)!.TradeCount);
        using var db = fixture.CreateContext();
        Assert.Equal(65L, db.MarketTicks.OrderByDescending(tick => tick.Tick).First().TotalTradeCount);
        Assert.Equal(65, db.Trades.Count());
    }

    private static Trade Trade(decimal price, decimal quantity, DateTimeOffset time) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Buyer", "Seller", price, quantity, time);

    private static SimulationTickResult Tick(
        Guid runId, Guid sessionId, Guid assetId, int tick, decimal price, IReadOnlyList<Trade>? trades = null) =>
        new(tick, new MarketSnapshot(price, null, null, trades?.Sum(trade => trade.Quantity) ?? 0m, [price], trades ?? []),
            new OrderBookSnapshot([], []), [], [])
        {
            RunId = runId,
            SessionId = sessionId,
            AssetId = assetId
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"abstock-markets-{Guid.NewGuid():N}.db");
        private readonly List<ServiceProvider> _providers = [];
        public string ConnectionString => $"Data Source={_path};Pooling=False";
        public ServiceProvider Services { get; }

        public Fixture() => Services = NewProvider();

        public ServiceProvider NewProvider(bool persistenceFirst = false)
        {
            var services = new ServiceCollection();
            if (persistenceFirst)
            {
                services.AddABStockPersistence(ConnectionString).AddABStockApplication();
            }
            else
            {
                services.AddABStockApplication().AddABStockPersistence(ConnectionString);
            }

            var provider = services.BuildServiceProvider();
            _providers.Add(provider);
            return provider;
        }

        public Asset CreateAsset(string name, decimal price) =>
            Services.GetRequiredService<IAssetCatalog>().Create(
                new(new AssetProfile(name, AssetType.Stock, "Test profile", [], 1m), price));

        public AbStockDbContext CreateContext() =>
            Services.GetRequiredService<IDbContextFactory<AbStockDbContext>>().CreateDbContext();

        public void ExecuteSql(string sql)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public long ExecuteScalar(string sql)
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(command.ExecuteScalar());
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var provider in _providers)
            {
                await provider.GetRequiredService<ISimulationRunner>().StopAsync();
                await provider.DisposeAsync();
            }

            File.Delete(_path);
        }
    }

    private sealed class LegacyDbContext(DbContextOptions<LegacyDbContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Ignore<AssetEntity>();
            modelBuilder.Ignore<TradingSessionEntity>();
            modelBuilder.Ignore<SessionMarketEntity>();
            modelBuilder.Entity<SimulationRunEntity>(entity =>
            {
                entity.ToTable("SimulationRuns");
                entity.HasKey(run => run.Id);
                entity.Ignore(run => run.Market);
                entity.Property(run => run.AssetName).HasMaxLength(160);
                entity.Property(run => run.AssetDescription).HasMaxLength(1_000);
                entity.Property(run => run.AssetType).HasConversion<string>().HasMaxLength(40);
                entity.HasMany(run => run.MarketTicks).WithOne(tick => tick.SimulationRun)
                    .HasForeignKey(tick => tick.SimulationRunId).OnDelete(DeleteBehavior.Cascade);
                entity.HasMany(run => run.Trades).WithOne(trade => trade.SimulationRun)
                    .HasForeignKey(trade => trade.SimulationRunId).OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<MarketTickEntity>(entity =>
            {
                entity.ToTable("MarketTicks");
                entity.HasKey(tick => tick.Id);
                entity.HasIndex(tick => new { tick.SimulationRunId, tick.CapturedAt });
                entity.HasIndex(tick => new { tick.SimulationRunId, tick.Tick }).IsUnique();
            });
            modelBuilder.Entity<TradeEntity>(entity =>
            {
                entity.ToTable("Trades");
                entity.HasKey(trade => trade.Id);
                entity.HasIndex(trade => new { trade.SimulationRunId, trade.ExecutedAt });
                entity.Property(trade => trade.BuyerAgentName).HasMaxLength(120);
                entity.Property(trade => trade.SellerAgentName).HasMaxLength(120);
            });
        }
    }
}
