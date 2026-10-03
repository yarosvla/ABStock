namespace ABStock.UI.Components.Ui;

/// <summary>
/// Элемент пути в крошках (раздел 13). В путь попадают только разделы:
/// статусы, режимы и бейджи между «›» читаются как ещё один раздел, поэтому
/// статус актива вешается чипом на сам элемент-актив (<see cref="ChipText"/>),
/// а статус приложения живёт в шапке.
/// </summary>
/// <param name="Title">Текст элемента.</param>
/// <param name="Href">Ссылка. null — элемент не кликается.</param>
/// <param name="IsAsset">Элемент-актив: имя со свитчером-шевроном (раздел 9.24).</param>
/// <param name="ChipText">Статус актива рядом с именем, внутри одного элемента пути.</param>
/// <param name="Symbol">Тикер актива — по нему свитчер отмечает выбранный.</param>
/// <param name="SwitchTarget">
/// Куда ведут строки свитчера: <c>trading</c> — на торги актива,
/// <c>assets</c> — на его профиль.
/// </param>
public sealed record Crumb(
    string Title,
    string? Href = null,
    bool IsAsset = false,
    string? ChipText = null,
    string? Symbol = null,
    string SwitchTarget = "trading");
