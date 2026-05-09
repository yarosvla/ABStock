using ABStock.Agents.Models;

namespace ABStock.Agents.Strategies;

public class MarketMakerAgent : AgentBase
{
    public MarketMakerAgent(decimal initialCash)
        : base("MarketMaker", AgentType.MarketMaker, initialCash) { }

    // TODO
    public override AgentDecision Decide()
    {
        return HoldDecision("No market data available yet");
    }
}
