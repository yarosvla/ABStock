using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

internal sealed class NewsProcessingService : INewsProcessingService
{
    /// <summary>
    /// С какой близости фактор считается задетым новостью. У
    /// text-embedding-3-small косинус несвязанных текстов около 0,1–0,3,
    /// новости и фактора на одну тему — около 0,4–0,6. Прежний порог 0,75
    /// почти не достигался, и любая новость выходила «ни о чём».
    /// </summary>
    internal const decimal RelevanceThreshold = 0.40m;

    private readonly IFinBertAnalyzer _finBert;
    private readonly IFactorMatcher _matcher;

    private readonly IEmbeddingService _embeddingService;

    public NewsProcessingService(IFinBertAnalyzer finBert, IFactorMatcher matcher, IEmbeddingService embeddingService)
    {
        _finBert = finBert;
        _matcher = matcher;
        _embeddingService = embeddingService;
    }

    public async Task<NewsSignal> AnalyzeAsync(NewsAnalysisRequest request, CancellationToken ct = default)
    {
        var newsEmbedding =
            await _embeddingService.CreateEmbeddingAsync(
                request.NewsText,
                ct);

        var finBertResult =
            await _finBert.AnalyzeAsync(
                request.NewsText,
                ct);

        var matches =
            await _matcher.MatchAsync(
                newsEmbedding,
                request.Profile,
                ct);

        var relevantMatches =
            matches
                .Where(x => x.Similarity > RelevanceThreshold)
                .OrderByDescending(x => x.Similarity)
                .ToList();

        decimal totalImpact = 0;

        foreach (var match in relevantMatches)
        {
            var sentimentStrength =
                finBertResult.PositiveProbability
                - finBertResult.NegativeProbability;

            if (!match.Factor.IsPositive)
            {
                sentimentStrength *= -1;
            }

            var contribution =
                sentimentStrength
                * match.Similarity
                * match.Factor.Importance;

            totalImpact += contribution;
        }

        var polarity = DeterminePolarity(finBertResult);

        var impactScore =
            totalImpact
            * request.Profile.NewsSensitivity;

        var explanation = BuildExplanation(
            polarity,
            finBertResult,
            relevantMatches,
            impactScore
        );

        return new NewsSignal(
            Polarity: polarity,
            Confidence: finBertResult.Confidence,
            ImpactScore: Math.Round(impactScore, 4),
            Explanation: explanation
        )
        {
            PositiveMatches = relevantMatches.Count(x => x.Factor.IsPositive),
            NegativeMatches = relevantMatches.Count(x => !x.Factor.IsPositive),
            MatchScore = relevantMatches.Count == 0
                ? 0m
                : Math.Round(relevantMatches.Average(x => x.Similarity), 4),
            Factors = relevantMatches
                .Select(x => new NewsFactorMatch(
                    x.Factor.IsPositive ? NewsFactorKind.Positive : NewsFactorKind.Negative,
                    x.Factor.Name,
                    Math.Round(x.Similarity, 4),
                    x.Factor.Importance))
                .ToArray()
        };
    }

    private static SignalPolarity DeterminePolarity(
        FinBertResult result)
    {
        if (result.PositiveProbability >
            result.NegativeProbability &&
            result.PositiveProbability >
            result.NeutralProbability)
        {
            return SignalPolarity.Positive;
        }

        if (result.NegativeProbability >
            result.PositiveProbability &&
            result.NegativeProbability >
            result.NeutralProbability)
        {
            return SignalPolarity.Negative;
        }

        return SignalPolarity.Neutral;
    }

    private static string BuildExplanation(
        SignalPolarity polarity,
        FinBertResult finBert,
        IReadOnlyList<FactorMatchResult> relevantMatches,
        decimal impactScore)
    {
        var header =
            $"Новость определена как {PolarityName(polarity)}. " +
            $"Уверенность FinBERT: {finBert.Confidence:F2}. " +
            $"Сила влияния: {impactScore:F2}.";

        if (!relevantMatches.Any())
        {
            return header + " Ни один фактор профиля не оказался достаточно близок к новости.";
        }

        var factorLines =
            relevantMatches.Select(x =>
                $"{x.Factor.Name} " +
                $"({(x.Factor.IsPositive ? "позитивный" : "негативный")}, " +
                $"вес {x.Factor.Importance:F2}, " +
                $"близость {x.Similarity:F2})");

        return header + $" Задетые факторы: {string.Join("; ", factorLines)}.";
    }

    private static string PolarityName(SignalPolarity polarity) => polarity switch
    {
        SignalPolarity.Positive => "позитивная",
        SignalPolarity.Negative => "негативная",
        _ => "нейтральная"
    };
}
