using OpenAI.Embeddings;

namespace ABStock.AI.Internal;

internal sealed class OpenAIEmbeddingService
    : IEmbeddingService
{
    private readonly EmbeddingClient _client;

    public OpenAIEmbeddingService(string apiKey)
    {
        _client = new EmbeddingClient(
            "text-embedding-3-large",
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

    public async Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        // Один запрос на все тексты: полсотни параллельных запросов на каждый
        // фактор упирались в лимит частоты запросов OpenAI.
        var response =
            await _client.GenerateEmbeddingsAsync(
                texts,
                cancellationToken: ct);

        return response.Value
            .OrderBy(embedding => embedding.Index)
            .Select(embedding => embedding.ToFloats().ToArray())
            .ToArray();
    }
}
