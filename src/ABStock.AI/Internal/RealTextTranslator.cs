using System.Net.Http.Json;

namespace ABStock.AI.Internal;

internal sealed class RealTextTranslator : ITextTranslator
{
    private readonly HttpClient _httpClient;

    public RealTextTranslator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> TranslateToEnglishAsync(
        string text,
        CancellationToken ct = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "translate",
            new { text },
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<TranslationResponse>(
            cancellationToken: ct);

        return result?.Text
            ?? throw new InvalidOperationException(
                "Python translation service returned an invalid response.");
    }

    private sealed record TranslationResponse(string Text);

    public async Task<IReadOnlyList<string>> TranslateBatchToEnglishAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct = default)
    {
        if (texts.Count == 0)
            return Array.Empty<string>();

        using var response = await _httpClient.PostAsJsonAsync(
            "translate-batch",
            new { texts },
            ct);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BatchTranslationResponse>(
            cancellationToken: ct);

        if (result is null || result.Texts is null || result.Texts.Count != texts.Count)
            throw new InvalidOperationException("Invalid batch translation response.");

        return result.Texts;
    }

    private sealed record BatchTranslationResponse(List<string> Texts);
}