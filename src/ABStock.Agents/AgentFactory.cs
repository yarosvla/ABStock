using ABStock.Agents.Strategies;
using ABStock.Shared;

namespace ABStock.Agents;

public sealed class AgentFactory : IAgentFactory
{
    /// <summary>
    /// У каждого экземпляра свой генератор с зерном от позиции в составе:
    /// одинаковые агенты, которые на каждый сигнал реагируют разом, превращают
    /// цену в качели между двумя уровнями. Зерно фиксировано — сессия с тем же
    /// составом воспроизводима.
    /// </summary>
    public IReadOnlyList<ITradeAgent> Create(IReadOnlyList<AgentSpec> specs) =>
        specs.Select<AgentSpec, ITradeAgent>((spec, index) => spec.Type switch
        {
            AgentType.TrendFollowing => new TrendFollowingAgent(spec.InitialCash, spec.InitialPosition, random: Seeded(index)),
            AgentType.CounterTrend   => new CounterTrendAgent(spec.InitialCash, spec.InitialPosition, random: Seeded(index)),
            AgentType.MarketMaker    => new MarketMakerAgent(spec.InitialCash, spec.InitialPosition),
            AgentType.NewsDriven     => new NewsDrivenAgent(spec.InitialCash, spec.InitialPosition),
            _ => throw new ArgumentOutOfRangeException(nameof(spec.Type))
        }).ToList();

    private static Random Seeded(int index) => new(7919 * (index + 1));
}
