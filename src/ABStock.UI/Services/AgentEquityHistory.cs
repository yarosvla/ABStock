using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Стоимость портфеля каждой стратегии с начала сессии, в процентах от её
/// стартовой стоимости. Ряд графика «Агентов».
/// </summary>
public interface IAgentEquityHistory
{
    IReadOnlyDictionary<AgentType, IReadOnlyList<ChartPoint>> Series { get; }

    event Action? Changed;
}

/// <summary>
/// Singleton и создаётся при старте приложения: в историю пишет шаг сессии, а
/// не страница. Ленивое создание отдало бы сервису первый шаг только после
/// того, как кто-то откроет «Агентов», и 100 % отсчитывались бы от середины.
///
/// База — стартовая стоимость счёта, а не первая увиденная точка: у общего
/// агента она растёт, когда в идущие торги вступает новый актив (стартовый
/// запас — вклад капитала), и процент от первой точки показал бы этот вклад
/// как прибыль.
///
/// Новая сессия — новые ряды; после остановки ряды остаются: разбирать сессию
/// приходят как раз после её конца.
/// </summary>
public sealed class AgentEquityHistory : IAgentEquityHistory, IDisposable
{
    private const int MaxChartPoints = 400;

    /// <summary>Сутки торгов по шагу в секунду — больше одной демонстрации не бывает.</summary>
    private const int MaxRawPoints = 86_400;

    private readonly ISessionMarkets _markets;
    private readonly Lock _sync = new();
    private readonly Dictionary<AgentType, List<ChartPoint>> _raw = [];
    private Guid _sessionId;

    public AgentEquityHistory(ISessionMarkets markets)
    {
        _markets = markets;
        _markets.Changed += HandleChanged;
    }

    public IReadOnlyDictionary<AgentType, IReadOnlyList<ChartPoint>> Series
    {
        get
        {
            lock (_sync)
            {
                return _raw.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<ChartPoint>)ChartSeries.Downsample([.. pair.Value], MaxChartPoints));
            }
        }
    }

    public event Action? Changed;

    public void Dispose() => _markets.Changed -= HandleChanged;

    private void HandleChanged()
    {
        if (!_markets.IsRunning)
        {
            return;
        }

        lock (_sync)
        {
            if (_markets.SessionId != _sessionId)
            {
                _sessionId = _markets.SessionId;
                _raw.Clear();
            }

            var time = DateTimeOffset.Now.ToUnixTimeSeconds();

            foreach (var group in _markets.Accounts.GroupBy(account => account.AgentType))
            {
                var initial = group.Sum(account => account.InitialPortfolioValue);
                var value = group.Sum(account => account.PortfolioValue);
                Append(group.Key, new ChartPoint(time, initial == 0m ? 100m : value / initial * 100m));
            }
        }

        Changed?.Invoke();
    }

    private void Append(AgentType type, ChartPoint point)
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
}
