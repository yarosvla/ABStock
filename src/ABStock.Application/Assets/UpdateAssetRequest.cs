using ABStock.Shared;

namespace ABStock.Application.Assets;

public sealed record UpdateAssetRequest(
    AssetProfile Profile,
    string Ticker,
    string Industry,
    bool IncludeGovernmentSupport,
    int? GrowthPotential
);
