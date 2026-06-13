using ABStock.Shared;

namespace ABStock.Agents.Models;

public class AgentState
{
    public string AgentName { get; set; } = string.Empty;
    public AgentType AgentType { get; set; }
    public decimal Cash { get; set; }
    public decimal Position { get; set; }

    public decimal ReservedCash { get; set; }
    public decimal ReservedPosition { get; set; }

    public decimal AvailableCash => Cash - ReservedCash;
    public decimal AvailablePosition => Position - ReservedPosition;
    
    public decimal GetPortfolioValue(decimal currentPrice) => Cash + Position * currentPrice;
}
