using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class MarketMakerAgent : AgentBase
{
    public MarketMakerAgent(decimal initialCash)
        : base("MarketMaker", AgentType.MarketMaker, initialCash) { }

    // TODO: implement market-maker logic using snapshot.BestBid / BestAsk
    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        return HoldDecision("Not implemented yet");
    }
}
