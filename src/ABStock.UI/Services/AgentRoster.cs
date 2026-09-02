using ABStock.Application.Simulation;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>Строка таблицы: агрегат по типу или отдельный экземпляр.</summary>
public enum AgentRowKind
{
    Group,
    Instance
}

/// <summary>
/// Строка таблицы «Агенты в сессии». Числа лежат уже округлёнными до
/// отображаемой разрядности (раздел 10): суммы строки группы считаются по
/// тому, что человек видит на экране, иначе итог не сходится со слагаемыми.
/// </summary>
/// <param name="AgentName">
/// Внутреннее имя агента — только у экземпляра. По нему строится ссылка на
/// детальную; в интерфейсе оно не показывается.
/// </param>
/// <param name="ShowTypeDot">
/// Точка цвета типа. Стоит в строке группы, а у типа с единственным
/// экземпляром — в самой строке экземпляра, чтобы связь с линией графика
/// не потерялась (раздел 9.8).
/// </param>
/// <param name="Action">Слово действия или null, если действие неизвестно.</param>
/// <param name="Explanation">Объяснение агента дословно или null — тогда «—».</param>
public sealed record AgentRow(
    AgentRowKind Kind,
    AgentType Type,
    string Title,
    string? AgentName,
    bool ShowTypeDot,
    decimal Cash,
    decimal Position,
    decimal Portfolio,
    decimal Pnl,
    string? Action,
    string? Explanation);

/// <summary>
/// Таблица и показатели страницы «Агенты», посчитанные разом: строка
/// показателей обязана сходиться с таблицей под ней (раздел 10.1).
/// </summary>
/// <param name="TradingCount">
/// Сколько агентов торговали за сессию. Признак — деньги, отличающиеся от
/// стартовых: они меняются от любой исполненной сделки.
/// </param>
public sealed record AgentRosterView(
    IReadOnlyList<AgentRow> Rows,
    decimal TotalCash,
    decimal TotalPosition,
    decimal TotalPortfolio,
    decimal TotalPnl,
    int AgentCount,
    int TypeCount,
    int TradingCount);

