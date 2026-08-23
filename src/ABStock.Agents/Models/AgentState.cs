using ABStock.Shared;

namespace ABStock.Agents.Models;

public class AgentState
{
    public string AgentName { get; set; } = string.Empty;
    public AgentType AgentType { get; set; }
    public decimal Cash { get; set; }
    public decimal Position { get; set; }

    /// <summary>Деньги агента на входе в сессию.</summary>
    public decimal InitialCash { get; set; }
    public decimal InitialPosition { get; set; }

    /// <summary>
    /// Стоимость портфеля на входе — база P/L. Считается по цене того момента,
    /// когда агент вошёл в сессию: стартовая позиция уже чего-то стоит,
    /// и её рыночная оценка не является заработком агента.
    /// </summary>
    public decimal InitialPortfolioValue { get; set; }

    public decimal ReservedCash { get; set; }
    public decimal ReservedPosition { get; set; }

    public decimal AvailableCash => Cash - ReservedCash;
    public decimal AvailablePosition => Position - ReservedPosition;
    
    public decimal GetPortfolioValue(decimal currentPrice) => Cash + Position * currentPrice;
}
