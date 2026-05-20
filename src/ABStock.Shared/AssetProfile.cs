namespace ABStock.Shared;

public record AssetProfile(
    string Id,
    string Name,
    string AssetType,
    string Description,
    string PositiveFactors,
    string NegativeFactors,
    string Risks,
    double NewsSensitivity,
    IReadOnlyList<string> Keywords
);