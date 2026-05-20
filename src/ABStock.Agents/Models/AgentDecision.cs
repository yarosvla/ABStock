namespace ABStock.Agents.Models;

public class AgentDecision
{
    public TradeAction Action { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public List<TradeProposal> Proposals { get; set; } = new();
}
