using ABStock.Shared;

namespace ABStock.Application.Assets;

public static class AssetFactory
{
    public static Asset Create(CreateAssetRequest request, IAssetStartPricePolicy? startPricePolicy = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateDetails(request.Profile, request.Industry, request.GrowthPotential);
        var startPrice = request.StartPrice ?? (startPricePolicy ?? new AssetStartPricePolicy()).Calculate(request);
        if (startPrice <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Start price must be positive.");
        }

        var assetId = Guid.NewGuid();
        return new Asset(assetId, NormalizeProfile(request.Profile), startPrice, DateTimeOffset.UtcNow)
        {
            Ticker = NormalizeTicker(request.Ticker ?? GenerateTicker(assetId)),
            Industry = request.Industry.Trim(),
            IncludeGovernmentSupport = request.IncludeGovernmentSupport,
            GrowthPotential = request.GrowthPotential
        };
    }

    public static Asset Update(Asset asset, UpdateAssetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (asset.IsArchived)
        {
            throw new InvalidOperationException("An archived asset cannot be edited.");
        }

        ValidateDetails(request.Profile, request.Industry, request.GrowthPotential);
        if (request.Profile.AssetType != asset.AssetType)
        {
            throw new ArgumentException("The type of an existing asset cannot be changed.", nameof(request));
        }

        return asset with
        {
            Profile = NormalizeProfile(request.Profile),
            Ticker = NormalizeTicker(request.Ticker),
            Industry = request.Industry.Trim(),
            IncludeGovernmentSupport = request.IncludeGovernmentSupport,
            GrowthPotential = request.GrowthPotential,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public static string GenerateTicker(Guid assetId) => $"AS{assetId:N}"[..12].ToUpperInvariant();

    public static Asset Copy(Asset asset) => asset with { Profile = CopyProfile(asset.Profile) };

    private static AssetProfile NormalizeProfile(AssetProfile profile) => CopyProfile(profile) with
    {
        Name = profile.Name.Trim(),
        Description = profile.Description.Trim()
    };

    private static void ValidateDetails(AssetProfile profile, string industry, int? growthPotential)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Description);
        ArgumentNullException.ThrowIfNull(industry);
        if (!Enum.IsDefined(profile.AssetType) || profile.NewsSensitivity < 0m
            || profile.Name.Trim().Length > 160 || industry.Trim().Length > 160)
        {
            throw new ArgumentException("Asset type, name, industry and news sensitivity must be valid.");
        }

        if (growthPotential is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(growthPotential), "Growth potential must be between 0 and 100.");
        }
    }

    private static string NormalizeTicker(string ticker)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        var normalized = ticker.Trim().ToUpperInvariant();
        if (normalized.Length > 24
            || normalized.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
        {
            throw new ArgumentException("Ticker must contain up to 24 ASCII letters, digits, dots, hyphens or underscores.", nameof(ticker));
        }

        return normalized;
    }

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
