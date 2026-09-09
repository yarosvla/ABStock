using ABStock.Shared;

namespace ABStock.AI.Internal;

/// <param name="Score">
/// Третий множитель формулы силы влияния — «совпадение по теме».
/// </param>
/// <param name="Matches">
/// Что именно совпало и с каким куском новости. Одних количеств мало: экран
/// показывает не «затронуто 3», а сами формулировки (раздел 16.3).
/// </param>
internal sealed record AspectMatchResult(
    int PositiveMatches,
    int NegativeMatches,
    int RiskMatches,
    decimal Score,
    IReadOnlyList<NewsFactorMatch>? Matches = null)
{
    /// <summary>Пустой список — «не разбирали» или «не совпало ничего».</summary>
    public IReadOnlyList<NewsFactorMatch> Matches { get; init; } = Matches ?? [];
}
