using ABStock.Shared;

namespace ABStock.AI.Internal;

internal sealed class RealFactorMatcher
    : IFactorMatcher
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ITextTranslator _translator;

    public RealFactorMatcher(IEmbeddingService embeddingService, ITextTranslator translator)
    {
        _embeddingService = embeddingService;
        _translator = translator;
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

        IReadOnlyList<float[]> computed = [];

        if (missing.Length > 0)
        {
            var englishNames = new List<string>();

            foreach (var factor in missing)
            {
                englishNames.Add(
                    await _translator.TranslateToEnglishAsync(factor.Name, ct));
            }

            computed = await _embeddingService.CreateEmbeddingsAsync(
                englishNames, ct);
        }

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
