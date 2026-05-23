using ABStock.Shared;

namespace ABStock.Application.Simulation;

public record AgentSnapshot(
    string Name,
    AgentType Type,
    decimal Cash,
    decimal Position,
    decimal PortfolioValue
);

public record SimulationTickResult(
    int Tick,
    MarketSnapshot Snapshot,
    IReadOnlyList<AgentSnapshot> Agents
);
