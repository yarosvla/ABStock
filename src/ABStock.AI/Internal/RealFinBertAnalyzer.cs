using System.Net.Http.Json;

namespace ABStock.AI.Internal;

/// <summary>
/// FinBERT из Python-сервиса. Адрес — из AI:ServiceUrl, клиент — из
/// IHttpClientFactory: свой new HttpClient() в singleton держал бы
/// соединения вечно и не видел смены DNS.
/// </summary>
internal sealed class RealFinBertAnalyzer
    : IFinBertAnalyzer
{
    private readonly HttpClient _httpClient;

    public RealFinBertAnalyzer(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FinBertResult> AnalyzeAsync(
        string text,
        CancellationToken ct = default)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "analyze",
                new { text },
                ct);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<Response>(
                    cancellationToken: ct);

        return new FinBertResult
        {
            PositiveProbability =
                result!.Positive,

            NeutralProbability =
                result.Neutral,

            NegativeProbability =
                result.Negative
        };
    }

    private sealed class Response
    {
        public decimal Positive { get; init; }
        public decimal Neutral { get; init; }
        public decimal Negative { get; init; }
    }
}
