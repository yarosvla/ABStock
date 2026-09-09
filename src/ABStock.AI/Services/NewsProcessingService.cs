using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public sealed class NewsProcessingService : INewsProcessingService
{
    private readonly IFinBertAnalyzer _finBert = new StubFinBertAnalyzer();
    private readonly IAspectMatcher _matcher = new ProfileAspectMatcher();

    internal NewsProcessingService(IFinBertAnalyzer finBert, IAspectMatcher matcher)
    {
        _finBert = finBert;
        _matcher = matcher;
    }

    public NewsSignal Analyze(NewsAnalysisRequest request)
    {
        var finBertResult = _finBert.Analyze(request.NewsText);
        var matchResult = _matcher.Match(request.NewsText, request.Profile);

        var polarity = DeterminePolarity(finBertResult, matchResult);
        var impactScore = finBertResult.Confidence * request.Profile.NewsSensitivity * matchResult.Score;

        var explanation = BuildExplanation(finBertResult, matchResult, polarity, impactScore);

        return new NewsSignal(
            Polarity: polarity,
            Confidence: finBertResult.Confidence,
            ImpactScore: Math.Round(impactScore, 4),
            Explanation: explanation
        )
        {
            PositiveMatches = matchResult.PositiveMatches,
            NegativeMatches = matchResult.NegativeMatches,
            RiskMatches = matchResult.RiskMatches,
            MatchScore = matchResult.Score,
            Factors = matchResult.Matches
        };
    }

    private static SignalPolarity DeterminePolarity(FinBertResult finBert, AspectMatchResult match)
    {
        var weightedNegative = match.NegativeMatches + match.RiskMatches * 0.5m;

        if (match.PositiveMatches == 0 && weightedNegative == 0)
        {
            return finBert.PositiveProbability >= finBert.NegativeProbability
                ? SignalPolarity.Positive
                : SignalPolarity.Negative;
        }

        if (match.PositiveMatches > weightedNegative)
            return SignalPolarity.Positive;

        if (weightedNegative > match.PositiveMatches)
            return SignalPolarity.Negative;

        return SignalPolarity.Neutral;
    }

    private static string BuildExplanation(
        FinBertResult finBert,
        AspectMatchResult match,
        SignalPolarity polarity,
        decimal impactScore)
    {
        var confidence = Math.Round(finBert.Confidence * 100, 0);
        return $"Новость определена как {PolarityName(polarity)}. " +
               $"Уверенность FinBERT: {confidence}%. " +
               $"Затронуто позитивных факторов: {match.PositiveMatches}, " +
               $"негативных факторов: {match.NegativeMatches}, " +
               $"рисков: {match.RiskMatches}. " +
               $"Сила влияния: {Math.Round(impactScore, 2)}.";
    }

    private static string PolarityName(SignalPolarity polarity) => polarity switch
    {
        SignalPolarity.Positive => "позитивная",
        SignalPolarity.Negative => "негативная",
        _ => "нейтральная"
    };
}