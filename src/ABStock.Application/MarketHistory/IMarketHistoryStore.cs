using ABStock.Application.Simulation;

namespace ABStock.Application.MarketHistory;

public interface IMarketHistoryStore
{
    void StartSession(Guid sessionId, TimeSpan tickInterval, DateTimeOffset startedAt) { }

    void EndSession(Guid sessionId, DateTimeOffset endedAt) { }

    Guid StartRun(SimulationConfig config, DateTimeOffset startedAt);

    void SaveTick(Guid runId, SimulationTickResult tickResult, DateTimeOffset capturedAt);
}
