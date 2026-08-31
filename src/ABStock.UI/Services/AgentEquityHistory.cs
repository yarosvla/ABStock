using ABStock.Application.Simulation;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Что «Агенты» знают о прогоне помимо текущего тика: стоимость портфеля по
/// типам с начала прогона (в процентах от стартового портфеля типа) и
/// последний известный состав.
///
/// Ряд копится в памяти, а не собирается из <see cref="ABStock.Application.MarketHistory.IAgentStatisticsReader"/>:
/// девять агентов — это девять обращений к хранилищу на каждый тик, а нужен
/// один и тот же ряд всем сразу.
///
/// Состав хранится здесь по одной причине: он обязан пережить остановку.
/// Раннер в finally своего цикла обнуляет Current вместе с составом, и после
/// остановки страница, которая читает только тик, теряет ВСЁ — и таблицу, и
/// итоги, и график, хотя разбирать сессию человек приходит как раз после
/// того, как она закончилась. Здесь состав живёт ровно столько же, сколько
/// ряды: до старта следующего прогона.
/// </summary>
public interface IAgentEquityHistory
{
    /// <summary>Ряд по каждому типу, уже прореженный под график.</summary>
    IReadOnlyDictionary<AgentType, IReadOnlyList<ChartPoint>> Series { get; }

    /// <summary>
    /// Состав из последнего тика прогона. Пустой, пока в текущем прогоне не
    /// было ни одного тика.
    /// </summary>
    IReadOnlyList<AgentSnapshot> LastAgents { get; }

    /// <summary>Решения агентов из того же последнего тика.</summary>
    IReadOnlyList<AgentDecision> LastDecisions { get; }

    /// <summary>
    /// Прогон, которому принадлежат ряды и состав. Нужен, чтобы после
    /// остановки было у кого спросить сводку: раннер к этому моменту уже
    /// обнулил CurrentRunId, и свежий контур иначе не знает, какой прогон
    /// показывать. <see cref="Guid.Empty"/>, пока не было ни одного тика.
    /// </summary>
    Guid RunId { get; }

    /// <summary>Ряд пополнился — странице пора перерисоваться.</summary>
    event Action? Changed;
}

/// <summary>
/// Сессия здесь — торговый прогон, а он живёт в singleton-е
/// <see cref="ISimulationRunner"/>. История обязана жить ровно столько же:
/// scoped-сервис подписался бы на OnTick только когда кто-то впервые откроет
/// «Агентов», и начало сессии было бы потеряно — тем же рассуждением, что и
/// у <see cref="ISessionNewsFeed"/>.
///
/// Отличие от ленты новостей: в ленту пишет страница, поэтому ленте хватает
/// ленивого создания. Сюда пишет тик, значит подписка должна существовать до
/// первого тика — сервис создаётся при старте приложения в Program.cs.
/// </summary>
public sealed class AgentEquityHistory : IAgentEquityHistory, IDisposable
{
    /// <summary>Отдаём в график не больше — дальше линия всё равно не различима.</summary>
    private const int MaxChartPoints = 400;

    /// <summary>
    /// Потолок накопителя на тип. Одна точка в секунду — это около 5,5 часов
    /// прогона; забытая открытой сессия не должна расти в памяти без предела.
    /// </summary>
    private const int MaxRawPoints = 20_000;

    private readonly Lock _sync = new();
    private readonly ISimulationRunner _runner;

    /// <summary>Стартовая сумма портфелей типа — она же 100 %.</summary>
    private readonly Dictionary<AgentType, decimal> _baselines = [];

    private readonly Dictionary<AgentType, List<ChartPoint>> _raw = [];

    private IReadOnlyList<AgentSnapshot> _lastAgents = [];
    private IReadOnlyList<AgentDecision> _lastDecisions = [];

    private Guid _runId = Guid.Empty;

    public AgentEquityHistory(ISimulationRunner runner)
    {
        _runner = runner;
        _runner.OnTick += HandleTick;
    }

    public IReadOnlyDictionary<AgentType, IReadOnlyList<ChartPoint>> Series
    {
        get
        {
            lock (_sync)
            {
                return _raw.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<ChartPoint>)ChartSeries.Downsample(
                        [.. pair.Value],
                        MaxChartPoints));
            }
        }
    }

    public IReadOnlyList<AgentSnapshot> LastAgents
    {
        get { lock (_sync) { return _lastAgents; } }
    }

    public IReadOnlyList<AgentDecision> LastDecisions
    {
        get { lock (_sync) { return _lastDecisions; } }
    }

    public Guid RunId
    {
        get { lock (_sync) { return _runId; } }
    }

    public event Action? Changed;

    public void Dispose() => _runner.OnTick -= HandleTick;

    private void HandleTick(SimulationTickResult tick)
    {
        lock (_sync)
        {
            ResetOnNewRunLocked();

            // Время старта сессии здесь не хранится: его показывает чип
            // «сессия идёт», и берётся оно из SimulationRunSummary.StartedAt —
            // там же, откуда его берут «Торги». Два источника одного числа
            // разошлись бы на секунду и противоречили бы друг другу.
            var time = DateTimeOffset.Now.ToUnixTimeSeconds();

            _lastAgents = tick.Agents;
            _lastDecisions = tick.Decisions;

            foreach (var group in tick.Agents.GroupBy(agent => agent.Type))
            {
                var value = group.Sum(agent => agent.PortfolioValue);

                // Первый тик, на котором тип вообще виден, и есть его 100 %.
                if (!_baselines.TryGetValue(group.Key, out var baseline))
                {
                    baseline = value;
                    _baselines[group.Key] = baseline;
                }

                var percent = baseline == 0m ? 100m : value / baseline * 100m;
                AppendLocked(group.Key, new ChartPoint(time, percent));
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// В накопителе не больше одной точки на секунду: тики идут чаще, а график
    /// показывает минуты сессии — секундной дискретности ему достаточно.
    /// Прореживание до <see cref="MaxChartPoints"/> делается уже при отдаче.
    /// </summary>
    private void AppendLocked(AgentType type, ChartPoint point)
    {
        if (!_raw.TryGetValue(type, out var points))
        {
            points = [];
            _raw[type] = points;
        }

        if (points.Count > 0 && points[^1].Time == point.Time)
        {
            points[^1] = point;
            return;
        }

        points.Add(point);

        if (points.Count > MaxRawPoints)
        {
            points.RemoveRange(0, points.Count - MaxRawPoints);
        }
    }

    /// <summary>
    /// Новый прогон — новая история: иначе на одном графике оказались бы
    /// проценты, посчитанные от двух разных стартов (раздел 10, «один период»).
    /// </summary>
    private void ResetOnNewRunLocked()
    {
        var runId = _runner.CurrentRunId;
        if (runId == _runId)
        {
            return;
        }

        _runId = runId;
        _baselines.Clear();
        _raw.Clear();
        _lastAgents = [];
        _lastDecisions = [];
    }
}
