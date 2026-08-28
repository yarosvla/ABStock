namespace ABStock.Shared;

/// <summary>
/// Русские названия типов агентов — одна таблица на весь продукт.
///
/// Лежит в Shared, а не в слое интерфейса, потому что нужна двум слоям
/// сразу: кит показывает тип агента на «Агентах», «Торгах» и в детальной, а
/// читатель истории собирает из него строку события сессии. Две таблицы
/// разошлись бы при первом же переименовании — а «Трендовый» на одном экране
/// и «TrendFollowing» на другом это одно и то же приложение, показывающее
/// два разных продукта.
///
/// Внутреннее имя агента совпадает с именем типа («TrendFollowing»,
/// «CounterTrend», «MarketMaker», «NewsDriven») — так его заводит
/// ABStock.Agents и так оно попадает в базу.
/// </summary>
public static class AgentTypeNames
{
    public static string Label(AgentType type) => type switch
    {
        AgentType.TrendFollowing => "Трендовый",
        AgentType.CounterTrend => "Контр-тренд",
        AgentType.MarketMaker => "Маркет-мейкер",
        AgentType.NewsDriven => "Новостной",
        _ => type.ToString()
    };

    /// <summary>
    /// Название по внутреннему имени агента. Незнакомое имя отдаётся как есть:
    /// выдумывать русское название для того, чего мы не знаем, хуже, чем
    /// показать техническую строку.
    /// </summary>
    public static string LabelForAgentName(string agentName) =>
        Enum.TryParse<AgentType>(agentName, ignoreCase: false, out var type)
            ? Label(type)
            : agentName;
}
