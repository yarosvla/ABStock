namespace ABStock.AI.Internal;

internal sealed class StubFinBertAnalyzer : IFinBertAnalyzer
{
    private readonly Random _rnd = new();

    public FinBertResult Analyze(String text)
    {
        
        var positive = (decimal) _rnd.NextDouble();
        var negative = (decimal) _rnd.NextDouble();
        var neutral = (decimal) _rnd.NextDouble();

        var total = positive + negative + neutral;

        return new FinBertResult
        {
            PositiveProbability = Math.Round(positive / total, 4),
            NeutralProbability = Math.Round(neutral / total, 4),
            NegativeProbability = Math.Round(negative / total, 4)
        };
    }
}