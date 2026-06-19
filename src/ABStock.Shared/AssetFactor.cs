namespace ABStock.Shared;

public record AssetFactor(
    string Name,
    bool IsPositive,
    decimal Importance,
    float[] Embedding
);