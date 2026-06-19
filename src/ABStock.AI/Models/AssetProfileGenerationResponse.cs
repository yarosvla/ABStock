namespace ABStock.AI.Models;

public sealed record AssetProfileGenerationResponse
{
    public required List<GeneratedFactor> Factors { get; init; }
}

public sealed record GeneratedFactor
{
    public required string Name { get; init; }

    public required bool IsPositive { get; init; }

    public required decimal Importance { get; init; }
}