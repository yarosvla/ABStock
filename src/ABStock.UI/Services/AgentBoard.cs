using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>Рынок глазами «Агентов»: тикер, цена и решения агентов на последнем шаге.</summary>
public sealed record BoardMarket(Guid AssetId, string Symbol, decimal Price, IReadOnlyList<AgentDecision> Decisions);

/// <summary>Изменение позиции в активе за сессию — со знаком (раздел 10).</summary>
public sealed record PositionChange(string Symbol, decimal Change);

/// <param name="AgentName">Внутреннее имя — по нему строится ссылка на детальную.</param>
/// <param name="Title">«Трендовый 1» — нумерация внутри типа, одна на весь продукт.</param>
public sealed record BoardInstance(
    string AgentName,
    string Title,
    decimal Cash,
    decimal Portfolio,
    decimal Pnl,
    IReadOnlyList<PositionChange> Changes);

/// <summary>
/// Стратегия целиком: суммы экземпляров и одно объяснение на строку — у
/// экземпляров его нет, иначе одинаковый текст стоял бы в колонке подряд
/// (раздел 9.8).
/// </summary>
public sealed record BoardGroup(
    AgentType Type,
    IReadOnlyList<BoardInstance> Instances,
    decimal Cash,
    decimal Portfolio,
    decimal Pnl,
    IReadOnlyList<PositionChange> Changes,
    string? Action,
    string? Explanation);

/// <param name="Share">Доля актива в бумагах стратегии, %: строка ячеек складывается в 100.</param>
public sealed record HoldingCell(string Symbol, decimal Share, decimal Quantity);

/// <param name="InSecurities">Доля бумаг в портфеле стратегии, % — свой знаменатель, отдельная колонка.</param>
public sealed record HoldingRow(AgentType Type, IReadOnlyList<HoldingCell> Cells, decimal InSecurities);

/// <param name="InitialPortfolio">
/// Стоимость портфелей на старте: деньги плюс стартовые бумаги по стартовым
/// ценам. Без неё «деньги 900 000 + P/L» не сходятся со стоимостью портфелей.
/// </param>
public sealed record AgentBoardView(
    IReadOnlyList<BoardGroup> Groups,
    IReadOnlyList<HoldingRow> Holdings,
    int AgentCount,
    decimal Cash,
    decimal Portfolio,
    decimal InitialPortfolio,
    decimal Pnl,
    decimal InSecurities,
    IReadOnlyList<PositionChange> Changes)
{
    public static readonly AgentBoardView Empty = new([], [], 0, 0m, 0m, 0m, 0m, 0m, []);
}

/// <summary>
/// Таблица, матрица и итоги «Агентов», посчитанные разом из общих счетов:
/// строка показателей обязана сходиться с таблицей под ней (раздел 10.1), а
/// матрица — с позициями в таблице.
/// </summary>
public static class AgentBoard
{
    public static AgentBoardView Build(IReadOnlyList<AgentAccountSnapshot> accounts, IReadOnlyList<BoardMarket> markets)
    {
        if (accounts.Count == 0)
        {
            return AgentBoardView.Empty;
        }

        var groups = new List<BoardGroup>();
        var holdings = new List<HoldingRow>();

        // Порядок стратегий — раздела 16.4, внутри стратегии — порядок счетов.
        foreach (var group in accounts.GroupBy(account => account.AgentType).OrderBy(group => group.Key))
        {
            var label = AgentDisplay.GetTypeLabel(group.Key);
            var instances = group
                .Select((account, index) => new BoardInstance(
                    account.AgentName,
                    $"{label} {index + 1}",
                    Round0(account.Cash),
                    Round0(account.PortfolioValue),
                    Round2(account.PortfolioValue - account.InitialPortfolioValue),
                    Changes([account], markets)))
                .ToArray();

            var (action, explanation) = Describe(group.Key, group.Select(account => account.AgentName).ToHashSet(StringComparer.Ordinal), markets);

            groups.Add(new BoardGroup(
                group.Key,
                instances,
                instances.Sum(item => item.Cash),
                instances.Sum(item => item.Portfolio),
                instances.Sum(item => item.Pnl),
                Changes(group.ToArray(), markets),
                action,
                explanation));

            holdings.Add(Holding(group.Key, group.ToArray(), markets));
        }

        var portfolio = groups.Sum(group => group.Portfolio);
        var securities = accounts.Sum(account => SecuritiesValue(account, markets));

        return new AgentBoardView(
            groups,
            holdings,
            accounts.Count,
            groups.Sum(group => group.Cash),
            portfolio,
            Round0(accounts.Sum(account => account.InitialPortfolioValue)),
            groups.Sum(group => group.Pnl),
            portfolio == 0m ? 0m : Round1(securities / portfolio * 100m),
            Changes(accounts, markets));
    }

