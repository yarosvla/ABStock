using ABStock.Application.Simulation;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Shared formatting and presentation helpers for agent-related pages
/// (Agents dashboard and the per-agent detail page).
/// </summary>
public static class AgentDisplay
{
    public static string GetTypeLabel(AgentType type) => type switch
    {
        AgentType.TrendFollowing => "Трендовый",
        AgentType.CounterTrend => "Контр-тренд",
        AgentType.MarketMaker => "Маркет-мейкер",
        AgentType.NewsDriven => "Новостной",
        _ => type.ToString()
    };

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
    /// Имя экземпляра по внутреннему имени агента. Если состава сессии нет —
    /// на детальную зашли по прямой ссылке при остановленных торгах — показываем
    /// внутреннее имя как есть: пустая строка на месте заголовка страницы хуже
    /// технического имени, а выдумывать номер не из чего.
    /// </summary>
    public static string GetInstanceName(IReadOnlyList<AgentSnapshot> agents, string agentName) =>
        BuildInstanceNames(agents).GetValueOrDefault(agentName, agentName);

    public static string GetTypeClass(AgentType type) => type switch
    {
        AgentType.TrendFollowing => "type-trend",
        AgentType.CounterTrend => "type-counter",
        AgentType.MarketMaker => "type-maker",
        AgentType.NewsDriven => "type-news",
        _ => ""
    };

    public static string GetTypeIcon(AgentType type) => type switch
    {
        AgentType.TrendFollowing => "bi-graph-up-arrow",
        AgentType.CounterTrend => "bi-arrow-down-up",
        AgentType.MarketMaker => "bi-arrows-expand",
        AgentType.NewsDriven => "bi-newspaper",
        _ => "bi-robot"
    };

    public static string GetStrategyDescription(AgentType type) => type switch
    {
        AgentType.TrendFollowing => "Следует за направлением рынка. Покупает при росте цены, продаёт при падении.",
        AgentType.CounterTrend => "Торгует против основного движения. Покупает просадки, продаёт на пиках.",
        AgentType.MarketMaker => "Обеспечивает ликвидность. Одновременно выставляет заявки на покупку и продажу вокруг текущей цены.",
        AgentType.NewsDriven => "Реагирует на внешние новости. Покупает при позитивном сигнале, продаёт при негативном.",
        _ => ""
    };

    public static string GetAssetTypeLabel(AssetType assetType) => assetType switch
    {
        AssetType.Stock => "Акция",
        AssetType.Bond => "Облигация",
        AssetType.Commodity => "Товар",
        AssetType.Crypto => "Криптоактив",
        _ => assetType.ToString()
    };

    public static string GetAssetIconClass(AssetType assetType) => assetType switch
    {
        AssetType.Stock => "bi-graph-up-arrow",
        AssetType.Bond => "bi-receipt",
        AssetType.Commodity => "bi-box-seam",
        AssetType.Crypto => "bi-currency-bitcoin",
        _ => "bi-lightning-charge-fill"
    };

    public static string FormatMoney(decimal value) => $"{value:N2} ₽";

    public static string FormatNullableMoney(decimal? value) => value is null ? "-" : FormatMoney(value.Value);

    public static string FormatPnl(decimal value) => value >= 0 ? $"+{value:N2} ₽" : $"{value:N2} ₽";

    public static string FormatQuantity(decimal value) => value % 1m == 0m
        ? value.ToString("0")
        : value.ToString("0.##");
}
