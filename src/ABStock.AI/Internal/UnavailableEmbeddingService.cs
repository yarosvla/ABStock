namespace ABStock.AI.Internal;

/// <summary>
/// Ключ OpenAI не задан. Приложение при этом запускается: профиль соберётся
/// запасным алгоритмом, а разбор новости покажет эту причину на экране.
/// </summary>
internal sealed class UnavailableEmbeddingService : IEmbeddingService
{
    private const string Message =
        "не задан ключ OpenAI (переменная окружения OPENAI_API_KEY или OpenAI:ApiKey)";

    public Task<float[]> CreateEmbeddingAsync(string text, CancellationToken ct = default) =>
        throw new InvalidOperationException(Message);

    public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        throw new InvalidOperationException(Message);
}
