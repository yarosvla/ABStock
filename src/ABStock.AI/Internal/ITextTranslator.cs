namespace ABStock.AI.Internal;

internal interface ITextTranslator
{
    Task<string> TranslateToEnglishAsync(
        string text,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> TranslateBatchToEnglishAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);
}