namespace ABStock.Shared;

public record AssetProfile(
    string Name,
    AssetType AssetType,
    string Description,
    IReadOnlyList<string> PositiveFactors,
    IReadOnlyList<string> NegativeFactors,
    IReadOnlyList<string> Risks,
    decimal NewsSensitivity,
    IReadOnlyList<string> Keywords
)
{
    /// <summary>
    /// Чем собран профиль. Значение по умолчанию — <see cref="ProfileSource.Ai"/>:
    /// сегодня разбор описания единственный, а когда появится реальный вызов
    /// модели, генератор будет выставлять <see cref="ProfileSource.Fallback"/>
    /// при неудаче, и интерфейс покажет это без переделки.
    /// </summary>
    public ProfileSource Source { get; init; } = ProfileSource.Ai;
}
