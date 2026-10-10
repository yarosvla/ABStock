using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

internal sealed class NewsProcessingService : INewsProcessingService
{
    /// <summary>
    /// С какой близости фактор считается задетым новостью. У
    /// text-embedding-3-large косинус несвязанных текстов около 0,1–0,3,
    /// новости и фактора на одну тему — около 0,4–0,6. Прежний порог 0,75
    /// почти не достигался, и любая новость выходила «ни о чём».
    /// </summary>
    internal const decimal RelevanceThreshold = 0.60m;

    private readonly IFinBertAnalyzer _finBert;
    private readonly IFactorMatcher _matcher;

    private readonly IEmbeddingService _embeddingService;

    private readonly ITextTranslator _translator;

    private readonly INliAnalyzer _nliAnalyzer;

    public NewsProcessingService(
        IFinBertAnalyzer finBert,
        IFactorMatcher matcher,
        IEmbeddingService embeddingService,
        ITextTranslator translator,
        INliAnalyzer nliAnalyzer)
    {
        _finBert = finBert;
        _matcher = matcher;
        _embeddingService = embeddingService;
        _translator = translator;
        _nliAnalyzer = nliAnalyzer;
    }

    public async Task<NewsSignal> AnalyzeAsync(NewsAnalysisRequest request, CancellationToken ct = default)
    {
        var englishNewsText = await _translator.TranslateToEnglishAsync(
            request.NewsText, ct);

        var newsEmbedding = await _embeddingService.CreateEmbeddingAsync(
            englishNewsText, ct);

        var finBertResult =
            await _finBert.AnalyzeAsync(
                request.NewsText,
                ct);

        var matches =
            await _matcher.MatchAsync(
                newsEmbedding,
                request.Profile,
                ct);

        Console.WriteLine("\n========== FACTOR SIMILARITY ==========");
        Console.WriteLine($"News: {request.NewsText}");
        Console.WriteLine($"Threshold: {RelevanceThreshold:F2}");

        foreach (var match in matches.OrderByDescending(x => x.Similarity))
        {
            Console.WriteLine(
                $"[{(match.Factor.IsPositive ? "POS" : "NEG")}] " +
                $"Similarity: {match.Similarity:F4} | " +
                $"Weight: {match.Factor.Importance:F2} | " +
                $"{match.Factor.Name}");
        }

        Console.WriteLine("=======================================\n");

        var relevantMatches =
            matches
                .Where(x => x.Similarity > RelevanceThreshold)
                .OrderByDescending(x => x.Similarity)
                .ToList();

        if (relevantMatches.Count > 0)
        {
            var hypotheses = relevantMatches
                .Select(x => x.Factor.NameEn ?? x.Factor.Name)
                .ToArray();

            var entailments = await _nliAnalyzer.CheckEntailmentAsync(
                englishNewsText,
                hypotheses,
                ct);

            relevantMatches = relevantMatches
                .Where((match, index) => entailments[index])
                .ToList();
        }

        decimal totalImpact = 0;

        foreach (var match in relevantMatches)
        {
            var sentimentStrength =
                finBertResult.PositiveProbability
                - finBertResult.NegativeProbability;

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
