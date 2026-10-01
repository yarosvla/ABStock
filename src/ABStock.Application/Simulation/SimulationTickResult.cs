using ABStock.Shared;

namespace ABStock.Application.Simulation;

public record AgentSnapshot(
    string Name,
    AgentType Type,
    decimal Cash,
    decimal Position,
    decimal PortfolioValue,
    decimal InitialCash,
    decimal InitialPortfolioValue
);

public record SimulationTickResult(
    int Tick,
    MarketSnapshot Snapshot,
    OrderBookSnapshot OrderBook,
    IReadOnlyList<AgentSnapshot> Agents,
    IReadOnlyList<AgentDecision> Decisions
)
{
    public Guid SessionId { get; init; }

    public Guid AssetId { get; init; }

    public Guid RunId { get; init; }

    public IReadOnlyList<AgentAccountSnapshot> Accounts { get; init; } = [];

    public SubmitResult? Submission { get; init; }
}
