using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public sealed class NewsProcessingService : INewsProcessingService
{
    private readonly IFinBertAnalyzer _finBert;
    private readonly IFactorMatcher _matcher;

    private readonly IEmbeddingService _embeddingService;

    internal NewsProcessingService(IFinBertAnalyzer finBert, IFactorMatcher matcher, IEmbeddingService embeddingService)
    {
        _finBert = finBert;
        _matcher = matcher;
        _embeddingService = embeddingService;
    }

    async public Task<NewsSignal> AnalyzeAsync(NewsAnalysisRequest request, CancellationToken ct = default)
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
                .Where(x => x.Similarity > 0.75m)
                .ToList();
        /*
        var averageSimilarity =
            relevantMatches.Any()
                ? relevantMatches.Average(x => x.Similarity)
                : 0m;

        var averageImportance =
            relevantMatches.Any()
                ? relevantMatches.Average(x => x.Factor.Importance)
                : 0m;
        */
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
        /*
        var impactScore =
            finBertResult.Confidence
            * averageSimilarity
            * averageImportance
            * request.Profile.NewsSensitivity;
        */
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
        );
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
        if (!relevantMatches.Any())
        {
            return
                $"Signal: {polarity}. " +
                $"FinBERT confidence: {finBert.Confidence:F2}. " +
                $"Impact: {impactScore:F2}. " +
                $"No sufficiently relevant business factors matched.";
        }

        var factorLines =
            relevantMatches.Select(x =>
            {
                var factorType =
                    x.Factor.IsPositive
                        ? "positive"
                        : "negative";

                return
                    $"{x.Factor.Name} " +
                    $"[{factorType}] " +
                    $"importance: {x.Factor.Importance:F2}, " +
                    $"similarity: {x.Similarity:F2}";
            });

        return
            $"Signal: {polarity}. " +
            $"FinBERT confidence: {finBert.Confidence:F2}. " +
            $"Impact: {impactScore:F2}. " +
            $"Matched factors: " +
            $"{string.Join("; ", factorLines)}.";
        }
}