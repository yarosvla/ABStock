namespace ABStock.Shared;

public record AgentSpec(AgentType Type, decimal InitialCash, decimal InitialPosition = 0)
{
    public IReadOnlyDictionary<Guid, decimal> InitialPositions { get; init; } =
        new Dictionary<Guid, decimal>();
}
