using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Internal;

internal sealed class RealFactorMatcher
    : IFactorMatcher
{
    public Task<IReadOnlyList<FactorMatchResult>> MatchAsync(
        float[] newsEmbedding,
        AssetProfile profile,
        CancellationToken ct = default)
    {
        var results =
            new List<FactorMatchResult>();

        var allFactors = profile.Factors;

        foreach (var factor in allFactors)
        {
            var similarity =
                CosineSimilarityHelper.Calculate(
                    newsEmbedding,
                    factor.Embedding);

            results.Add(
                new FactorMatchResult(
                    factor,
                    similarity));
        }

        return Task.FromResult<IReadOnlyList<FactorMatchResult>>(
            results);
    }
}