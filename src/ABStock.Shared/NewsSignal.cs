namespace ABStock.Shared;

public record NewsSignal(
    SignalPolarity Polarity,
    decimal Confidence,
    decimal ImpactScore,
    string Explanation
);