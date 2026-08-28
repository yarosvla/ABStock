using Microsoft.JSInterop;

namespace ABStock.UI.Services;

/// <summary>
/// Пользовательские настройки: акцент интерфейса, таймфрейм по умолчанию,
/// три переключателя уведомлений и имя оператора.
///
/// Хранилище — localStorage браузера. Ни базы, ни серверного состояния:
/// авторизации в системе нет (раздел 16), пользователь один, и настройки
/// принадлежат его браузеру, а не серверу.
/// </summary>
public interface IUserPreferences
{
    /// <summary>Настройки уже прочитаны из хранилища.</summary>
    bool IsLoaded { get; }

    string Accent { get; }

    string DefaultTimeframe { get; }

    bool NotifyTrades { get; }

    bool NotifyNews { get; }

    bool NotifySystem { get; }

    string OperatorName { get; }

    /// <summary>Настройка изменилась — подписчикам пора перерисоваться.</summary>
    event Action? Changed;

    /// <summary>
    /// Прочитать настройки из хранилища. Вызывается из макета после первой
    /// отрисовки; повторные вызовы отдают ту же задачу и в хранилище не лезут.
    /// </summary>
    Task EnsureLoadedAsync();

    Task SetAccentAsync(string accent);

    Task SetDefaultTimeframeAsync(string timeframeKey);

    Task SetNotifyTradesAsync(bool enabled);

    Task SetNotifyNewsAsync(bool enabled);

    Task SetNotifySystemAsync(bool enabled);

    /// <summary>
    /// Сохранить имя оператора. Пустое имя не сохраняется — вернёт false,
    /// и страница показывает ошибку. Решать за человека, что он имел в виду
    /// пустой строкой, сервис не должен.
    /// </summary>
    Task<bool> TrySetOperatorNameAsync(string name);
}

/// <summary>
/// Scoped, и это осознанно.
///
/// В Blazor Server scoped-сервис живёт ровно столько, сколько контур, и
/// перезагрузку страницы не переживает. Здесь это не страшно: источник
/// истины — localStorage, а сервис лишь кэш на время жизни контура, который
/// при следующем открытии наполнится из того же хранилища.
///
/// Singleton здесь был бы прямой ошибкой: настройки принадлежат браузеру,
/// а не серверу, и один общий экземпляр раздавал бы всем открытым вкладкам
/// чужой акцент. Тем же и отличается от <see cref="ISessionNewsFeed"/> и
/// <see cref="IAgentEquityHistory"/> — те singleton, потому что показывают
/// прогон, а прогон один на сервер.
/// </summary>
public sealed class UserPreferences(IJSRuntime js) : IUserPreferences
{
    private const string AccentKey = "abstock.accent";
    private const string TimeframeKey = "abstock.timeframe";
    private const string NotifyTradesKey = "abstock.notify.trades";
    private const string NotifyNewsKey = "abstock.notify.news";
    private const string NotifySystemKey = "abstock.notify.system";
    private const string OperatorNameKey = "abstock.operator.name";

    public const string DefaultAccent = "graphite";
    public const string DefaultOperatorName = "Оператор";

    /// <summary>
    /// Те же десять ключей, что в загрузочном скрипте App.razor и в
    /// design-system.css. Значение не из списка игнорируется: в хранилище
    /// может лежать что угодно — правка руками, старая версия, порча.
    /// </summary>
    public static readonly IReadOnlyList<AccentPreset> Accents =
    [
        new("graphite", "Графит"),
        new("steel", "Сталь"),
        new("ultramarine", "Ультрамарин"),
        new("azure", "Лазурь"),
        new("turquoise", "Бирюза"),
        new("lavender", "Лаванда"),
        new("amethyst", "Аметист"),
        new("orchid", "Орхидея"),
        new("brass", "Латунь"),
        new("copper", "Медь")
    ];

    private Task? loading;

    public bool IsLoaded { get; private set; }

    public string Accent { get; private set; } = DefaultAccent;

    public string DefaultTimeframe { get; private set; } = Timeframes.DefaultKey;

    public bool NotifyTrades { get; private set; } = true;

    public bool NotifyNews { get; private set; } = true;

    public bool NotifySystem { get; private set; } = true;

    public string OperatorName { get; private set; } = DefaultOperatorName;

    public event Action? Changed;

    public static bool IsKnownAccent(string? accent) =>
        accent is not null && Accents.Any(preset => string.Equals(preset.Key, accent, StringComparison.Ordinal));

    public static string NormalizeAccent(string? accent) =>
        IsKnownAccent(accent) ? accent! : DefaultAccent;

    public static string GetAccentLabel(string accent) =>
        Accents.FirstOrDefault(preset => preset.Key == accent)?.Label
        ?? Accents[0].Label;

