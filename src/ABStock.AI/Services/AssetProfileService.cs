using ABStock.AI.Internal;
using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public sealed class AssetProfileService : IAssetProfileService
{
    private readonly IEmbeddingService _embeddingService =
        new StubEmbeddingService();

    public async Task<AssetProfile> CreateProfileAsync(
        AssetProfileRequest request,
        CancellationToken ct = default)
    {
        var factors = new List<AssetFactor>
        {
            new(
                "рост спроса на AI-сервисы",
                true,
                0.9m,
                await _embeddingService.CreateEmbeddingAsync(
                    "рост спроса на AI-сервисы",
                    ct)),

            new(
                "крупные корпоративные контракты",
                true,
                0.8m,
                await _embeddingService.CreateEmbeddingAsync(
                    "крупные корпоративные контракты",
                    ct)),

            new(
                "регуляторные ограничения",
                false,
                0.9m,
                await _embeddingService.CreateEmbeddingAsync(
                    "регуляторные ограничения",
                    ct)),

            new(
                "дефицит GPU",
                false,
                0.7m,
                await _embeddingService.CreateEmbeddingAsync(
                    "дефицит GPU",
                    ct))
        };

        return new AssetProfile(
            Name: request.Name,
            AssetType: request.AssetType,
            Description: request.Description,
            Factors: factors,
            NewsSensitivity: 0.9m
        );
    }
}