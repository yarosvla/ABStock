namespace ABStock.AI.Internal;

internal interface IEmbeddingService
{
    Task<float[]> CreateEmbeddingAsync(
        string text,
        CancellationToken ct = default);
}