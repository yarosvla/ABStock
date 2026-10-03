using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Состав агентов перед запуском — количество каждого типа и капитал
/// экземпляра (DESIGN.md 16.0). Живёт в пульте «Активов», рядом с кнопкой
/// «Запустить торги»: состав один на сессию, а не на актив.
/// </summary>
public interface IAgentComposition
{
    const int MaxPerType = 8;
    const int MinTotal = 1;
    const int MaxTotal = 32;

    /// <summary>
    /// Стартовый запас агента в каждом активе. Бэкенд выдаёт его сам при
    /// запуске и при добавлении рынка; интерфейс показывает его подписью.
    /// </summary>
    const decimal InitialPosition = 50m;

    /// <summary>Строки в порядке стратегий раздела 16.4.</summary>
    IReadOnlyList<CompositionRow> Rows { get; }

    int TotalCount { get; }

    decimal TotalCapital { get; }

    bool IsValid { get; }

    event Action? Changed;

    void SetCount(AgentType type, int count);

    void SetCapital(AgentType type, decimal capital);

    IReadOnlyList<AgentSpec> ToSpecs();
}

/// <param name="Capital">Капитал одного экземпляра, ₽.</param>
public sealed record CompositionRow(AgentType Type, int Count, decimal Capital)
{
    public decimal TotalCapital => Count * Capital;

    /// <summary>Ноль агентов типа допустим, поэтому капитал проверяется только у тех, кто идёт в сессию.</summary>
    public bool HasCapitalError => Count > 0 && Capital <= 0m;
}

/// <summary>
/// Singleton: состав — свойство сессии, а не вкладки. Пока торги идут,
/// состав приведён к тому, которым они реально идут: после перезагрузки
/// страницы посреди сессии пульт показывал бы значения по умолчанию, а не
/// то, чем торгуют на экране.
/// </summary>
public sealed class AgentComposition : IAgentComposition, IDisposable
{
    private readonly ISessionMarkets _markets;
    private readonly Lock _sync = new();
    private Guid _syncedSession;

    // По два экземпляра каждой стратегии — столько, чтобы у каждой было
    // «×2» и группа в таблице агентов, но экран не превращался в список.
    private CompositionRow[] _rows =
    [
        new(AgentType.TrendFollowing, 2, 100_000m),
        new(AgentType.CounterTrend, 2, 100_000m),
        new(AgentType.MarketMaker, 2, 150_000m),
        new(AgentType.NewsDriven, 2, 100_000m)
    ];

    public AgentComposition(ISessionMarkets markets)
    {
        _markets = markets;
        _markets.Changed += SyncFromSession;
    }

    public IReadOnlyList<CompositionRow> Rows
    {
        get { lock (_sync) { return _rows; } }
    }

    public int TotalCount => Rows.Sum(row => row.Count);

    public decimal TotalCapital => Rows.Sum(row => row.TotalCapital);

    public bool IsValid
    {
        get
        {
            var rows = Rows;
            var total = rows.Sum(row => row.Count);

            return total is >= IAgentComposition.MinTotal and <= IAgentComposition.MaxTotal
                && rows.All(row => row.Count is >= 0 and <= IAgentComposition.MaxPerType && !row.HasCapitalError);
        }
    }

    public event Action? Changed;

    public void SetCount(AgentType type, int count) =>
        Replace(type, row => row with { Count = Math.Clamp(count, 0, IAgentComposition.MaxPerType) });

    public void SetCapital(AgentType type, decimal capital) =>
        Replace(type, row => row with { Capital = Math.Max(0m, Math.Round(capital, 0)) });

    public IReadOnlyList<AgentSpec> ToSpecs() =>
        Rows.Where(row => row.Count > 0)
            .SelectMany(row => Enumerable.Repeat(
                new AgentSpec(row.Type, row.Capital, IAgentComposition.InitialPosition),
                row.Count))
            .ToArray();

    public void Dispose() => _markets.Changed -= SyncFromSession;

    private void Replace(AgentType type, Func<CompositionRow, CompositionRow> change)
    {
        // Пока торги идут, состав заперт: менять его можно только после остановки.
        if (_markets.IsRunning)
        {
            return;
        }

        lock (_sync)
        {
            _rows = _rows.Select(row => row.Type == type ? change(row) : row).ToArray();
        }

        Changed?.Invoke();
    }

    private void SyncFromSession()
    {
        var sessionId = _markets.SessionId;
        var accounts = _markets.Accounts;

        if (!_markets.IsRunning || sessionId == _syncedSession || accounts.Count == 0)
        {
            return;
        }

        lock (_sync)
        {
            _syncedSession = sessionId;
            _rows = _rows
                .Select(row =>
                {
                    var live = accounts.Where(account => account.AgentType == row.Type).ToArray();
                    return live.Length == 0
                        ? row with { Count = 0 }
                        : row with { Count = live.Length, Capital = Math.Round(live[0].InitialCash, 0) };
                })
                .ToArray();
        }

        Changed?.Invoke();
    }
}
