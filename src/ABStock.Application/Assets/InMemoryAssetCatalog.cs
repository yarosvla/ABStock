using ABStock.Shared;

namespace ABStock.Application.Assets;

public sealed class InMemoryAssetCatalog : IAssetCatalog
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Asset> _assets = new();

    public Asset Create(CreateAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Profile.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Profile.Description);

        if (!Enum.IsDefined(request.Profile.AssetType))
        {
            throw new ArgumentException("Unsupported asset type.", nameof(request));
        }

        if (request.StartPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Start price must be positive.");
        }

        var profile = CopyProfile(request.Profile) with
        {
            Name = request.Profile.Name.Trim(),
            Description = request.Profile.Description.Trim()
        };
        var asset = new Asset(Guid.NewGuid(), profile, request.StartPrice, DateTimeOffset.UtcNow);

        lock (_sync)
        {
            _assets.Add(asset.AssetId, asset);
        }

        return CopyAsset(asset);
    }

    public Asset? Get(Guid assetId)
    {
        lock (_sync)
        {
            return _assets.TryGetValue(assetId, out var asset) ? CopyAsset(asset) : null;
        }
    }

    public IReadOnlyList<Asset> GetAll()
    {
        lock (_sync)
        {
            return Array.AsReadOnly(_assets.Values.Select(CopyAsset).ToArray());
        }
    }

    private static Asset CopyAsset(Asset asset) => asset with
    {
        Profile = CopyProfile(asset.Profile)
    };

    private static AssetProfile CopyProfile(AssetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile.Factors);

        // Records are shallow copies; factor lists and embedding arrays must not expose catalog state.
        var factors = profile.Factors.Select(factor =>
        {
            ArgumentNullException.ThrowIfNull(factor);
            ArgumentNullException.ThrowIfNull(factor.Embedding);

            return factor with { Embedding = factor.Embedding.ToArray() };
        }).ToArray();

        return profile with { Factors = Array.AsReadOnly(factors) };
    }
}
