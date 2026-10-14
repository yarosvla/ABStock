using System.Net.Http.Json;

namespace ABStock.AI.Internal;

internal sealed class RealNliAnalyzer : INliAnalyzer
{
    private readonly HttpClient _httpClient;

    public RealNliAnalyzer(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<bool>> CheckEntailmentAsync(
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

        for (int i = 0; i < hypotheses.Count; i++)
{
            Console.WriteLine($"NLI hypothesis: {hypotheses[i]}");

            foreach (var (label, score) in result.Results[i])
            {
                Console.WriteLine($"  {label}: {score:F4}");
            }
        }

        return result.Results
            .Select(scores =>
                scores.TryGetValue("entailment", out var entailment) &&
                entailment > scores.GetValueOrDefault("neutral") &&
                entailment > scores.GetValueOrDefault("contradiction"))
            .ToArray();
    }

    private sealed record NliResponse(
        List<Dictionary<string, double>> Results);
}