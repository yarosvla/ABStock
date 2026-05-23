namespace ABStock.AI.Internal;

internal sealed record AspectMatchResult(
    int PositiveMatches,
    int NegativeMatches,
    int RiskMatches,
    decimal Score);