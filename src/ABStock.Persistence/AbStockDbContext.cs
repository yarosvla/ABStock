using ABStock.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence;

public sealed class AbStockDbContext(DbContextOptions<AbStockDbContext> options) : DbContext(options)
{
    public DbSet<AssetEntity> Assets => Set<AssetEntity>();

    public DbSet<TradingSessionEntity> TradingSessions => Set<TradingSessionEntity>();

    public DbSet<SessionMarketEntity> SessionMarkets => Set<SessionMarketEntity>();

    public DbSet<SimulationRunEntity> SimulationRuns => Set<SimulationRunEntity>();

    public DbSet<MarketTickEntity> MarketTicks => Set<MarketTickEntity>();

    public DbSet<TradeEntity> Trades => Set<TradeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssetEntity>(entity =>
        {
            entity.ToTable("Assets");
            entity.HasKey(asset => asset.Id);
            entity.Property(asset => asset.Ticker).HasMaxLength(24).UseCollation("NOCASE");
            entity.Property(asset => asset.Industry).HasMaxLength(160);
            entity.HasIndex(asset => asset.Ticker).IsUnique();
        });

        modelBuilder.Entity<TradingSessionEntity>(entity =>
        {
            entity.ToTable("TradingSessions");
            entity.HasKey(session => session.Id);
        });

        modelBuilder.Entity<SessionMarketEntity>(entity =>
        {
            entity.ToTable("SessionMarkets");
            entity.HasKey(market => market.RunId);
            entity.HasIndex(market => new { market.SessionId, market.AssetId }).IsUnique();
            entity.HasOne(market => market.Run).WithOne(run => run.Market)
                .HasForeignKey<SessionMarketEntity>(market => market.RunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(market => market.Session).WithMany(session => session.Markets)
                .HasForeignKey(market => market.SessionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(market => market.Asset).WithMany(asset => asset.Markets)
                .HasForeignKey(market => market.AssetId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SimulationRunEntity>(entity =>
        {
            entity.ToTable("SimulationRuns");
            entity.HasKey(run => run.Id);
            entity.Property(run => run.AssetName).HasMaxLength(160);
            entity.Property(run => run.AssetDescription).HasMaxLength(1_000);
            entity.Property(run => run.AssetType).HasConversion<string>().HasMaxLength(40);
            entity.HasMany(run => run.MarketTicks)
                .WithOne(tick => tick.SimulationRun)
                .HasForeignKey(tick => tick.SimulationRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(run => run.Trades)
                .WithOne(trade => trade.SimulationRun)
                .HasForeignKey(trade => trade.SimulationRunId)
                .OnDelete(DeleteBehavior.Cascade);
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
