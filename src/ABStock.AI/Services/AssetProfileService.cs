using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public sealed class AssetProfileService : IAssetProfileService
{
    public AssetProfile CreateProfile(AssetProfileRequest request)
    {
        var normalizedName = string.IsNullOrWhiteSpace(request.Name)
            ? "Новый актив"
            : request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description)
            ? "Описание не предоставлено."
            : request.Description.Trim();
        var normalizedIndustry = string.IsNullOrWhiteSpace(request.Industry)
            ? GetIndustryByAssetType(request.AssetType)
            : request.Industry.Trim();

        var positiveFactors = new List<string>
        {
            $"Спрос в секторе «{normalizedIndustry}»",
            $"Профиль {GetAssetTypeLabel(request.AssetType).ToLowerInvariant()} позволяет использовать сценарии роста через симуляцию"
        };
        var negativeFactors = new List<string>
        {
            "Требуется подтверждение спроса на реальных объёмах",
            "Темп масштабирования зависит от рыночной ликвидности"
        };
        var risks = new List<string>
        {
            "Регуляторный риск",
            "Операционные задержки"
        };

        if (request.IncludeGovernmentSupport)
        {
            positiveFactors.Insert(0, "Государственная поддержка и институциональный спрос");
        }
        else
        {
            negativeFactors.Add("Ограниченная внешняя поддержка");
        }

        if (request.GrowthPotential >= 70)
        {
            positiveFactors.Add("Высокий потенциал роста в среднесрочном горизонте");
        }
        else if (request.GrowthPotential >= 40)
        {
            positiveFactors.Add("Умеренный потенциал роста при стабильном новостном фоне");
        }
        else
        {
            risks.Add("Ограниченный апсайд при текущих вводных");
        }

        switch (request.AssetType)
        {
            case AssetType.Stock:
                positiveFactors.Add("Корпоративные новости быстро отражаются в цене");
                risks.Add("Давление на мультипликаторы и стоимость капитала");
                break;
            case AssetType.Bond:
                positiveFactors.Add("Предсказуемый денежный поток и понятный риск-профиль");
                negativeFactors.Add("Чувствительность к ставкам и кредитному спреду");
                break;
            case AssetType.Commodity:
                positiveFactors.Add("Прямая зависимость от циклов спроса и предложения");
                risks.Add("Высокая чувствительность к глобальной волатильности");
                break;
            case AssetType.Crypto:
                positiveFactors.Add("Повышенная реакция на рыночный импульс и новости");
                risks.Add("Повышенная волатильность и тонкий стакан");
                break;
        }

        var keywords = BuildKeywords(normalizedName, normalizedIndustry, normalizedDescription, request.AssetType, request.IncludeGovernmentSupport);
        var newsSensitivity = CalculateNewsSensitivity(request.AssetType, normalizedIndustry, request.IncludeGovernmentSupport, request.GrowthPotential);

        return new AssetProfile(
            Name: normalizedName,
            AssetType: request.AssetType,
            Description: normalizedDescription,
            PositiveFactors: positiveFactors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NegativeFactors: negativeFactors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Risks: risks.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            NewsSensitivity: newsSensitivity,
            Keywords: keywords
        );
    }

    private static decimal CalculateNewsSensitivity(
        AssetType assetType,
        string industry,
        bool includeGovernmentSupport,
        int growthPotential)
    {
        var baseSensitivity = assetType switch
        {
            AssetType.Stock => 0.62m,
            AssetType.Bond => 0.44m,
            AssetType.Commodity => 0.58m,
            AssetType.Crypto => 0.78m,
            _ => 0.55m
        };

        if (industry.Contains("Энерг", StringComparison.OrdinalIgnoreCase) ||
            industry.Contains("Тех", StringComparison.OrdinalIgnoreCase))
        {
            baseSensitivity += 0.08m;
        }

        if (includeGovernmentSupport)
        {
            baseSensitivity += 0.05m;
        }

        baseSensitivity += Math.Clamp(growthPotential, 0, 100) / 250m;
        return Math.Clamp(baseSensitivity, 0.35m, 0.95m);
    }

    private static IReadOnlyList<string> BuildKeywords(
        string name,
        string industry,
        string description,
        AssetType assetType,
        bool includeGovernmentSupport)
    {
        var keywords = new List<string>
        {
            name,
            industry,
            GetAssetTypeLabel(assetType)
        };

        if (includeGovernmentSupport)
        {
            keywords.Add("господдержка");
        }

        keywords.AddRange(description
            .Split([' ', ',', '.', ':', ';', '(', ')', '\n', '\r', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 5)
            .Take(6));

        return keywords
            .Select(keyword => keyword.Trim())
            .Where(keyword => keyword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetAssetTypeLabel(AssetType assetType) =>
        assetType switch
        {
            AssetType.Stock => "Акция",
            AssetType.Bond => "Облигация",
            AssetType.Commodity => "Товар",
            AssetType.Crypto => "Криптоактив",
            _ => assetType.ToString()
        };

    private static string GetIndustryByAssetType(AssetType assetType) =>
        assetType switch
        {
            AssetType.Stock => "Энергетика",
            AssetType.Bond => "Финансы",
            AssetType.Commodity => "Сырьё",
            AssetType.Crypto => "Технологии",
            _ => "Рынок"
        };
}
