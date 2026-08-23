using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation;
using ABStock.Persistence.Extensions;
using ABStock.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Persistence.Tests;

public sealed class MarketHistoryTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"abstock-history-{Guid.NewGuid():N}.db");

    [Fact]
    public void SaveTick_PersistsTicksAndBuildsCandles()
    {
        using var serviceProvider = new ServiceCollection()
            .AddABStockPersistence($"Data Source={_databasePath};Pooling=False")
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<IMarketHistoryStore>();
        var candleReader = serviceProvider.GetRequiredService<IMarketCandleReader>();
        var runId = store.StartRun(CreateConfig(), DateTimeOffset.UnixEpoch);
        var startedAt = DateTimeOffset.Parse("2026-06-11T10:00:00Z");
        var tradeId = CreateId(10);

        store.SaveTick(
            runId,
            CreateTick(1, 100m, totalVolume: 0m, capturedAt: startedAt, trades: []),
            startedAt);
        store.SaveTick(
            runId,
            CreateTick(2, 105m, totalVolume: 2m, capturedAt: startedAt.AddSeconds(5), trades:
            [
                new Trade(
                    Id: tradeId,
                    BuyOrderId: CreateId(1),
                    SellOrderId: CreateId(2),
                    BuyerAgentName: "buyer",
                    SellerAgentName: "seller",
                    Price: 105m,
                    Quantity: 2m,
                    ExecutedAt: startedAt.AddSeconds(5))
            ]),
            startedAt.AddSeconds(5));
        store.SaveTick(
            runId,
            CreateTick(3, 99m, totalVolume: 2m, capturedAt: startedAt.AddSeconds(9), trades: []),
            startedAt.AddSeconds(9));

        var candles = candleReader.GetCandles(runId, TimeSpan.FromSeconds(10), limit: 10);

        var candle = Assert.Single(candles);
        Assert.Equal(100m, candle.Open);
        Assert.Equal(105m, candle.High);
        Assert.Equal(99m, candle.Low);
        Assert.Equal(99m, candle.Close);
        Assert.Equal(2m, candle.Volume);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static SimulationConfig CreateConfig() =>
        new(
            "Test asset",
            "Asset for persistence tests.",
            AssetType.Stock,
            100m,
            TimeSpan.FromSeconds(1),
            []);

    private static SimulationTickResult CreateTick(
        int tick,
        decimal lastPrice,
        decimal totalVolume,
        DateTimeOffset capturedAt,
        IReadOnlyList<Trade> trades) =>
        new(
            tick,
            new MarketSnapshot(
                lastPrice,
                BestBid: null,
                BestAsk: null,
                totalVolume,
                RecentPrices: [lastPrice],
                RecentTrades: trades),
            new OrderBookSnapshot([], []),
            Agents: [],
            Decisions: []);

    private static Guid CreateId(int id) =>
        Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}");
}
