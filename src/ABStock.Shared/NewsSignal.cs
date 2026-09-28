namespace ABStock.Shared;

public record NewsSignal(
    SignalPolarity Polarity,
    decimal Confidence,
    decimal ImpactScore,
    string Explanation
)
{
    /// <summary>
    /// Сколько позитивных факторов профиля затронула новость.
    /// </summary>
    public int PositiveMatches { get; init; }

    /// <summary>
    /// Сколько негативных факторов профиля затронула новость.
    /// </summary>
    public int NegativeMatches { get; init; }

    /// <summary>
    /// Насколько новость вообще попала в профиль актива: средняя близость
    /// сработавших факторов. Значение по умолчанию 0 — сигнал, собранный
    /// без разбора совпадений, честно показывает, что попаданий не разбирали.
    /// </summary>
    public decimal MatchScore { get; init; }

    /// <summary>
    /// Сработавшие факторы профиля с их близостью к новости. Пустой список значит
    /// «не разбирали» или «не совпало ничего» — и то и другое честно: панель
    /// сработавших факторов в этом случае показывает пустое состояние, а не
    /// выдуманные строки.
    /// </summary>
    public IReadOnlyList<NewsFactorMatch> Factors { get; init; } = [];
}
