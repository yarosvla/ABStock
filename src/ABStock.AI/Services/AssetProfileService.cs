using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public sealed class AssetProfileService : IAssetProfileService
{
    public AssetProfile CreateProfile(AssetProfileRequest request)
    {
        // TODO
        return new AssetProfile(
            Name: request.Name,
            AssetType: request.AssetType,
            Description: request.Description,
            PositiveFactors: ["рост спроса на AI-сервисы", "крупные контракты"],
            NegativeFactors: ["регуляторные ограничения", "дефицит чипов"],
            Risks: ["высокая конкуренция", "зависимость от поставщиков"],
            NewsSensitivity: 0.9m,
            Keywords: []
        );
    }
}