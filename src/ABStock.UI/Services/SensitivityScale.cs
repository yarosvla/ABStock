namespace ABStock.UI.Services;

/// <summary>
/// Шкала чувствительности актива к новостям: границы, зоны и слово зоны.
///
/// Живёт здесь, а не на странице, потому что шкалу показывают трое —
/// «Создание актива» (полоса связи), «Новости» (правый рельс) и «Торги»
/// (панель актива после остановки), — и все обязаны размечать её одинаково.
/// Раздел 9.17: названия зон стоят по центру своих сегментов, числа границ
/// под делениями; разъехавшаяся разметка означала бы, что на одном экране
/// 0,70 «средняя», а на другом «высокая».
///
/// Диапазон 0,45–0,95 — из бизнес-логики, а не из головы (раздел 10.1).
/// </summary>
public static class SensitivityScale
{
    public const decimal Min = 0.45m;
    public const decimal Max = 0.95m;

    /// <summary>Зоны в порядке возрастания; <c>To</c> — верхняя граница зоны.</summary>
    public static readonly IReadOnlyList<(string Name, decimal To)> Zones =
    [
        ("низкая", 0.60m),
        ("средняя", 0.75m),
        ("высокая", Max)
    ];

    /// <summary>Слово зоны, в которую попало значение.</summary>
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
}
