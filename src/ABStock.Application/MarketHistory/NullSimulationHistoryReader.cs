namespace ABStock.Application.MarketHistory;

internal sealed class NullSimulationHistoryReader : ISimulationHistoryReader
{
    public SimulationRunSummary? GetRun(Guid runId) => null;

    public SimulationHistoryOverview GetOverview(int recentRuns = 10) =>
        new(0, 0, 0, 0, []);
}
