using ABStock.Application.MarketHistory;
using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Часы сессии. Главное здесь — что идущая и остановленная сессия считаются
/// по-разному, и что остановленная НЕ считается как «сейчас минус старт».
///
/// Ошибка была живой и невидимой: «Агенты» считали именно так, но чип
/// остановленной сессии показывал слова вместо числа, и выводить неверный
/// расчёт было некуда. Как только чип начал показывать время в обоих
/// состояниях (пункт 106 docs/ui-backlog.md), ошибка стала бы видна на экране.
/// </summary>
public sealed class SessionClockTests
{
    private static SimulationRunSummary Run(DateTimeOffset startedAt, int tickCount) =>
        new(Guid.NewGuid(), "Гелиос Энерго", AssetType.Stock, startedAt, tickCount, TradeCount: 0, LastPrice: 100m);

    [Fact]
    public void Остановленная_сессия_не_растёт_после_остановки()
    {
        // Сессия шла четыре минуты и остановлена. Страницу открыли час спустя.
        var summary = Run(DateTimeOffset.Now.AddHours(-1), tickCount: 240);

        Assert.Equal("00:04:00", SessionClock.Format(running: false, summary));
    }

    [Fact]
    public void Идущая_сессия_считается_от_старта_а_не_по_числу_тиков()
    {
        // Тиков ещё ноль — например, сводка прочитана раньше первого тика.
        // Длительность при этом не нулевая: сессия идёт полминуты.
        var summary = Run(DateTimeOffset.Now.AddSeconds(-30), tickCount: 0);

        var elapsed = SessionClock.Elapsed(running: true, summary);

        Assert.NotNull(elapsed);
        Assert.InRange(elapsed.Value.TotalSeconds, 29, 32);
    }

    [Fact]
    public void Без_прогона_часов_нет()
    {
        // Именно null, а не «00:00:00»: нули утверждали бы, что сессия длилась
        // ноль, то есть высказывались бы о сессии, которой не было. Чип на это
        // отвечает тем, что не показывается вовсе.
        Assert.Null(SessionClock.Elapsed(running: false, summary: null));
        Assert.Null(SessionClock.Format(running: false, summary: null));
        Assert.Null(SessionClock.Format(running: true, summary: null));
    }

    [Fact]
    public void Формат_всегда_ЧЧ_ММ_СС()
    {
        // Раздел 10: разрядность одна на весь продукт, а не «1:05» на одном
        // экране и «01:05» на другом.
        Assert.Equal("01:01:05", SessionClock.Format(running: false, Run(DateTimeOffset.Now, 3665)));
        Assert.Equal("00:00:07", SessionClock.Format(running: false, Run(DateTimeOffset.Now, 7)));
    }
}
