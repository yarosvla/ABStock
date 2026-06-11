using ABStock.Shared;

namespace ABStock.Application.Simulation;

public interface ISimulationRunner
{
    event Action<SimulationTickResult>? OnTick;

    SimulationTickResult? Current { get; }

    Guid CurrentRunId { get; }

    bool IsRunning { get; }

    Task StartAsync(SimulationConfig config, CancellationToken ct = default);

    Task StopAsync();

    void SubmitNews(NewsSignal signal);
}
