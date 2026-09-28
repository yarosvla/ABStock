namespace ABStock.Shared;

public record AssetProfile(
    string Name,
    AssetType AssetType,
    string Description,
    IReadOnlyList<AssetFactor> Factors,
    decimal NewsSensitivity
)
{
    /// <summary>
    /// Чем собран профиль. Модель недоступна (нет ключа, не отвечает
    /// Python-сервис) — генератор ставит <see cref="ProfileSource.Fallback"/>,
    /// и интерфейс показывает запасной профиль без переделки.
    /// </summary>
    public ProfileSource Source { get; init; } = ProfileSource.Ai;

    public IEnumerable<AssetFactor> PositiveFactors => Factors.Where(factor => factor.IsPositive);

    public IEnumerable<AssetFactor> NegativeFactors => Factors.Where(factor => !factor.IsPositive);
}
