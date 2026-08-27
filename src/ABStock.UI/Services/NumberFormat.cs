namespace ABStock.UI.Services;

/// <summary>
/// Разрядность по типу величины — таблица раздела 10 DESIGN.md. Разрядность
/// фиксирована и не прыгает внутри колонки, поэтому формат выбирается по
/// смыслу числа, а не по месту на экране.
///
/// Формат «N» берёт групповой разделитель из культуры — узкий неразрывный
/// пробел, он задан в Program.cs: 1 324 500.
/// </summary>
public static class NumberFormat
{
    /// <summary>
    /// Знак у величин, которые могут быть отрицательными. Минус — U+2212,
    /// а не дефис. Ноль пишется без знака.
    /// </summary>
    public static string Sign(decimal value) =>
        value > 0m ? "+" : value < 0m ? "−" : "";

    /// <summary>Деньги агента, капитал, портфель: 0 знаков — 149 864.</summary>
    public static string Money0(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0");

    /// <summary>Объём сессии, число сделок, тиков: 0 знаков.</summary>
    public static string Count0(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("N0");

    /// <summary>Цена, спред, мид: 2 знака — 126,84.</summary>
    public static string Price2(decimal value) => value.ToString("F2");

    /// <summary>P/L: ровно 2 знака со знаком — +31,90, не +31,9.</summary>
    public static string Pnl2(decimal value) =>
        Sign(value) + Math.Abs(value).ToString("N2");

    /// <summary>Позиция: 0 знаков со знаком — +3 900 / −1 200.</summary>
    public static string Position0(decimal value) =>
        Sign(value) + Math.Round(Math.Abs(value), 0, MidpointRounding.AwayFromZero).ToString("N0");

    /// <summary>Проценты: 2 знака со знаком — +2,01 %.</summary>
    public static string Percent2(decimal value) => value.ToString("N2");

    /// <summary>Количество в стакане и ленте: 1 знак — 1,5 · 34,0.</summary>
    public static string Quantity1(decimal value) => value.ToString("N1");

    /// <summary>Класс тона по знаку величины: рынок — единственный носитель цвета.</summary>
    public static string DeltaTone(decimal value) =>
        value > 0m ? "tone-up" : value < 0m ? "tone-down" : "tone-flat";

    /// <summary>Счётная форма: 1 агент · 2 агента · 5 агентов.</summary>
    public static string Plural(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        if (mod100 is >= 11 and <= 14)
        {
            return many;
        }

        return (count % 10) switch
        {
            1 => one,
            2 or 3 or 4 => few,
            _ => many
        };
    }
}
