namespace ABStock.AI.Internal;

internal interface INliAnalyzer
{
    Task<IReadOnlyList<bool>> CheckEntailmentAsync(
        string premise,
        IReadOnlyList<string> hypotheses,
        CancellationToken ct = default);
}