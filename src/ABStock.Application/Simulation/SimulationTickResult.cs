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
);
