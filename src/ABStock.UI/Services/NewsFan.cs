using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Одна новость — веер сигналов (docs/multi-asset.md, контракт 2.4).
/// Тональность и уверенность принадлежат тексту и считаются один раз; сила
/// влияния — профилю, поэтому у каждого актива своя.
/// </summary>
/// <param name="Assets">
/// Строка на каждый актив каталога в его порядке — и на незадетые, и на
/// ждущие запуска: незадетый актив остаётся строкой «не задет», иначе
/// непонятно, проверяли ли его вообще.
/// </param>
public sealed record NewsFan(
    SignalPolarity Polarity,
    decimal Confidence,
    IReadOnlyList<AssetSignal> Assets)
{
    /// <summary>Идентификатор одной разобранной новости и всех её рыночных сигналов.</summary>
    public Guid NewsId { get; init; } = Guid.NewGuid();

    /// <summary>Задетые активы в порядке каталога.</summary>
    public IEnumerable<AssetSignal> Hit => Assets.Where(asset => asset.IsHit);

    /// <summary>
    /// Самый задетый актив для отображения силы влияния. Сигналы получают
    /// все задетые рынки. Null — новость не задела ни один актив.
    /// </summary>
    public AssetSignal? Lead => Hit.MaxBy(asset => asset.Strength);

    /// <summary>Наибольшая сила в веере — от неё считается толщина связей.</summary>
    public decimal MaxImpact => Lead?.Strength ?? 0m;

    public AssetSignal? For(string? symbol) =>
        Assets.FirstOrDefault(asset => string.Equals(asset.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Веер из сигналов, посчитанных по одному на актив. Тональность и
    /// уверенность берутся у первого сигнала: модель тональности читает один
    /// и тот же текст и от профиля не зависит.
    /// </summary>
    public static NewsFan FromSignals(
        IReadOnlyList<SessionAsset> catalog,
        IReadOnlyDictionary<string, NewsSignal> signals)
    {
        var rows = catalog
            .Select(asset => new AssetSignal(
                asset.Symbol,
                signals.TryGetValue(asset.Symbol, out var signal) ? signal : null))
            .ToArray();

        var first = rows.FirstOrDefault(row => row.Signal is not null)?.Signal;

        return new NewsFan(
            first?.Polarity ?? SignalPolarity.Neutral,
            first?.Confidence ?? 0m,
            rows);
    }
}

/// <param name="Signal">
/// Сигнал для профиля этого актива; null — новость его не разбирала
/// (актив ждёт запуска и в торгах не участвует).
/// </param>
public sealed record AssetSignal(string Symbol, NewsSignal? Signal)
{
    public bool WasAnalyzed => Signal is not null;

    /// <summary>
    /// Задет — совпал хотя бы один пункт профиля (контракт 2.4). Порога по
    /// силе нет: заглушки анализаторов дают около 0,2, ниже начала шкалы, и
    /// любой порог из шкалы отсёк бы всё.
    /// </summary>
    public bool IsHit => Signal is { MatchScore: > 0m };

    /// <summary>
    /// Сила влияния со знаком. Знак — направление для ЭТОГО профиля:
    /// позитивная новость, задевшая негативный фактор, толкает цену вниз.
    /// </summary>
    public decimal? Impact => IsHit ? Signal!.ImpactScore : null;

    /// <summary>Сила без знака — то, что стоит на шкале (DESIGN.md 9.17).</summary>
    public decimal? Strength => Impact is { } impact ? Math.Abs(impact) : null;

    /// <summary>Куда новость толкает цену этого актива: вверх, вниз или никуда.</summary>
    public int Direction => Impact is { } impact ? Math.Sign(impact) : 0;
}
