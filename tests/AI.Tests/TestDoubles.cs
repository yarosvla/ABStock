using System.Net;
using System.Text;
using ABStock.AI.Internal;
using ABStock.Shared;

namespace ABStock.AI.Tests;

internal sealed class FixedFinBert(decimal positive, decimal neutral, decimal negative) : IFinBertAnalyzer
{
    public Task<FinBertResult> AnalyzeAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(new FinBertResult
        {
            PositiveProbability = positive,
            NeutralProbability = neutral,
            NegativeProbability = negative
        });
}

/// <summary>Векторы по словарю «текст → вектор»; незнакомый текст — ошибка теста.</summary>
internal sealed class DictionaryEmbeddings(Dictionary<string, float[]> vectors) : IEmbeddingService
{
    public List<IReadOnlyList<string>> BatchCalls { get; } = [];

    public Task<float[]> CreateEmbeddingAsync(string text, CancellationToken ct = default) =>
        Task.FromResult(vectors[text]);

    public Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        BatchCalls.Add(texts);
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(text => vectors[text]).ToArray());
    }
}

internal sealed class StubHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
    }
}

internal static class Profiles
{
    public static AssetProfile With(params AssetFactor[] factors) =>
        new("Гелиос Энерго", AssetType.Stock, "Региональный энергетический холдинг.", factors, 0.80m);
}
