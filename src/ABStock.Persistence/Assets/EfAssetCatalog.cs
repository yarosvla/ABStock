using System.Text.Json;
using ABStock.Application.Assets;
using ABStock.Persistence.Entities;
using ABStock.Shared;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.Assets;

internal sealed class EfAssetCatalog(
    IDbContextFactory<AbStockDbContext> contextFactory,
    StorageInitializer initializer,
    IAssetStartPricePolicy startPricePolicy) : IAssetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IgnoreReadOnlyProperties = true };

    public Asset Create(CreateAssetRequest request)
    {
        var asset = AssetFactory.Create(request, startPricePolicy);
        var entity = new AssetEntity
        {
            Id = asset.AssetId,
            ProfileJson = JsonSerializer.Serialize(asset.Profile, JsonOptions),
            StartPrice = asset.StartPrice,
            CreatedAt = asset.CreatedAt
        };
        ApplyDetails(entity, asset);
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        EnsureTickerAvailable(db, asset);
        db.Assets.Add(entity);
        SaveChanges(db);
        return asset;
    }

    public Asset? Get(Guid assetId)
    {
        if (assetId == Guid.Empty)
        {
            return null;
        }

        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        var entity = db.Assets.AsNoTracking().FirstOrDefault(asset => asset.Id == assetId);
        return entity is null ? null : ToAsset(entity);
    }

    public IReadOnlyList<Asset> GetAll(bool includeArchived = false)
    {
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        return Array.AsReadOnly(db.Assets.AsNoTracking()
            .Where(asset => includeArchived || asset.ArchivedAt == null).AsEnumerable()
            .OrderBy(asset => asset.CreatedAt).ThenBy(asset => asset.Id).Select(ToAsset).ToArray());
    }

    public Asset Update(Guid assetId, UpdateAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        var entity = db.Assets.Find(assetId)
            ?? throw new KeyNotFoundException($"Asset '{assetId}' is not in the catalog.");
        var asset = AssetFactory.Update(ToAsset(entity), request);
        EnsureTickerAvailable(db, asset);
        entity.ProfileJson = JsonSerializer.Serialize(asset.Profile, JsonOptions);
        ApplyDetails(entity, asset);
        SaveChanges(db);
        return asset;
    }

    public Asset Archive(Guid assetId)
    {
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        var entity = db.Assets.Find(assetId)
            ?? throw new KeyNotFoundException($"Asset '{assetId}' is not in the catalog.");
        if (entity.ArchivedAt is null)
        {
            entity.ArchivedAt = DateTimeOffset.UtcNow;
            db.SaveChanges();
        }

        return ToAsset(entity);
    }

    internal static Asset ToAsset(AssetEntity entity)
    {
        var profile = JsonSerializer.Deserialize<AssetProfile>(entity.ProfileJson, JsonOptions)
            ?? throw new InvalidOperationException($"Asset '{entity.Id}' has no stored profile.");
        return AssetFactory.Copy(new Asset(entity.Id, profile, entity.StartPrice, entity.CreatedAt)
        {
            Ticker = entity.Ticker,
            Industry = entity.Industry,
            IncludeGovernmentSupport = entity.IncludeGovernmentSupport,
            GrowthPotential = entity.GrowthPotential,
            UpdatedAt = entity.UpdatedAt,
            ArchivedAt = entity.ArchivedAt
        });
    }

    private static void ApplyDetails(AssetEntity entity, Asset asset)
    {
        entity.Ticker = asset.Ticker;
        entity.Industry = asset.Industry;
        entity.IncludeGovernmentSupport = asset.IncludeGovernmentSupport;
        entity.GrowthPotential = asset.GrowthPotential;
        entity.UpdatedAt = asset.UpdatedAt;
        entity.ArchivedAt = asset.ArchivedAt;
    }

    private static void EnsureTickerAvailable(AbStockDbContext db, Asset asset)
    {
        if (db.Assets.Any(existing => existing.Id != asset.AssetId && existing.Ticker == asset.Ticker))
        {
            throw new ArgumentException($"Ticker '{asset.Ticker}' is already used.");
        }
    }

    private static void SaveChanges(AbStockDbContext db)
    {
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            throw new ArgumentException("Ticker is already used.", exception);
        }
    }
}
