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
}