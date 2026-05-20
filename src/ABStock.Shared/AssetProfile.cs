namespace ABStock.Shared;

public record AssetProfile(
    string Name,
    AssetType AssetType,
    string Description,
    IReadOnlyList<string> PositiveFactors,
    IReadOnlyList<string> NegativeFactors,
    IReadOnlyList<string> Risks,
    decimal NewsSensitivity,
    IReadOnlyList<string> Keywords
);