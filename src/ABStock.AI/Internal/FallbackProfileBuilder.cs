using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Internal;

/// <summary>
/// Профиль без языковой модели: несколько общих факторов по типу актива и
/// вводным формы. Векторов у факторов нет — их досчитает сопоставитель
/// новостей, если ключ OpenAI появится.
/// </summary>
internal static class FallbackProfileBuilder
{
    public static AssetProfile Build(AssetProfileRequest request)
    {
        var industry = string.IsNullOrWhiteSpace(request.Industry)
            ? "отрасли"
            : $"секторе «{request.Industry.Trim()}»";

        var factors = new List<AssetFactor>
        {
            Factor($"Рост спроса в {industry}", true, 0.7m),
            Factor("Расширение продуктовой линейки и выход на новые рынки", true, 0.5m),
            Factor("Регуляторные ограничения и проверки", false, 0.6m),
            Factor("Операционные задержки и срыв сроков проектов", false, 0.5m)
        };

        if (request.IncludeGovernmentSupport)
        {
            factors.Insert(0, Factor("Государственная поддержка и институциональный спрос", true, 0.8m));
        }

        switch (request.AssetType)
        {
            case AssetType.Stock:
                factors.Add(Factor("Сильная квартальная отчётность", true, 0.6m));
                factors.Add(Factor("Давление на мультипликаторы и стоимость капитала", false, 0.5m));
                break;
            case AssetType.Bond:
                factors.Add(Factor("Снижение ключевой ставки", true, 0.7m));
                factors.Add(Factor("Рост кредитного спреда эмитента", false, 0.7m));
                break;
            case AssetType.Commodity:
                factors.Add(Factor("Сокращение предложения у производителей", true, 0.7m));
                factors.Add(Factor("Падение глобального спроса на сырьё", false, 0.7m));
                break;
            case AssetType.Crypto:
                factors.Add(Factor("Приток институциональных инвесторов", true, 0.6m));
                factors.Add(Factor("Ужесточение регулирования криптовалют", false, 0.8m));
                break;
        }

        return new AssetProfile(
            request.Name,
            request.AssetType,
            request.Description,
            factors,
            CalculateNewsSensitivity(request.AssetType, request.GrowthPotential))
        {
            Source = ProfileSource.Fallback
        };
    }

    /// <summary>
    /// Чувствительность в диапазоне шкалы «Создания актива» (0,45–0,95):
    /// тип актива задаёт положение, потенциал роста сдвигает от середины.
    /// </summary>
    private static decimal CalculateNewsSensitivity(AssetType assetType, int? growthPotential)
    {
        var sensitivity = assetType switch
        {
            AssetType.Stock => 0.62m,
            AssetType.Bond => 0.50m,
            AssetType.Commodity => 0.58m,
            AssetType.Crypto => 0.78m,
            _ => 0.60m
        };

        if (growthPotential is { } potential)
        {
            sensitivity += (Math.Clamp(potential, 0, 100) - 50) / 400m;
        }

        return Math.Clamp(sensitivity, 0.45m, 0.95m);
    }

    private static AssetFactor Factor(string name, bool isPositive, decimal importance) =>
        new(name, isPositive, importance, []);
}