    /// <summary>Изменение позиций за сессию по каждому активу — от стартового запаса.</summary>
    private static IReadOnlyList<PositionChange> Changes(IReadOnlyList<AgentAccountSnapshot> accounts, IReadOnlyList<BoardMarket> markets) =>
        markets
            .Select(market => new PositionChange(
                market.Symbol,
                Round0(accounts.Sum(account => account.Positions.TryGetValue(market.AssetId, out var position)
                    ? position.Quantity - position.InitialQuantity
                    : 0m))))
            .ToArray();

    private static HoldingRow Holding(AgentType type, IReadOnlyList<AgentAccountSnapshot> accounts, IReadOnlyList<BoardMarket> markets)
    {
        var values = markets
            .Select(market =>
            {
                var quantity = accounts.Sum(account => account.Positions.GetValueOrDefault(market.AssetId)?.Quantity ?? 0m);
                return (market.Symbol, Quantity: quantity, Value: quantity * market.Price);
            })
            .ToArray();

        var securities = values.Sum(item => item.Value);
        var portfolio = accounts.Sum(account => account.PortfolioValue);

        return new HoldingRow(
            type,
            values.Select(item => new HoldingCell(
                item.Symbol,
                securities == 0m ? 0m : Round0(item.Value / securities * 100m),
                Round0(item.Quantity))).ToArray(),
            portfolio == 0m ? 0m : Round1(securities / portfolio * 100m));
    }

    private static decimal SecuritiesValue(AgentAccountSnapshot account, IReadOnlyList<BoardMarket> markets) =>
        markets.Sum(market => (account.Positions.GetValueOrDefault(market.AssetId)?.Quantity ?? 0m) * market.Price);

    /// <summary>
    /// Действие стратегии на последнем шаге: актив, где её экземпляры чаще
    /// всего покупали или продавали. Общий агент торгует несколькими активами
    /// сразу, и строка называет тот, где он действовал, — «покупают GLEN».
    /// </summary>
    private static (string? Action, string? Explanation) Describe(AgentType type, IReadOnlySet<string> names, IReadOnlyList<BoardMarket> markets)
    {
        var busiest = markets
            .Select(market => (market.Symbol, Trades: market.Decisions
                .Where(decision => names.Contains(decision.AgentName) && decision.Action is TradeAction.Buy or TradeAction.Sell)
                .ToArray()))
            .Where(item => item.Trades.Length > 0)
            .OrderByDescending(item => item.Trades.Length)
            .FirstOrDefault();

        if (busiest.Trades is { Length: > 0 } trades)
        {
            var buys = trades.Count(decision => decision.Action == TradeAction.Buy);
            var verb = buys >= trades.Length - buys ? "покупают" : "продают";
            return ($"{verb} {busiest.Symbol}", Common(trades));
        }

        var waiting = markets.SelectMany(market => market.Decisions).Where(decision => names.Contains(decision.AgentName)).ToArray();

        if (waiting.Length == 0)
        {
            return (null, null);
        }

        return (type == AgentType.MarketMaker ? "держат спред" : "ждут", Common(waiting));
    }

    private static string? Common(IEnumerable<AgentDecision> decisions) =>
        decisions
            .Select(decision => decision.Explanation?.Trim())
            .Where(text => !string.IsNullOrEmpty(text))
            .GroupBy(text => text, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();

    private static decimal Round0(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static decimal Round1(decimal value) => Math.Round(value, 1, MidpointRounding.AwayFromZero);

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
