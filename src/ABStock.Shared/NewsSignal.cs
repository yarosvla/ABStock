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
    /// Сколько рисков профиля затронула новость.
    /// </summary>
    public int RiskMatches { get; init; }

    /// <summary>
    /// Третий множитель формулы силы влияния
    /// (Confidence × NewsSensitivity × MatchScore): насколько новость вообще
    /// попала в профиль актива. Значение по умолчанию 0 — сигнал, собранный
    /// без разбора совпадений, честно показывает, что попаданий не разбирали.
    /// </summary>
    public decimal MatchScore { get; init; }

    /// <summary>
    /// Сработавшие пункты профиля с фрагментами новости. Пустой список значит
    /// «не разбирали» или «не совпало ничего» — и то и другое честно: панель
    /// сработавших факторов в этом случае показывает пустое состояние, а не
    /// выдуманные строки.
    /// </summary>
    public IReadOnlyList<NewsFactorMatch> Factors { get; init; } = [];
}
