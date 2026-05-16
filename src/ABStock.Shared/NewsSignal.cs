namespace ABStock.Shared;

public record NewsSignal(
    string OriginalText,
    SignalPolarity Polarity,
    double Confidence,
    double ImpactScore,
    double PositiveProbability,
    double NeutralProbability,
    double NegativeProbability
);