/// <summary>
/// Построение таблицы агентов из снимка тика. Вынесено из разметки в чистый
/// метод, потому что правил здесь больше, чем видно глазом на скриншоте:
/// группа из одного экземпляра, дословно совпадающее объяснение и нумерация
/// экземпляров внутри типа.
/// </summary>
public static class AgentRoster
{
    public static AgentRosterView Build(
        IReadOnlyList<AgentSnapshot> agents,
        IReadOnlyList<AgentDecision> decisions)
    {
        if (agents.Count == 0)
        {
            return new AgentRosterView([], 0m, 0m, 0m, 0m, 0, 0, 0);
        }

        // Решение на агента: за тик оно одно, но подстрахуемся от повтора имени.
        var decisionByAgent = decisions
            .GroupBy(decision => decision.AgentName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

        var rows = new List<AgentRow>();
        decimal totalCash = 0m, totalPosition = 0m, totalPortfolio = 0m, totalPnl = 0m;

        // Типы — в порядке объявления AgentType, внутри типа — порядок снимка.
        var groups = agents
            .GroupBy(agent => agent.Type)
            .OrderBy(group => group.Key);

        foreach (var group in groups)
        {
            var instances = group.ToArray();
            var typeLabel = AgentDisplay.GetTypeLabel(group.Key);

            var cells = instances
                .Select((agent, index) => new
                {
                    Agent = agent,
                    Title = $"{typeLabel} {index + 1}",
                    Cash = Round0(agent.Cash),
                    Position = Round0(agent.Position),
                    Portfolio = Round0(agent.PortfolioValue),
                    Pnl = Round2(agent.PortfolioValue - agent.InitialPortfolioValue),
                    Decision = decisionByAgent.GetValueOrDefault(agent.Name)
                })
                .ToArray();

            totalCash += cells.Sum(cell => cell.Cash);
            totalPosition += cells.Sum(cell => cell.Position);
            totalPortfolio += cells.Sum(cell => cell.Portfolio);
            totalPnl += cells.Sum(cell => cell.Pnl);

            // Объяснение, дословно совпавшее у всех экземпляров, перестаёт быть
            // свойством экземпляра и становится свойством группы (раздел 9.8).
            var sharedExplanation = GetSharedExplanation(cells.Select(cell => cell.Decision));
            var hasGroupRow = instances.Length > 1;

            if (hasGroupRow)
            {
                rows.Add(new AgentRow(
                    AgentRowKind.Group,
                    group.Key,
                    $"{typeLabel} ×{instances.Length}",
                    AgentName: null,
                    ShowTypeDot: true,
                    cells.Sum(cell => cell.Cash),
                    cells.Sum(cell => cell.Position),
                    cells.Sum(cell => cell.Portfolio),
                    cells.Sum(cell => cell.Pnl),
                    // Слова действия в строке группы нет: единственный текст,
                    // допустимый в её текстовой колонке, — общее объяснение.
                    Action: null,
                    Explanation: sharedExplanation));
            }

            foreach (var cell in cells)
            {
                var explanation = Normalize(cell.Decision?.Explanation);

                rows.Add(new AgentRow(
                    AgentRowKind.Instance,
                    group.Key,
                    cell.Title,
                    cell.Agent.Name,
                    // Единственный экземпляр типа несёт точку сам: строки
                    // группы, которая обычно её несёт, у него нет.
                    ShowTypeDot: !hasGroupRow,
                    cell.Cash,
                    cell.Position,
                    cell.Portfolio,
                    cell.Pnl,
                    sharedExplanation is null ? GetActionLabel(cell.Decision) : null,
                    sharedExplanation is null ? explanation : null));
            }
        }

        return new AgentRosterView(
            rows,
            totalCash,
            totalPosition,
            totalPortfolio,
            totalPnl,
            agents.Count,
            agents.Select(agent => agent.Type).Distinct().Count(),
            agents.Count(agent => agent.Cash != agent.InitialCash));
    }

    /// <summary>
    /// Общее объяснение группы: непустой текст, дословно совпавший у всех
    /// экземпляров. Иначе null — объяснение остаётся в строках экземпляров.
    /// Группа из одного экземпляра общего объяснения не имеет: дублировать
    /// там нечего.
    /// </summary>
    private static string? GetSharedExplanation(IEnumerable<AgentDecision?> decisions)
    {
        var texts = decisions.Select(decision => Normalize(decision?.Explanation)).ToArray();

        if (texts.Length < 2 || texts[0] is null)
        {
            return null;
        }

        return texts.All(text => string.Equals(text, texts[0], StringComparison.Ordinal))
            ? texts[0]
            : null;
    }

    /// <summary>
    /// Действие словами. Констатация, а не кнопка, поэтому не инфинитив.
    /// Hold с заявками по обе стороны стакана — это маркет-мейкер, который
    /// держит спред; всякий другой Hold — ожидание.
    /// </summary>
    private static string? GetActionLabel(AgentDecision? decision) => decision?.Action switch
    {
        TradeAction.Buy => "покупает",
        TradeAction.Sell => "продаёт",
        TradeAction.Hold when HoldsBothSides(decision) => "держит спред",
        TradeAction.Hold => "ждёт",
        _ => null
    };

    private static bool HoldsBothSides(AgentDecision decision) =>
        decision.Orders.Any(order => order.Side == OrderSide.Buy)
        && decision.Orders.Any(order => order.Side == OrderSide.Sell);

    /// <summary>Пустой текст объяснения — это отсутствие данных, то есть «—».</summary>
    private static string? Normalize(string? explanation) =>
        string.IsNullOrWhiteSpace(explanation) ? null : explanation.Trim();

    /// <summary>Деньги, позиция, портфель — 0 знаков (раздел 10).</summary>
    private static decimal Round0(decimal value) =>
        Math.Round(value, 0, MidpointRounding.AwayFromZero);

    /// <summary>P/L — ровно 2 знака (раздел 10).</summary>
    private static decimal Round2(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
