using ABStock.Application.MarketHistory;
using ABStock.Application.Simulation;
using ABStock.Persistence.Assets;
using ABStock.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.MarketHistory;

internal sealed class EfMarketHistoryStore(
    IDbContextFactory<AbStockDbContext> contextFactory,
    StorageInitializer initializer) : IMarketHistoryStore
{
    public void StartSession(Guid sessionId, TimeSpan tickInterval, DateTimeOffset startedAt)
    {
        if (sessionId == Guid.Empty || tickInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("A session requires a non-empty id and positive tick interval.");
        }

        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        var existing = db.TradingSessions.Find(sessionId);
        if (existing is not null)
        {
            if (existing.EndedAt is not null || existing.StartedAt != startedAt
                || existing.TickIntervalTicks != tickInterval.Ticks)
            {
                throw new InvalidOperationException("The session id is already used by another or completed session.");
            }

            return;
        }

        db.TradingSessions.Add(new TradingSessionEntity
        {
            Id = sessionId,
            StartedAt = startedAt,
            TickIntervalTicks = tickInterval.Ticks
        });
        db.SaveChanges();
    }

    public void EndSession(Guid sessionId, DateTimeOffset endedAt)
    {
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        var session = db.TradingSessions.Find(sessionId)
            ?? throw new KeyNotFoundException($"Session '{sessionId}' was not recorded.");
        if (session.EndedAt is not null)
        {
            return;
        }

        if (endedAt < session.StartedAt)
        {
            throw new ArgumentException("A session cannot end before it started.", nameof(endedAt));
        }

        session.EndedAt = endedAt;
        db.SaveChanges();
    }

    public Guid StartRun(SimulationConfig config, DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.AssetId.HasValue != config.SessionId.HasValue
            || config.AssetId == Guid.Empty || config.SessionId == Guid.Empty)
        {
            throw new ArgumentException("A linked market run requires both valid AssetId and SessionId.", nameof(config));
        }

        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var runConfig = config;
        if (config.SessionId is { } sessionId && config.AssetId is { } assetId)
        {
            var session = db.TradingSessions.Find(sessionId)
                ?? throw new KeyNotFoundException($"Session '{sessionId}' was not recorded.");
            if (session.EndedAt is not null)
            {
                throw new InvalidOperationException("Cannot add a market to a completed session.");
            }

            var asset = db.Assets.Find(assetId)
                ?? throw new KeyNotFoundException($"Asset '{assetId}' is not in the persistent catalog.");
            var existing = db.SessionMarkets.AsNoTracking()
                .FirstOrDefault(market => market.SessionId == sessionId && market.AssetId == assetId);
            if (existing is not null)
            {
                return existing.RunId;
            }

            var catalogAsset = EfAssetCatalog.ToAsset(asset);
            runConfig = config with
            {
                AssetName = catalogAsset.Name,
                AssetDescription = catalogAsset.Description,
                AssetType = catalogAsset.AssetType,
                StartPrice = catalogAsset.StartPrice
            };
        }

        var run = new SimulationRunEntity
        {
            Id = Guid.NewGuid(),
            AssetName = runConfig.AssetName,
            AssetDescription = runConfig.AssetDescription,
            AssetType = runConfig.AssetType,
            StartPrice = runConfig.StartPrice,
            StartedAt = startedAt
        };

        db.SimulationRuns.Add(run);
        if (config.SessionId is { } linkedSessionId && config.AssetId is { } linkedAssetId)
        {
            db.SessionMarkets.Add(new SessionMarketEntity
            {
                RunId = run.Id,
                SessionId = linkedSessionId,
                AssetId = linkedAssetId
            });
        }

        db.SaveChanges();
        transaction.Commit();

        return run.Id;
    }

    public void SaveTick(Guid runId, SimulationTickResult tickResult, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(tickResult);
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        if (!db.SimulationRuns.Any(run => run.Id == runId))
        {
            throw new KeyNotFoundException($"Run '{runId}' was not recorded.");
        }

        var market = db.SessionMarkets.AsNoTracking().FirstOrDefault(item => item.RunId == runId);
        if ((tickResult.RunId != Guid.Empty && tickResult.RunId != runId)
            || (market is not null && (tickResult.AssetId != market.AssetId || tickResult.SessionId != market.SessionId)))
        {
            throw new ArgumentException("Tick metadata does not match the recorded market.", nameof(tickResult));
        }

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
        var recentTrades = (tickResult.Submission?.Trades ?? [])
            .Concat(tickResult.Snapshot.RecentTrades).DistinctBy(trade => trade.Id).ToArray();
        if (recentTrades.Length == 0)
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
