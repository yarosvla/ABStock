using ABStock.Application.Simulation;

namespace ABStock.Application.MarketHistory;

internal sealed class NullMarketHistoryStore : IMarketHistoryStore
{
    public Guid StartRun(SimulationConfig config, DateTimeOffset startedAt) => Guid.Empty;

    public void SaveTick(Guid runId, SimulationTickResult tickResult, DateTimeOffset capturedAt)
    {
    }
}
