namespace ABStock.UI.Components.Ui;

/// <summary>Один сегмент <see cref="Segmented{TValue}"/>: значение и подпись.</summary>
public sealed record SegmentedItem<TValue>(TValue Value, string Label);
