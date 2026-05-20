using ABStock.AI.Internal;
using ABStock.AI.Models;

namespace ABStock.AI.Services;

public sealed class NewsProcessingService : INewsProcessingService
{
    private readonly IFinBertAnalyzer _finBert = new StubFinBertAnalyzer();
    private readonly IAspectMatcher _matcher = new StubAspectMatcher();

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

        return new NewsSignal
        {
            Polarity = polarity,
            Confidence = finBertResult.Confidence,
            ImpactScore = Math.Round(impactScore, 4),
            Explanation = explanation
        };
    }

    private static Polarity DeterminePolarity(FinBertResult finBert, AspectMatchResult match)
    {
        var weightedNegative = match.NegativeMatches + match.RiskMatches * 0.5m;

        if (match.PositiveMatches == 0 && weightedNegative == 0)
        {
            return (finBert.PositiveProbability >= finBert.NegativeProbability ? Polarity.Positive : Polarity.Negative);
        }

        if (match.PositiveMatches > weightedNegative)
        {
            return Polarity.Positive;
        }

        if (weightedNegative > match.PositiveMatches)
        {
            return Polarity.Negative;
        }

        return Polarity.Neutral;
    }

    private static String BuildExplanation(
        FinBertResult finBert,
        AspectMatchResult match,
        Polarity polarity,
        decimal impactScore)
    {
        var confidence = Math.Round(finBert.Confidence * 100, 0);
        return $"News identified as {polarity}. " +
               $"FinBERT confidence: {confidence}%. " +
               $"Positive factor matches: {match.PositiveMatches}, " +
               $"Negative factor matches: {match.NegativeMatches}, " +
               $"Risk factor matches: {match.RiskMatches}. " +
               $"Impact score: {Math.Round(impactScore, 2)}.";
    }
}