    /// <summary>
    /// Инициалы из имени: «Иван Петров» → «ИП», «Оператор» → «ОП».
    /// Живут здесь, а не в шапке: имя одно, и считать инициалы двумя
    /// способами — верный путь к тому, что «Профиль» и шапка покажут разное.
    /// </summary>
    public static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return GetInitials(DefaultOperatorName);
        }

        var initials = parts.Length == 1
            ? parts[0][..Math.Min(2, parts[0].Length)]
            : $"{parts[0][0]}{parts[^1][0]}";

        return initials.ToUpperInvariant();
    }

    public Task EnsureLoadedAsync() => loading ??= LoadAsync();

    private async Task LoadAsync()
    {
        // До первой отрисовки JS-интеропа в Blazor Server не существует, а
        // при разрыве контура вызов бросает. Настройки — не то, ради чего
        // страница имеет право не открыться: значения по умолчанию уже
        // проставлены полями, и с ними всё работает.
        try
        {
            Accent = NormalizeAccent(await ReadAsync(AccentKey));
            DefaultTimeframe = Timeframes.Normalize(await ReadAsync(TimeframeKey));
            NotifyTrades = ReadFlag(await ReadAsync(NotifyTradesKey));
            NotifyNews = ReadFlag(await ReadAsync(NotifyNewsKey));
            NotifySystem = ReadFlag(await ReadAsync(NotifySystemKey));
            OperatorName = ReadName(await ReadAsync(OperatorNameKey));
        }
        catch (Exception)
        {
            // Остаёмся на значениях по умолчанию и пробуем ещё раз при
            // следующем обращении: задача не кешируется, если упала.
            loading = null;
            return;
        }

        IsLoaded = true;
        Changed?.Invoke();
    }

    public async Task SetAccentAsync(string accent)
    {
        var value = NormalizeAccent(accent);

        if (value == Accent && IsLoaded)
        {
            return;
        }

        Accent = value;

        // Не просто запись в хранилище: тот же вызов переставляет data-accent
        // на корне документа, то есть применяет акцент мгновенно и без
        // перезагрузки (раздел 3.1).
        await InvokeAsync("window.abstockPrefs.setAccent", value);
        Changed?.Invoke();
    }

    public Task SetDefaultTimeframeAsync(string timeframeKey) =>
        WriteAsync(TimeframeKey, Timeframes.Normalize(timeframeKey), value => DefaultTimeframe = value);

    public Task SetNotifyTradesAsync(bool enabled) =>
        WriteFlagAsync(NotifyTradesKey, enabled, value => NotifyTrades = value);

    public Task SetNotifyNewsAsync(bool enabled) =>
        WriteFlagAsync(NotifyNewsKey, enabled, value => NotifyNews = value);

    public Task SetNotifySystemAsync(bool enabled) =>
        WriteFlagAsync(NotifySystemKey, enabled, value => NotifySystem = value);

    public async Task<bool> TrySetOperatorNameAsync(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (trimmed.Length == 0)
        {
            return false;
        }

        await WriteAsync(OperatorNameKey, trimmed, value => OperatorName = value);
        return true;
    }

    private Task WriteFlagAsync(string key, bool enabled, Action<bool> assign) =>
        WriteAsync(key, enabled ? "true" : "false", _ => assign(enabled));

    private async Task WriteAsync(string key, string value, Action<string> assign)
    {
        assign(value);
        await InvokeAsync("window.abstockPrefs.set", key, value);
        Changed?.Invoke();
    }

    private async Task<string?> ReadAsync(string key) =>
        await js.InvokeAsync<string?>("window.abstockPrefs.get", key);

    /// <summary>
    /// Запись в хранилище не должна ронять обработчик клика: настройка уже
    /// применена в памяти, и человек видит результат даже там, где хранилище
    /// недоступно, — просто до перезагрузки.
    /// </summary>
    private async Task InvokeAsync(string identifier, params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
        }
        catch (Exception)
        {
            // Хранилище недоступно или контур разорван — значение остаётся
            // в памяти на время жизни контура.
        }
    }

    /// <summary>Пусто — значит настройку не трогали: все три уведомления включены.</summary>
    private static bool ReadFlag(string? stored) =>
        stored is null || !string.Equals(stored, "false", StringComparison.Ordinal);

    private static string ReadName(string? stored)
    {
        var trimmed = stored?.Trim();
        return string.IsNullOrEmpty(trimmed) ? DefaultOperatorName : trimmed;
    }
}

/// <param name="Key">Значение атрибута data-accent и ключ в хранилище.</param>
/// <param name="Label">Название пресета по-русски.</param>
public sealed record AccentPreset(string Key, string Label);
