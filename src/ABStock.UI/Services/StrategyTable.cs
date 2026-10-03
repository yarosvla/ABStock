using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Строка «Агентов» на «Торгах»: стратегия целиком, глазами одного актива
/// (DESIGN.md 16.2). Экземпляры — на «Агентах».
/// </summary>
/// <param name="Position">Бумаг этого актива у стратегии, шт.</param>
/// <param name="PositionChange">Изменение позиции за сессию — со знаком (раздел 10).</param>
/// <param name="Share">Доля актива в портфеле стратегии, %.</param>
/// <param name="Pnl">P/L портфеля стратегии целиком, а не по активу: подписан как портфельный.</param>
/// <param name="Action">«покупает GLEN», «торгуют KVAN», «держит спред», «ждёт» или null.</param>
/// <param name="TradesElsewhere">Стратегия на этом шаге работает в другом активе.</param>
public sealed record StrategyRow(
    AgentType Type,
    int Count,
    decimal Position,
    decimal PositionChange,
    decimal Share,
    decimal Pnl,
    string? Action,
    string? Explanation,
    bool TradesElsewhere);

public sealed record StrategyTotals(decimal Position, decimal PositionChange, decimal Pnl);

/// <summary>
/// Таблица стратегий для одного актива. Чистая функция от счетов и решений
/// последнего шага — правил здесь больше, чем видно на скриншоте: стратегия,
/// которая в этом активе ждёт, а в соседнем покупает, должна сказать именно
/// это, иначе «общие агенты» необъяснимы.
/// </summary>
public static class StrategyTable
{
    public static IReadOnlyList<StrategyRow> Build(
        Guid assetId,
        string symbol,
        decimal price,
        IReadOnlyList<AgentAccountSnapshot> accounts,
        IReadOnlyList<AgentDecision> decisionsHere,
        IReadOnlyDictionary<string, IReadOnlyList<AgentDecision>> decisionsElsewhere)
    {
        var rows = new List<StrategyRow>();

        // Порядок стратегий — как в разделе 16.4, один на всех экранах.
        foreach (var group in accounts.GroupBy(account => account.AgentType).OrderBy(group => group.Key))
        {
            var names = group.Select(account => account.AgentName).ToHashSet(StringComparer.Ordinal);
            var positions = group
                .Select(account => account.Positions.GetValueOrDefault(assetId))
                .Where(position => position is not null)
                .ToArray();

            var position = positions.Sum(item => item!.Quantity);
            var change = positions.Sum(item => item!.Quantity - item.InitialQuantity);
            var portfolio = group.Sum(account => account.PortfolioValue);
            var share = portfolio == 0m ? 0m : position * price / portfolio * 100m;
            var pnl = group.Sum(account => Math.Round(account.PortfolioValue - account.InitialPortfolioValue, 2, MidpointRounding.AwayFromZero));

            var (action, explanation, elsewhere) = Describe(group.Key, symbol, names, decisionsHere, decisionsElsewhere);

            rows.Add(new StrategyRow(
                group.Key,
                group.Count(),
                Math.Round(position, 0, MidpointRounding.AwayFromZero),
                Math.Round(change, 0, MidpointRounding.AwayFromZero),
                Math.Round(share, 1, MidpointRounding.AwayFromZero),
                pnl,
                action,
                explanation,
                elsewhere));
        }

        return rows;
    }

    /// <summary>Итог сходится со строками, которые человек видит (раздел 10).</summary>
    public static StrategyTotals Totals(IReadOnlyList<StrategyRow> rows) =>
        new(rows.Sum(row => row.Position), rows.Sum(row => row.PositionChange), rows.Sum(row => row.Pnl));

    private static (string? Action, string? Explanation, bool Elsewhere) Describe(
        AgentType type,
        string symbol,
        IReadOnlySet<string> names,
        IReadOnlyList<AgentDecision> decisionsHere,
        IReadOnlyDictionary<string, IReadOnlyList<AgentDecision>> decisionsElsewhere)
    {
        var here = decisionsHere.Where(decision => names.Contains(decision.AgentName)).ToArray();
        var trading = here.Where(IsTrade).ToArray();

        if (trading.Length > 0)
        {
            var buys = trading.Count(decision => decision.Action == TradeAction.Buy);
            var verb = buys >= trading.Length - buys ? "покупает" : "продаёт";
            return ($"{verb} {symbol}", CommonExplanation(trading), false);
        }

        // Здесь ждёт — но, может быть, работает в другом активе. Это и есть
        // объяснение общего агента: «торгуют KVAN», а не немое «ждёт».
        foreach (var (otherSymbol, decisions) in decisionsElsewhere)
        {
            var other = decisions.Where(decision => names.Contains(decision.AgentName) && IsTrade(decision)).ToArray();

            if (other.Length > 0)
            {
                return ($"торгуют {otherSymbol}", CommonExplanation(other), true);
            }
        }

        if (here.Length == 0)
        {
            return (null, null, false);
        }

        var holdsBothSides = here.Any(decision =>
            decision.Orders.Any(order => order.Side == OrderSide.Buy)
            && decision.Orders.Any(order => order.Side == OrderSide.Sell));

        return (holdsBothSides || type == AgentType.MarketMaker ? "держит спред" : "ждёт", CommonExplanation(here), false);
    }

    private static bool IsTrade(AgentDecision decision) =>
        decision.Action is TradeAction.Buy or TradeAction.Sell;

    /// <summary>
    /// Объяснение стратегии — самое частое среди её экземпляров. Строка
    /// стратегии одна, и в ней одно объяснение (раздел 9.8).
    /// </summary>
    private static string? CommonExplanation(IEnumerable<AgentDecision> decisions) =>
        decisions
            .Select(decision => decision.Explanation?.Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .GroupBy(text => text, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
}
