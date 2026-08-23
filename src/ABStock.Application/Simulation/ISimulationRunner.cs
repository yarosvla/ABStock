using ABStock.Shared;

namespace ABStock.Application.Simulation;

public interface ISimulationRunner
{
    event Action<SimulationTickResult>? OnTick;

    /// <summary>
    /// Торги запущены или остановлены. Без этого события подписчики узнают о
    /// состоянии только из тиков, а после остановки тики прекращаются — и,
    /// например, статус в шапке навсегда остаётся «Торги активны».
    /// </summary>
    event Action? OnStateChanged;

    SimulationTickResult? Current { get; }

    Guid CurrentRunId { get; }

    bool IsRunning { get; }

    Task StartAsync(SimulationConfig config, CancellationToken ct = default);

    Task StopAsync();

    void SubmitNews(NewsSignal signal);
}
