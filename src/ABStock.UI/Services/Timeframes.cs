namespace ABStock.UI.Services;

/// <summary>
/// Таймфреймы графика — один список на весь продукт.
///
/// Список нужен двум местам: «Торгам», где он рисуется сегментным
/// переключателем над графиком, и «Профилю», где им же выбирается значение
/// по умолчанию. Второй список подписей на «Профиле» разошёлся бы с первым
/// при любой правке — а подписи здесь ещё и участвуют в форматировании оси
/// времени, то есть расхождение вылезло бы не только в настройке.
///
/// Хранится ключ (<see cref="TimeframeOption.Key"/>), рисуется подпись
/// (<see cref="TimeframeOption.Label"/>): подпись — вопрос оформления, ключ —
/// то, по чему сходятся настройка, ряды свечей и запросы к истории.
/// </summary>
public static class Timeframes
{
    /// <summary>
    /// Значение по умолчанию. Ровно то, что стояло в <c>selectedTimeframe</c>
    /// на «Торгах» до появления настройки: человек, который её не трогал,
    /// не должен заметить, что настройка вообще появилась.
    /// </summary>
    public const string DefaultKey = "30s";

    public static readonly IReadOnlyList<TimeframeOption> All =
    [
        new("10s", "10s", TimeSpan.FromSeconds(10)),
        new("30s", "30s", TimeSpan.FromSeconds(30)),
        new("1m", "1m", TimeSpan.FromMinutes(1)),
        new("5m", "5m", TimeSpan.FromMinutes(5)),
        new("15m", "15m", TimeSpan.FromMinutes(15)),
        new("1h", "1h", TimeSpan.FromHours(1))
    ];

    /// <summary>Ключ известен системе. Всё остальное — мусор из хранилища.</summary>
    public static bool IsKnown(string? key) =>
        key is not null && All.Any(option => string.Equals(option.Key, key, StringComparison.Ordinal));

    /// <summary>Ключ, если он известен, иначе значение по умолчанию.</summary>
    public static string Normalize(string? key) => IsKnown(key) ? key! : DefaultKey;
}

/// <param name="Key">То, что хранится и по чему сходятся ряды свечей.</param>
/// <param name="Label">То, что видит человек. Таймфреймы — английские (раздел 17).</param>
/// <param name="Duration">Длина свечи.</param>
public sealed record TimeframeOption(string Key, string Label, TimeSpan Duration);
