using ABStock.Shared;

namespace ABStock.Application.Assets;

public sealed record CreateAssetRequest(
    AssetProfile Profile,
    decimal? StartPrice = null
)
{
    public string? Ticker { get; init; }

    public string Industry { get; init; } = string.Empty;

    public bool IncludeGovernmentSupport { get; init; }

    public int? GrowthPotential { get; init; }
}
