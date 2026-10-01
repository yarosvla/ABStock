namespace ABStock.Shared;

public sealed record AgentAccountSpec(
    string AgentName,
    AgentType AgentType,
    decimal InitialCash
)
{
    public decimal InitialPosition { get; init; }

    public IReadOnlyDictionary<Guid, decimal> InitialPositions { get; init; } =
        new Dictionary<Guid, decimal>();
}
