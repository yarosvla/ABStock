using OpenAI.Embeddings;

namespace ABStock.AI.Internal;

internal sealed class OpenAIEmbeddingService
    : IEmbeddingService
{
    private readonly EmbeddingClient _client;

    public OpenAIEmbeddingService(string apiKey)
    {
        _client = new EmbeddingClient(
            "text-embedding-3-small",
            apiKey);
    }

    public async Task<float[]> CreateEmbeddingAsync(
        string text,
        CancellationToken ct = default)
    {
        var response =
            await _client.GenerateEmbeddingAsync(
                text,
                cancellationToken: ct);

        return response.Value.ToFloats().ToArray();
    }
}