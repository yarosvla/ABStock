using ABStock.Shared;

namespace ABStock.Application.Simulation;

public record SimulationConfig(
    string AssetName,
    string AssetDescription,
    AssetType AssetType,
    decimal StartPrice,
    TimeSpan TickInterval,
    IReadOnlyList<AgentSpec> Agents
);
