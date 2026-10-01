namespace ABStock.UI.Services;

/// <summary>
/// Шкала силы влияния новости (ImpactScore): границы, зоны и слово зоны.
///
/// Раньше жила внутри «Новостей». С веером её показывают четверо — колонка
/// «Новость» на «Активах», веер и разбор на «Новостях», полоса связи на
/// «Торгах» — и разметка обязана быть одной: 0,85 не может быть «слабой» на
/// одном экране и «умеренной» на другом.
///
/// Диапазон ≈ 0,3–2,5 — из бизнес-логики (DESIGN.md 10.1).
/// </summary>
public static class ImpactScale
{
    public const decimal Min = 0.30m;
    public const decimal Max = 2.50m;

    public static readonly IReadOnlyList<(string Name, decimal To)> Zones =
    [
        ("слабая", 0.90m),
        ("умеренная", 1.60m),
        ("сильная", Max)
    ];

    public static string Word(decimal value)
    {
        foreach (var zone in Zones)
        {
            if (value < zone.To)
            {
                return zone.Name;
            }
        }

        return Zones[^1].Name;
    }

    /// <summary>
    /// Значение ниже начала шкалы. Заглушки анализаторов дают около 0,2 —
    /// это не «ноль», и подпись «&lt; 0,30» говорит об этом честно, а не
    /// прижимает маркер к краю молча.
    /// </summary>
    public static bool IsBelow(decimal value) => value < Min;
}
