using ABStock.Shared;
using ABStock.AI.Models;

namespace ABStock.AI.Internal;

internal interface IFactorMatcher
{
    Task<IReadOnlyList<FactorMatchResult>> MatchAsync(
        float[] newsEmbedding,
        AssetProfile profile,
        CancellationToken ct = default);
}