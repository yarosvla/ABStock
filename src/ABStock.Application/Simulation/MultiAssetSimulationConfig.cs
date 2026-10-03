using ABStock.Shared;

namespace ABStock.Application.Simulation;

public sealed record MultiAssetSimulationConfig(
    IReadOnlyList<Guid> AssetIds,
    TimeSpan TickInterval,
    IReadOnlyList<AgentSpec> Agents
);
