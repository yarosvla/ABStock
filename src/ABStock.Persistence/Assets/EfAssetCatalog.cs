using System.Text.Json;
using ABStock.Application.Assets;
using ABStock.Persistence.Entities;
using ABStock.Shared;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.Assets;

internal sealed class EfAssetCatalog(
    IDbContextFactory<AbStockDbContext> contextFactory,
    StorageInitializer initializer) : IAssetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { IgnoreReadOnlyProperties = true };

    public Asset Create(CreateAssetRequest request)
    {
        var asset = AssetFactory.Create(request);
        var entity = new AssetEntity
        {
            Id = asset.AssetId,
            ProfileJson = JsonSerializer.Serialize(asset.Profile, JsonOptions),
            StartPrice = asset.StartPrice,
            CreatedAt = asset.CreatedAt
        };
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        db.Assets.Add(entity);
        db.SaveChanges();
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

    public IReadOnlyList<Asset> GetAll()
    {
        initializer.EnsureInitialized();
        using var db = contextFactory.CreateDbContext();
        return Array.AsReadOnly(db.Assets.AsNoTracking().AsEnumerable()
            .OrderBy(asset => asset.CreatedAt).ThenBy(asset => asset.Id).Select(ToAsset).ToArray());
    }

    internal static Asset ToAsset(AssetEntity entity)
    {
        var profile = JsonSerializer.Deserialize<AssetProfile>(entity.ProfileJson, JsonOptions)
            ?? throw new InvalidOperationException($"Asset '{entity.Id}' has no stored profile.");
        return AssetFactory.Copy(new Asset(entity.Id, profile, entity.StartPrice, entity.CreatedAt));
    }
}
