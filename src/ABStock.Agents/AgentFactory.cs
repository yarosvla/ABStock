using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents;

public sealed class AgentFactory
{
    public IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs) =>
        specs.Select<AgentSpec, ITradeAgent>(spec => spec.Type switch
        {
            AgentType.TrendFollowing => new TrendFollowingAgent(spec.InitialCash),
            AgentType.CounterTrend   => new CounterTrendAgent(spec.InitialCash),
            AgentType.MarketMaker    => new MarketMakerAgent(spec.InitialCash),
            AgentType.NewsDriven     => new NewsDrivenAgent(spec.InitialCash),
            _ => throw new ArgumentOutOfRangeException(nameof(spec.Type))
        }).ToList();
}