using ABStock.Shared;

namespace ABStock.Agents.Strategies;

internal static class AssetStrategyPolicy
{
    public static decimal MomentumThresholdPercent(Asset asset) =>
        Math.Round(0.05m + RiskScore(asset) * 0.20m, 3, MidpointRounding.AwayFromZero);

    public static decimal MomentumQuantity(decimal baseQuantity, Asset asset)
    {
        if (baseQuantity <= 0m)
        {
            return baseQuantity;
        }

        var riskMultiplier = 1.25m - RiskScore(asset) * 0.50m;
        var growthMultiplier = asset.GrowthPotential is { } growth
            ? 0.80m + Math.Clamp(growth, 0, 100) / 250m
            : 1m;
        var multiplier = Math.Clamp(riskMultiplier * growthMultiplier, 0.50m, 1.50m);

        return Math.Max(0.01m,
            Math.Round(baseQuantity * multiplier, 2, MidpointRounding.AwayFromZero));
    }

    public static decimal MarketMakerSpread(decimal baseSpread, Asset asset) =>
        baseSpread * (0.75m + RiskScore(asset) * 0.75m);

    public static string Symbol(Asset asset) =>
        string.IsNullOrWhiteSpace(asset.Ticker) ? asset.Name : asset.Ticker;

    private static decimal RiskScore(Asset asset)
    {
        var typeRisk = asset.AssetType switch
        {
            AssetType.Bond => 0.15m,
            AssetType.Stock => 0.45m,
            AssetType.Commodity => 0.65m,
            AssetType.Crypto => 0.90m,
            _ => 0.50m
        };
        var newsRisk = Math.Clamp(asset.Profile.NewsSensitivity, 0m, 1m);

        return (typeRisk + newsRisk) / 2m;
    }
}
