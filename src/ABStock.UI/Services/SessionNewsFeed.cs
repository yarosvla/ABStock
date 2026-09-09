using ABStock.Application.Simulation;
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

/// <summary>
/// Сессия здесь — это торговый прогон, а он живёт в singleton-е
/// <see cref="ISimulationRunner"/>. Лента обязана жить ровно столько же,
/// сколько прогон, события которого показывает, — поэтому тоже singleton.
///
/// Scoped переходы по ссылкам внутри приложения переживает: enhanced
/// navigation не перезагружает документ и не рвёт контур. Но перезагрузку
/// страницы и вход по прямому адресу — нет, а симуляция при этом продолжает
/// идти. В рельсе оказалось бы «Новостей 0» посреди сессии, в которой новости
/// были. Тем же рассуждением singleton и <see cref="IActiveAssetContext"/>.
/// </summary>
public sealed class SessionNewsFeed(ISimulationRunner runner) : ISessionNewsFeed
{
    private readonly Lock _sync = new();
    private readonly List<SessionNewsEntry> _entries = [];
    private Guid _runId;

    public IReadOnlyList<SessionNewsEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                DropOtherRunLocked();
                return _entries.ToArray();
            }
        }
    }

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

        lock (_sync)
        {
            DropOtherRunLocked();
            _entries.Insert(0, entry);
            Revision++;
        }

        Changed?.Invoke();
        return entry;
    }

    public void Clear()
    {
        lock (_sync)
        {
            if (_entries.Count == 0)
            {
                return;
            }

            _entries.Clear();
            Revision++;
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Новый прогон — новая хронология: иначе рядом оказались бы новости
    /// прошлого запуска и счётчики нынешнего (раздел 10, «один период»).
    /// Событие отсюда не шлётся: проверка вызывается из геттера во время
    /// отрисовки, и StateHasChanged на этом месте был бы повторным входом.
    /// </summary>
    private void DropOtherRunLocked()
    {
        var runId = runner.CurrentRunId;

        if (runId == _runId)
        {
            return;
        }

        _runId = runId;

        if (_entries.Count > 0)
        {
            _entries.Clear();
            Revision++;
        }
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
