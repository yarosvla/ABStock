namespace ABStock.UI.Components.Ui;

/// <summary>
/// Элемент пути в крошках (раздел 13). В путь попадают только разделы:
/// статусы, режимы и бейджи между «›» читаются как ещё один раздел, поэтому
/// статус актива вешается чипом на сам элемент-актив (<see cref="ChipText"/>),
/// а статус приложения живёт в шапке.
/// </summary>
/// <param name="Title">Текст элемента.</param>
/// <param name="Href">Ссылка. null — элемент не кликается.</param>
/// <param name="IsAsset">
/// Элемент-актив: имя со свитчером-шевроном. Это задел на мульти-актив,
/// закрытый разделом 16: в сессии актив один.
/// </param>
/// <param name="ChipText">Статус актива рядом с именем, внутри одного элемента пути.</param>
public sealed record Crumb(
    string Title,
    string? Href = null,
    bool IsAsset = false,
    string? ChipText = null);
