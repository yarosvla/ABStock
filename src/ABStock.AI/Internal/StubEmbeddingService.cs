namespace ABStock.AI.Internal;

internal sealed class StubEmbeddingService : IEmbeddingService
{
    public Task<float[]> CreateEmbeddingAsync(
        string text,
        CancellationToken ct = default)
    {
        var rnd = new Random(text.GetHashCode());

        var vector = new float[32];

        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)rnd.NextDouble();
        }

        return Task.FromResult(vector);
    }

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        var vectors = new float[texts.Count][];

        for (int i = 0; i < texts.Count; i++)
        {
            vectors[i] = await CreateEmbeddingAsync(texts[i], ct);
        }

        return vectors;
    }
}
