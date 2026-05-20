namespace ABStock.Application.Simulation;

public interface ISimulationRunner
{
    event Action<SimulationTickResult>? OnTick;
    Task StartAsync(SimulationConfig config, CancellationToken ct);
    void SubmitNews(string newsText);
}
