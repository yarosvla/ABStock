using ABStock.Shared;

namespace ABStock.Application.Assets;

public sealed class AssetStartPricePolicy : IAssetStartPricePolicy
{
    public decimal Calculate(CreateAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        if (request.GrowthPotential is < 0 or > 100 || request.Profile.NewsSensitivity < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Growth potential and news sensitivity must be valid.");
        }

        var basePrice = request.Profile.AssetType switch
        {
            AssetType.Stock => 96m,
            AssetType.Bond => 101m,
            AssetType.Commodity => 88m,
            AssetType.Crypto => 72m,
            _ => throw new ArgumentException("Unsupported asset type.", nameof(request))
        };
        var price = basePrice + (request.GrowthPotential ?? 0) * 0.42m
            + (request.IncludeGovernmentSupport ? 4m : 0m)
            + request.Profile.NewsSensitivity * 10m;
        return Math.Round(price / 0.05m, MidpointRounding.AwayFromZero) * 0.05m;
    }
}
