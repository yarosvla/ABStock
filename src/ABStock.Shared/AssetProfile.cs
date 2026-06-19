namespace ABStock.Shared;

public record AssetProfile(
    string Name,
    AssetType AssetType,
    string Description,
    // IReadOnlyList<AssetFactor> PositiveFactors,
    // IReadOnlyList<AssetFactor> NegativeFactors,
    IReadOnlyList<AssetFactor> Factors,
    decimal NewsSensitivity
);