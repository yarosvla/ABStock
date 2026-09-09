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

    /// <summary>
    /// Название актива, которым торгует идущий прогон, — из конфигурации, с
    /// которой его запустили. Когда торги не идут — <see langword="null"/>,
    /// не пустая строка и не имя прошлого прогона.
    /// </summary>
    /// <remarks>
    /// Знание принадлежит раннеру, а не контексту интерфейса: контекст актива
    /// живёт в скоупе контура, а приветственная страница отрисовывается
    /// статически, в скоупе запроса, где его уже нет. Читается тем же замком
    /// и тем же условием, что <see cref="IsRunning"/>, поэтому разойтись с
    /// ним не может.
    /// </remarks>
    string? CurrentAssetName { get; }

    Task StartAsync(SimulationConfig config, CancellationToken ct = default);

    Task StopAsync();

    void SubmitNews(NewsSignal signal);
}
