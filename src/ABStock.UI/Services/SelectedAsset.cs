using Microsoft.JSInterop;

namespace ABStock.UI.Services;

/// <summary>
/// Какой актив выбран в этой вкладке. Хранится в адресе (<c>/trading/GLEN</c>)
/// и запоминается в localStorage под <see cref="StorageKey"/> — как тема и
/// акцент, это свойство браузера, а не сервера (DESIGN.md 13).
/// </summary>
public interface ISelectedAsset
{
    /// <summary>Последний выбранный тикер, известный контуру, или null, пока его не читали.</summary>
    string? Symbol { get; }

    event Action? Changed;

    /// <summary>
    /// Тикер, который надо показать, когда адрес его не называет: запомненный,
    /// если такой актив ещё есть в каталоге, иначе первый в торгах, иначе
    /// первый в каталоге. Null — активов нет.
    /// </summary>
    Task<string?> ResolveAsync(IReadOnlyList<SessionAsset> assets, IReadOnlyCollection<string> trading);

    /// <summary>
    /// Адрес назвал актив — он и становится последним выбранным. Адрес главнее
    /// памяти: ссылка коллеги открывает тот актив, что в ней.
    /// </summary>
    Task RememberAsync(string symbol);
}

/// <summary>
/// Scoped, как <see cref="UserPreferences"/>, и по той же причине: источник
/// истины лежит в браузере, а сервис — кеш на время жизни контура. Singleton
/// раздал бы всем вкладкам один выбранный актив, и переключение на одном
/// проекторе меняло бы актив у всех.
/// </summary>
public sealed class SelectedAsset(IJSRuntime js) : ISelectedAsset
{
    public const string StorageKey = "abstock.asset";

    private bool loaded;

    public string? Symbol { get; private set; }

    public event Action? Changed;

    public async Task<string?> ResolveAsync(IReadOnlyList<SessionAsset> assets, IReadOnlyCollection<string> trading)
    {
        if (!loaded)
        {
            Symbol = Normalize(await ReadAsync());
            loaded = true;
        }

        return Pick(Symbol, assets, trading);
    }

    /// <summary>Выбор без хранилища — чистое правило, его и проверяют тесты.</summary>
    public static string? Pick(string? remembered, IReadOnlyList<SessionAsset> assets, IReadOnlyCollection<string> trading)
    {
        if (remembered is not null
            && assets.Any(asset => string.Equals(asset.Symbol, remembered, StringComparison.OrdinalIgnoreCase)))
        {
            return assets.First(asset => string.Equals(asset.Symbol, remembered, StringComparison.OrdinalIgnoreCase)).Symbol;
        }

        // Порядок каталога — по дате создания, поэтому «первый в торгах» один
        // и тот же на каждом входе, а не случайный.
        return assets.FirstOrDefault(asset => trading.Contains(asset.Symbol, StringComparer.OrdinalIgnoreCase))?.Symbol
            ?? assets.FirstOrDefault()?.Symbol;
    }

    public async Task RememberAsync(string symbol)
    {
        var value = Normalize(symbol);

        if (value is null)
        {
            return;
        }

        loaded = true;

        if (string.Equals(Symbol, value, StringComparison.Ordinal))
        {
            return;
        }

        Symbol = value;
        Changed?.Invoke();

        try
        {
            await js.InvokeVoidAsync("window.abstockPrefs.set", StorageKey, value);
        }
        catch (Exception)
        {
            // Хранилище недоступно или контур ещё не интерактивный (пре-рендер):
            // выбор остаётся в памяти контура, адрес всё равно его называет.
        }
    }

    private async Task<string?> ReadAsync()
    {
        try
        {
            return await js.InvokeAsync<string?>("window.abstockPrefs.get", StorageKey);
        }
        catch (Exception)
        {
            // До первой отрисовки JS-интеропа нет; без памяти выбор падает на
            // первый актив в торгах, и это не повод не открыть страницу.
            return null;
        }
    }

    private static string? Normalize(string? symbol)
    {
        var trimmed = symbol?.Trim().ToUpperInvariant();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > 8 ? null : trimmed;
    }
}
