namespace ABStock.AI.Internal;

internal sealed record NliResult(
    decimal Entailment,
    decimal Neutral,
    decimal Contradiction
);