using ABStock.AI.Models;
using ABStock.AI.Services;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>
/// Ввод новости. Новость одна на весь рынок (DESIGN.md 16.3): текст
/// разбирается по профилю каждого актива в торгах, и каждый задетый актив
/// получает свой сигнал — <c>SubmitNews(assetId, signal)</c>. Отправки «на
/// весь рынок» у бэкенда нет, и интерфейс её не изображает: веер на экране —
/// это ровно то, что ушло на рынки.
/// </summary>
public interface INewsDesk
{
    const int MinLength = 20;
    const int MaxLength = 1000;

    Task<SessionEvent> AnalyzeAsync(string text, CancellationToken ct = default);
}

public sealed class NewsDesk(
    INewsProcessingService analyzer,
    IAssetRegistry assets,
    ISessionMarkets markets,
    ISessionEvents events) : INewsDesk
{
    public async Task<SessionEvent> AnalyzeAsync(string text, CancellationToken ct = default)
    {
        var trimmed = text.Trim();

        if (trimmed.Length < INewsDesk.MinLength)
        {
            throw new ArgumentException($"Нужно не меньше {INewsDesk.MinLength} символов.", nameof(text));
        }

        // Разбирается только то, что торгуется: сигнал без рынка некому
        // доставить, а строка «не разбиралась» честнее выдуманного числа.
        var trading = assets.Assets.Where(asset => markets.Trades(asset.Id)).ToArray();

        // Анализы независимы — по профилю на актив, — и идут параллельно:
        // восемь последовательных вызовов модели человек ждал бы восьмикратно.
        var signals = await Task.WhenAll(trading.Select(async asset =>
            (asset.Symbol, Signal: await analyzer.AnalyzeAsync(
                new NewsAnalysisRequest { NewsText = trimmed, Profile = asset.Profile }, ct))));

        var fan = NewsFan.FromSignals(
            assets.Assets,
            signals.ToDictionary(item => item.Symbol, item => item.Signal, StringComparer.OrdinalIgnoreCase));

        // Сначала хронология (она запоминает цены на момент новости), потом
        // рынки: иначе первый шаг после сигнала мог бы уже сдвинуть цену «до».
        var entry = events.AddNews(trimmed, fan);
        markets.SubmitNews(fan);
        return entry;
    }
}
