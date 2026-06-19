using System.Net.Http.Json;

namespace ABStock.AI.Internal;

internal sealed class RealFinBertAnalyzer
    : IFinBertAnalyzer
{
    private readonly HttpClient _httpClient =
        new();

    public async Task<FinBertResult> AnalyzeAsync(
        string text,
        CancellationToken ct = default)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "http://127.0.0.1:8000/analyze",
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