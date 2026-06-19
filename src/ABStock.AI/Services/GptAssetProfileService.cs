using System.Net.Http.Json;
using System.Text.Json;
using ABStock.AI.Models;
using ABStock.Shared;
using ABStock.AI.Internal;

namespace ABStock.AI.Services;

internal sealed class GptAssetProfileService : IAssetProfileService
{
    private readonly HttpClient _httpClient;
    private readonly IEmbeddingService _embeddingService;
    private readonly IProfilePromptBuilder _promptBuilder;

    internal GptAssetProfileService(
        HttpClient httpClient,
        IEmbeddingService embeddingService,
        IProfilePromptBuilder promptBuilder)
    {
        _httpClient = httpClient;
        _embeddingService = embeddingService;
        _promptBuilder = promptBuilder;
    }

    public async Task<AssetProfile> CreateProfileAsync(
        AssetProfileRequest request,
        CancellationToken ct = default)
    {
        var prompt = _promptBuilder.BuildPrompt(request);

        var response =
            await _httpClient.PostAsJsonAsync(
                "http://localhost:8001/generate-profile",
                new { prompt },
                ct);

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content.ReadFromJsonAsync<AssetProfileGenerationResponse>(ct);

        if (result?.Factors == null || result.Factors.Count == 0)
        {
            throw new Exception("Invalid GPT response: empty factors");
        }

        var assetEmbedding =
            await _embeddingService.CreateEmbeddingAsync(
                request.Description,
                ct);

        var scoredFactors = await Task.WhenAll(
            result.Factors.Select(async f =>
            {
                var factorEmbedding =
                    await _embeddingService.CreateEmbeddingAsync(f.Name, ct);

                var similarity =
                    CosineSimilarityHelper.Calculate(
                        assetEmbedding,
                        factorEmbedding);

                return new
                {
                    Factor = f,
                    Similarity = similarity,
                    Embedding = factorEmbedding
                };
            })
        );

        var selectedFactors = scoredFactors
            .Where(x => x.Similarity > 0.75m)
            .OrderByDescending(x => x.Similarity * x.Factor.Importance)
            .Take(20)
            .ToList();

        var factors = new List<AssetFactor>();

        foreach (var item in selectedFactors)
        {
            var f = item.Factor;

            factors.Add(new AssetFactor(
                f.Name,
                f.IsPositive,
                f.Importance,
                item.Embedding
            ));
        }

        return new AssetProfile(
            request.Name,
            request.AssetType,
            request.Description,
            factors,
            0.9m
        );
    }
}