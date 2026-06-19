using ABStock.Shared;
using ABStock.AI.Models;

namespace ABStock.AI.Internal;

internal sealed class StubFactorMatcher : IFactorMatcher
{
    public Task<IReadOnlyList<FactorMatchResult>> MatchAsync(
        float[] newsEmbedding,
        AssetProfile profile, 
        CancellationToken ct = default)
    {
        var results = new List<FactorMatchResult>();

        foreach (var factor in profile.Factors)
        {
            if (factor.Embedding is null)
            {
                continue;
            }

            var similarity =
                CosineSimilarityHelper.Calculate(
                    newsEmbedding,
                    factor.Embedding);

            results.Add(new FactorMatchResult(
                factor,
                similarity));
        }

        return Task.FromResult<IReadOnlyList<FactorMatchResult>>(results);
    }
}