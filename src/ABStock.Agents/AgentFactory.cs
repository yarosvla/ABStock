using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents;

public sealed class AgentFactory : IAgentFactory
{
    public IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs) =>
        specs.Select<AgentSpec, ITradeAgent>(spec => spec.Type switch
        {
            AgentType.TrendFollowing => new TrendFollowingAgent(spec.InitialCash, spec.InitialPosition),
            AgentType.CounterTrend   => new CounterTrendAgent(spec.InitialCash, spec.InitialPosition),
            AgentType.MarketMaker    => new MarketMakerAgent(spec.InitialCash, spec.InitialPosition),
            AgentType.NewsDriven     => new NewsDrivenAgent(spec.InitialCash, spec.InitialPosition),
            _ => throw new ArgumentOutOfRangeException(nameof(spec.Type))
        }).ToList();
}
