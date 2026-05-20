using ABStock.Shared;

namespace ABStock.Agents.Models;

public class AgentState
{
    public string AgentName { get; set; } = string.Empty;
    public AgentType AgentType { get; set; }
    public decimal Cash { get; set; }
    public int Position { get; set; }

    public decimal GetPortfolioValue(decimal currentPrice) => Cash + Position * currentPrice;
}
