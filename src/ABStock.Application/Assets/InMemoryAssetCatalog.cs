using ABStock.Shared;

namespace ABStock.Application.Assets;

public sealed class InMemoryAssetCatalog(IAssetStartPricePolicy? startPricePolicy = null) : IAssetCatalog
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Asset> _assets = new();

    public Asset Create(CreateAssetRequest request)
    {
        var asset = AssetFactory.Create(request, startPricePolicy);

        lock (_sync)
        {
            EnsureTickerAvailable(asset);
            _assets.Add(asset.AssetId, asset);
        }

        return AssetFactory.Copy(asset);
    }

    public Asset? Get(Guid assetId)
    {
        lock (_sync)
        {
            return _assets.TryGetValue(assetId, out var asset) ? AssetFactory.Copy(asset) : null;
        }
    }

    public IReadOnlyList<Asset> GetAll(bool includeArchived = false)
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_assets.Values
                .Where(asset => includeArchived || !asset.IsArchived).Select(AssetFactory.Copy).ToArray());
        }
    }

    public Asset Update(Guid assetId, UpdateAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            var updated = AssetFactory.Update(RequireAsset(assetId), request);
            EnsureTickerAvailable(updated);
            _assets[assetId] = updated;
            return AssetFactory.Copy(updated);
        }
    }

    public Asset Archive(Guid assetId)
    {
        lock (_sync)
        {
            var asset = RequireAsset(assetId);
            var archived = asset.IsArchived ? asset : asset with { ArchivedAt = DateTimeOffset.UtcNow };
            _assets[assetId] = archived;
            return AssetFactory.Copy(archived);
        }
    }

    private Asset RequireAsset(Guid assetId) => _assets.GetValueOrDefault(assetId)
        ?? throw new KeyNotFoundException($"Asset '{assetId}' is not in the catalog.");

    private void EnsureTickerAvailable(Asset asset)
    {
        if (_assets.Values.Any(existing => existing.AssetId != asset.AssetId
            && string.Equals(existing.Ticker, asset.Ticker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Ticker '{asset.Ticker}' is already used.");
        }
    }
}
