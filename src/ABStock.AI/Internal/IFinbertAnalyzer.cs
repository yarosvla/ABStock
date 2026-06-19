namespace ABStock.AI.Internal;

internal interface IFinBertAnalyzer
{
    Task<FinBertResult> AnalyzeAsync(String text, CancellationToken ct = default);
}