namespace ABStock.Shared;

/// <summary>Какой стороной профиля новость попала в актив.</summary>
public enum NewsFactorKind
{
    Positive,
    Negative
}

/// <summary>
/// Сработавший фактор профиля: что именно совпало и насколько близко.
///
/// Одних количеств мало. Экран «Новостей» обязан показать не «затронуто 3»,
/// а «позитивный · Ввод новых генерирующих мощностей · близость 0,62»:
/// раздел 1 требует, чтобы причинность читалась с экрана без пояснений,
/// а раздел 16.3 — чтобы человек увидел превращение текста в торговый сигнал.
/// </summary>
/// <param name="Text">Формулировка фактора, как её показывает «Создание актива».</param>
/// <param name="Similarity">Косинусная близость новости и фактора.</param>
/// <param name="Importance">Вес фактора в профиле, 0–1.</param>
public sealed record NewsFactorMatch(
    NewsFactorKind Kind,
    string Text,
    decimal Similarity,
    decimal Importance);
