using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation;
using ABStock.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.MarketHistory;

internal sealed class EfMarketHistoryStore(IDbContextFactory<AbStockDbContext> contextFactory) : IMarketHistoryStore
{
    public Guid StartRun(SimulationConfig config, DateTimeOffset startedAt)
    {
        using var db = contextFactory.CreateDbContext();
        db.Database.EnsureCreated();

        var run = new SimulationRunEntity
        {
            Id = Guid.NewGuid(),
            AssetName = config.AssetName,
            AssetDescription = config.AssetDescription,
            AssetType = config.AssetType,
            StartPrice = config.StartPrice,
            StartedAt = startedAt
        };

        db.SimulationRuns.Add(run);
        db.SaveChanges();

        return run.Id;
    }

    public void SaveTick(Guid runId, SimulationTickResult tickResult, DateTimeOffset capturedAt)
    {
        using var db = contextFactory.CreateDbContext();

        db.MarketTicks.Add(new MarketTickEntity
        {
            SimulationRunId = runId,
            Tick = tickResult.Tick,
            CapturedAt = capturedAt,
            LastPrice = tickResult.Snapshot.LastPrice,
            BestBid = tickResult.Snapshot.BestBid,
            BestAsk = tickResult.Snapshot.BestAsk,
            TotalVolume = tickResult.Snapshot.Volume
        });

        AddMissingTrades(db, runId, tickResult);
        db.SaveChanges();
    }

    private static void AddMissingTrades(
        AbStockDbContext db,
        Guid runId,
        SimulationTickResult tickResult)
    {
        var recentTrades = tickResult.Snapshot.RecentTrades;
        if (recentTrades.Count == 0)
        {
            return;
        }

        var recentTradeIds = recentTrades.Select(trade => trade.Id).ToArray();
        var existingTradeIds = db.Trades
            .Where(trade => trade.SimulationRunId == runId && recentTradeIds.Contains(trade.Id))
            .Select(trade => trade.Id)
            .ToHashSet();

        foreach (var trade in recentTrades.Where(trade => !existingTradeIds.Contains(trade.Id)))
        {
            db.Trades.Add(new TradeEntity
            {
                Id = trade.Id,
                SimulationRunId = runId,
                BuyOrderId = trade.BuyOrderId,
                SellOrderId = trade.SellOrderId,
                BuyerAgentName = trade.BuyerAgentName,
                SellerAgentName = trade.SellerAgentName,
                Price = trade.Price,
                Quantity = trade.Quantity,
                ExecutedAt = trade.ExecutedAt
            });
        }
    }
}
