namespace ABStock.Shared;

/// <summary>
/// Приведение уровней стакана к цене, которую видит человек.
///
/// Внутренняя точность цены выше отображаемых двух знаков, поэтому без
/// агрегации в стакане появляются несколько строк подряд с одинаковой ценой.
/// Округление — НАРУЖУ от спреда: аски вверх, биды вниз. Арифметическое
/// округление сводит соседние уровни по разные стороны спреда в одну цену,
/// и стакан становится пересечённым: лучший бид равен лучшему аску, спред 0,00.
/// Такого стакана не существует — заявки исполнились бы (DESIGN.md 11).
/// </summary>
public static class OrderBookDisplay
{
    /// <summary>Шаг отображаемой цены: два знака после запятой.</summary>
    public const decimal PriceStep = 0.01m;

    public static decimal ToDisplayPrice(decimal price, bool isAsk)
    {
        var steps = price / PriceStep;
        var rounded = isAsk ? Math.Ceiling(steps) : Math.Floor(steps);
        return rounded * PriceStep;
    }

    /// <summary>
    /// Схлопывает уровни, попавшие в один шаг отображения, суммируя объём
    /// и число заявок. Порядок уровней сохраняется.
    /// </summary>
    public static IReadOnlyList<OrderBookLevel> Aggregate(
        IReadOnlyList<OrderBookLevel> levels,
        bool isAsk) =>
        levels
            .GroupBy(level => ToDisplayPrice(level.Price, isAsk))
            .Select(group => new OrderBookLevel(
                group.Key,
                group.Sum(level => level.Quantity),
                group.Sum(level => level.OrdersCount)))
            .ToArray();
}
