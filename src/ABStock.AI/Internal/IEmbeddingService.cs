namespace ABStock.AI.Internal;

internal interface IEmbeddingService
{
    Task<float[]> CreateEmbeddingAsync(
        string text,
        CancellationToken ct = default);

    /// <summary>Векторы для нескольких текстов одним запросом, в том же порядке.</summary>
    Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default);
}
