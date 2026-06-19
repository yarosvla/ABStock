using ABStock.Shared;

namespace ABStock.AI.Internal;
internal sealed record FactorMatchResult(
    AssetFactor Factor,
    decimal Similarity
);