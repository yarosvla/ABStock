namespace ABStock.AI.Models;

public sealed record NewsSignal
{
    public Polarity Polarity { get; init; }
    public decimal Confidence { get; init; }
    public decimal ImpactScore { get; init; }
    public String Explanation { get; init; } = string.Empty;
}