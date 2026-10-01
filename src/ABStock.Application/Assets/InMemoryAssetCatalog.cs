using ABStock.Shared;

namespace ABStock.Application.Assets;

public sealed class InMemoryAssetCatalog : IAssetCatalog
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Asset> _assets = new();

    public Asset Create(CreateAssetRequest request)
    {
        var asset = AssetFactory.Create(request);

        lock (_sync)
        {
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

    public IReadOnlyList<Asset> GetAll()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_assets.Values.Select(AssetFactory.Copy).ToArray());
        }
    }
}
