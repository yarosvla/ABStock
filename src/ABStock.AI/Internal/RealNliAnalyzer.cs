using System.Net.Http.Json;

namespace ABStock.AI.Internal;

internal sealed class RealNliAnalyzer : INliAnalyzer
{
    private readonly HttpClient _httpClient;

    public RealNliAnalyzer(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<NliResult>> AnalyzeAsync(
        string premise,
        IReadOnlyList<string> hypotheses,
        CancellationToken ct = default)
    {
        if (hypotheses.Count == 0)
            return [];

        using var response = await _httpClient.PostAsJsonAsync(
            "nli",
            new { premise, hypotheses },
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<NliResponse>(
            cancellationToken: ct);

        if (result is null || result.Results.Count != hypotheses.Count)
            throw new InvalidOperationException("Invalid NLI response.");

        return result.Results
            .Select(scores => new NliResult(
                Entailment: (decimal)scores.GetValueOrDefault("entailment"),
                Neutral: (decimal)scores.GetValueOrDefault("neutral"),
                Contradiction: (decimal)scores.GetValueOrDefault("contradiction")
            ))
            .ToArray();
    }

    private sealed record NliResponse(
        List<Dictionary<string, double>> Results);
}