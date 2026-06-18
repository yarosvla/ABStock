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
        AgentType.CounterTrend => "Контртрендовый",
        AgentType.MarketMaker => "Маркетмейкер",
        AgentType.NewsDriven => "Новостной",
        _ => type.ToString()
    };

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
