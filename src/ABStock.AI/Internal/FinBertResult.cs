namespace ABStock.AI.Internal;

internal sealed record FinBertResult
{
    public decimal PositiveProbability { get; init; }
    public decimal NeutralProbability { get; init; }
    public decimal NegativeProbability { get; init; }

    public decimal Confidence
        => Math.Max(PositiveProbability, 
           Math.Max(NeutralProbability, 
                    NegativeProbability));
}