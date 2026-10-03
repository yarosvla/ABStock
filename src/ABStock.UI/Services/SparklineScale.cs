namespace ABStock.UI.Services;

/// <summary>
/// Общая шкала спарклайнов «с открытия» на «Активах» (DESIGN.md 9.25).
///
/// Каждый спарклайн, масштабированный по себе, врёт о размахе: облигация с
/// ходом 0,4 % выглядит такой же бурной, как акция с 2 %. Поэтому все строки
/// рисуются в процентах от своей цены открытия на одной шкале, и пунктир
/// открытия встаёт на одну высоту — общей нулевой линией.
/// </summary>
public static class SparklineScale
{
    /// <summary>Меньше ±0,5 % шкала не бывает: иначе шум в сотые доли процента выглядит обвалом.</summary>
    public const decimal MinHalfRange = 0.5m;

    /// <summary>
    /// Полудиапазон шкалы в процентах: наибольшее отклонение от открытия среди
    /// всех активов, округлённое вверх до 0,5 %.
    /// </summary>
    public static decimal HalfRange(IEnumerable<(decimal Open, IReadOnlyList<decimal> Prices)> series)
    {
        var maxDeviation = series
            .Where(item => item.Open > 0m)
            .SelectMany(item => item.Prices.Select(price => Math.Abs(DeviationPercent(price, item.Open))))
            .DefaultIfEmpty(0m)
            .Max();

        var rounded = Math.Ceiling(maxDeviation * 2m) / 2m;
        return Math.Max(rounded, MinHalfRange);
    }

    public static decimal DeviationPercent(decimal price, decimal open) =>
        open == 0m ? 0m : (price / open - 1m) * 100m;
}
