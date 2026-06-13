using ABStock.Application.Simulation;

namespace ABStock.Application.MarketHistory;

public interface IMarketHistoryStore
{
    Guid StartRun(SimulationConfig config, DateTimeOffset startedAt);

    void SaveTick(Guid runId, SimulationTickResult tickResult, DateTimeOffset capturedAt);
}
