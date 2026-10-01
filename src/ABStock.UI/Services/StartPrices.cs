using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Стартовая цена актива. По контракту (docs/multi-asset.md 2.1) её считает
/// система, человек не вводит; пока бэкенд её не отдаёт, расчёт живёт здесь —
/// тот же, что раньше стоял внутри «Торгов». Переехал оттуда, потому что
/// цена нужна до запуска: «Активы», свитчер и профиль актива показывают её
/// как «стартовую», а «Торги» больше не единственное место, где актив
/// впервые встречается с рынком.
/// </summary>
public static class StartPrices
{
    public static decimal Calculate(AssetDraft draft, AssetProfile profile)
    {
        var basePrice = draft.AssetType switch
        {
            AssetType.Stock => 96m,
            AssetType.Bond => 101m,
            AssetType.Commodity => 88m,
            AssetType.Crypto => 72m,
            _ => 96m
        };

        var adjusted = basePrice
            + draft.GrowthPotential * 0.42m
            + (draft.IncludeGovernmentSupport ? 4m : 0m)
            + profile.NewsSensitivity * 10m;

        // Шаг цены 0,05: стартовая цена выглядит как биржевая котировка, а не
        // как результат деления.
        return Math.Round(adjusted / 0.05m, MidpointRounding.AwayFromZero) * 0.05m;
    }
}
