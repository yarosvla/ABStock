using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Хронология новостей сессии. Раздел 13 DESIGN.md отдаёт левый рельс под
/// события во времени и требует, чтобы она была одна на весь продукт: пока
/// список жил внутри News.razor, панель «События сессии» на «Торгах» была
/// пуста всегда, потому что страницы не видели общего списка.
/// </summary>
public interface ISessionNewsFeed
{
    /// <summary>Новости от свежей к старой — так же, как они лежат в рельсе.</summary>
    IReadOnlyList<SessionNewsEntry> Entries { get; }

    /// <summary>Растёт при каждом изменении ленты: за него можно зацепить кеш разметки.</summary>
    int Revision { get; }

    /// <summary>Лента изменилась — странице пора перерисоваться.</summary>
    event Action? Changed;

    SessionNewsEntry Add(string text, NewsSignal signal);

    void Clear();
}

public sealed class SessionNewsFeed : ISessionNewsFeed
{
    private readonly List<SessionNewsEntry> _entries = [];

    public IReadOnlyList<SessionNewsEntry> Entries => _entries;

    public int Revision { get; private set; }

    public event Action? Changed;

    public SessionNewsEntry Add(string text, NewsSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var entry = new SessionNewsEntry(
            DateTimeOffset.Now,
            text.Trim(),
            signal.Polarity,
            signal.Confidence,
            signal.ImpactScore);

        _entries.Insert(0, entry);
        Touch();

        return entry;
    }

    public void Clear()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        _entries.Clear();
        Touch();
    }

    private void Touch()
    {
        Revision++;
        Changed?.Invoke();
    }
}

/// <param name="At">Когда новость введена — время местное, то же, что в часах шапки.</param>
/// <param name="Text">Текст новости целиком: обрезает его разметка, а не хранилище.</param>
public sealed record SessionNewsEntry(
    DateTimeOffset At,
    string Text,
    SignalPolarity Polarity,
    decimal Confidence,
    decimal ImpactScore);
