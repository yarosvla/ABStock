using ABStock.Shared;

namespace ABStock.AI.Internal;

internal sealed class RealFactorMatcher
    : IFactorMatcher
{
    private readonly IEmbeddingService _embeddingService;

    public RealFactorMatcher(IEmbeddingService embeddingService)
    {
        _embeddingService = embeddingService;
    }

    public async Task<IReadOnlyList<FactorMatchResult>> MatchAsync(
        float[] newsEmbedding,
        AssetProfile profile,
        CancellationToken ct = default)
    {
        var allFactors = profile.Factors;

        // У запасного и демо-профиля векторов нет: они собраны без модели.
        // Досчитываем их здесь одним запросом, иначе такой профиль не
        // сопоставился бы ни с одной новостью.
        var missing = allFactors
            .Where(factor => factor.Embedding.Length == 0)
            .ToArray();

        IReadOnlyList<float[]> computed = missing.Length == 0
            ? []
            : await _embeddingService.CreateEmbeddingsAsync(
                missing.Select(factor => factor.Name).ToArray(),
                ct);

        var results =
            new List<FactorMatchResult>();

        var next = 0;

        foreach (var factor in allFactors)
        {
            var embedding = factor.Embedding.Length == 0
                ? computed[next++]
                : factor.Embedding;

            var similarity =
                CosineSimilarityHelper.Calculate(
                    newsEmbedding,
                    embedding);

            results.Add(
                new FactorMatchResult(
                    factor,
                    similarity));
        }

        return results;
    }
}
