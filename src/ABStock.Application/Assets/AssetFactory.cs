using ABStock.Shared;

namespace ABStock.Application.Assets;

public static class AssetFactory
{
    public static Asset Create(CreateAssetRequest request)
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
        return new Asset(Guid.NewGuid(), profile, request.StartPrice, DateTimeOffset.UtcNow);
    }

    public static Asset Copy(Asset asset) => asset with { Profile = CopyProfile(asset.Profile) };

    private static AssetProfile CopyProfile(AssetProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile.Factors);
        var factors = profile.Factors.Select(factor =>
        {
            ArgumentNullException.ThrowIfNull(factor);
            ArgumentNullException.ThrowIfNull(factor.Embedding);
            return factor with { Embedding = factor.Embedding.ToArray() };
        }).ToArray();
        return profile with { Factors = Array.AsReadOnly(factors) };
    }
}
