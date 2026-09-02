using ABStock.Application.Simulation;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Названия и тона агентов — одни на «Агентов», детальную агента и «Торги».
/// Форматирование чисел живёт в <see cref="NumberFormat"/>: разрядность
/// задаётся типом величины, а не тем, на какой она странице.
/// </summary>
public static class AgentDisplay
{
    /// <summary>
    /// Названия живут в ABStock.Shared: их показывает не только интерфейс, но
    /// и читатель истории, который собирает из них строку события сессии.
    /// Пока таблиц было две, «Профиль» показывал «TrendFollowing» там, где
    /// «Агенты» показывали «Трендовый».
    /// </summary>
    public static string GetTypeLabel(AgentType type) => AgentTypeNames.Label(type);

    /// <summary>
    /// Тон типа агента для StatusDot и подсветки строк. Цвет типа один и тот
    /// же везде: легенда, таблица, лента, маркеры на графике (раздел 3).
    /// </summary>
    public static string GetTypeTone(AgentType type) => type switch
    {
        AgentType.TrendFollowing => "agent-trend",
        AgentType.CounterTrend => "agent-counter",
        AgentType.MarketMaker => "agent-mm",
        AgentType.NewsDriven => "agent-news",
        _ => "muted"
    };

    /// <summary>Стратегия одной строкой — для чипа рядом с именем экземпляра.</summary>
    public static string GetStrategyKind(AgentType type) => type switch
    {
        AgentType.TrendFollowing => "трендовая стратегия",
        AgentType.CounterTrend => "контр-трендовая стратегия",
        AgentType.MarketMaker => "маркет-мейкинг",
        AgentType.NewsDriven => "новостная стратегия",
        _ => "стратегия"
    };

    /// <summary>
    /// Человеческие имена экземпляров: «Трендовый 1», «Трендовый 2».
    /// Ключ — внутреннее имя агента (по нему строится ссылка на детальную),
    /// значение — то, что видит человек.
    ///
    /// Нумерация одна на весь продукт и живёт здесь, а не в двух страницах:
    /// иначе список и детальная называли бы один и тот же экземпляр разными
    /// номерами. Порядок — типы по порядку AgentType, внутри типа порядок
    /// снимка, тот же, что в таблице.
    /// </summary>
    public static IReadOnlyDictionary<string, string> BuildInstanceNames(
        IReadOnlyList<AgentSnapshot> agents)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var group in agents.GroupBy(agent => agent.Type).OrderBy(group => group.Key))
        {
            var label = GetTypeLabel(group.Key);
            var number = 1;

            foreach (var agent in group)
            {
                names[agent.Name] = $"{label} {number}";
                number++;
            }
        }

        return names;
    }

    /// <summary>
    /// Имя экземпляра по внутреннему имени агента. Когда состава сессии нет —
    /// на детальную зашли при остановленных торгах — имя выводится из самого
    /// внутреннего имени, а не показывается технической строкой.
    ///
    /// Раньше здесь стоял возврат внутреннего имени как есть, и заголовок
    /// страницы, крошки и вкладка браузера показывали «TrendFollowing»
    /// латиницей (пункт 88 docs/ui-backlog.md). Довод «выдумывать номер не из
    /// чего» оказался неверен: номер выводится. Раннер даёт экземплярам одного
    /// типа имена «TrendFollowing», «TrendFollowing2», «TrendFollowing3»
    /// (SimulationRunner.GetUniqueAgentName), а BuildInstanceNames нумерует их
    /// в том же порядке начиная с единицы — суффикс и номер совпадают.
    /// </summary>
    public static string GetInstanceName(IReadOnlyList<AgentSnapshot> agents, string agentName) =>
        BuildInstanceNames(agents).GetValueOrDefault(agentName)
        ?? DeriveInstanceName(agentName);

    /// <summary>
    /// «TrendFollowing3» → «Трендовый 3», «TrendFollowing» → «Трендовый 1».
    /// Имя, из которого тип не выводится, отдаётся как есть: выдумывать
    /// русское название для того, чего мы не знаем, хуже технической строки.
    /// </summary>
    private static string DeriveInstanceName(string agentName)
    {
        var digits = agentName.Length;
        while (digits > 0 && char.IsAsciiDigit(agentName[digits - 1]))
        {
            digits--;
        }

        var baseName = agentName[..digits];

        if (!Enum.TryParse<AgentType>(baseName, ignoreCase: false, out var type))
        {
            return agentName;
        }

        var suffix = agentName[digits..];
        var number = suffix.Length == 0 ? 1 : int.Parse(suffix);

        return $"{GetTypeLabel(type)} {number}";
    }

    public static string GetAssetTypeLabel(AssetType assetType) => assetType switch
    {
        AssetType.Stock => "Акция",
        AssetType.Bond => "Облигация",
        AssetType.Commodity => "Товар",
        AssetType.Crypto => "Криптовалюта",
        _ => assetType.ToString()
    };
}
