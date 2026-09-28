namespace ABStock.Shared;

/// <param name="Embedding">
/// Вектор формулировки. Пустой — фактор собран без модели (запасной или
/// демо-профиль): сопоставитель тогда посчитает вектор сам при разборе новости.
/// </param>
public record AssetFactor(
    string Name,
    bool IsPositive,
    decimal Importance,
    float[] Embedding
);
