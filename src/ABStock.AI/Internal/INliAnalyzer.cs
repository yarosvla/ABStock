namespace ABStock.AI.Internal;

internal interface INliAnalyzer
{
    Task<IReadOnlyList<NliResult>> AnalyzeAsync(
        string premise,
        IReadOnlyList<string> hypotheses,
        CancellationToken ct = default);
}