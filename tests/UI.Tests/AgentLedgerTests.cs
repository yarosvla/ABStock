using ABStock.Application.MarketHistory;
using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Книга общего агента: один кошелёк, история по прогонам активов. Проверяется
/// арифметика, которую на защите проверяют первой (раздел 10.1).
/// </summary>
public sealed class AgentLedgerTests
{
    private static readonly Guid Glen = Guid.NewGuid();
    private static readonly Guid Kvan = Guid.NewGuid();
    private static readonly DateTimeOffset Start = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    // Стартовый запас: по 50 шт GLEN (открытие 100) и KVAN (открытие 50),
    // деньги 100 000. Купил 10 GLEN по 101, продал 20 KVAN по 49.
    private static readonly LedgerMarket[] Markets =
    [
        new(Glen, "GLEN", 100m, 102m, Report(
            [Trade(5, OrderSide.Buy, 101m, 10m)],
            [(0, 100m), (5, 101m), (10, 102m)])),
        new(Kvan, "KVAN", 50m, 48m, Report(
            [Trade(7, OrderSide.Sell, 49m, 20m)],
            [(0, 50m), (7, 49m), (10, 48m)]))
    ];

    private static readonly AgentAccountSnapshot Account = BuildAccount();

    [Fact]
    public void Pnl_по_активам_складывается_в_pnl_портфеля()
    {
        var ledger = AgentLedger.Build(Account, Markets);

        // GLEN: 60 × 102 − 50 × 100 − 10 × 101 = 110. KVAN: 30 × 48 − 50 × 50 + 20 × 49 = −80.
        Assert.Equal([110m, -80m], ledger.Assets.Select(asset => asset.Pnl));
        Assert.Equal(ledger.Pnl, ledger.Assets.Sum(asset => asset.Pnl));
    }

    [Fact]
    public void Итог_по_активу_раскладывается_на_зафиксированный_и_открытый()
    {
        var ledger = AgentLedger.Build(Account, Markets);
        var kvan = ledger.Assets.Single(asset => asset.Symbol == "KVAN");

        // Продал 20 по 49 при средней 50: −20 зафиксировано. Остаток 30 × (48 − 50) = −60.
        Assert.Equal(-20m, kvan.Realized);
        Assert.Equal(-60m, kvan.Unrealized);
        Assert.Equal(50m, kvan.AverageEntryPrice);
        Assert.Equal(kvan.Pnl, kvan.Realized + kvan.Unrealized);
    }

    [Fact]
    public void Копейки_по_активам_сходятся_с_общим_итогом()
    {
        // Три актива по +0,004: по отдельности каждый округлился бы в 0,00,
        // а в сумме это 0,01 — копейка достаётся одной строке.
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var markets = ids.Select((id, index) => new LedgerMarket(id, $"A{index}", 100m, 100m, null)).ToArray();
        var account = new AgentAccountSnapshot("Trend1", AgentType.TrendFollowing, 0m, 0m, 0m, 0m, 0m,
            ids.ToDictionary(id => id, id => new AgentPositionSnapshot(id, 1m, 0m, 1m, 100m) { CostBasis = 99.996m }));

        var ledger = AgentLedger.Build(account, markets);

        Assert.Equal(0.01m, ledger.Pnl);
        Assert.Equal(ledger.Pnl, ledger.Assets.Sum(asset => asset.Pnl));
        Assert.All(ledger.Assets, asset => Assert.Equal(asset.Pnl, asset.Realized + asset.Unrealized));
    }

    [Fact]
    public void Сделки_всех_активов_с_тикером_от_свежей_к_старой()
    {
        var ledger = AgentLedger.Build(Account, Markets);

        Assert.Equal(["KVAN", "GLEN"], ledger.Trades.Select(trade => trade.Symbol));
    }

    [Fact]
    public void Капитал_начинается_со_старта_и_кончается_нынешним()
    {
        var ledger = AgentLedger.Build(Account, Markets);

        Assert.Equal(Account.InitialPortfolioValue, ledger.Equity[0].Value);
        Assert.Equal(Account.PortfolioValue, ledger.Equity[^1].Value);
    }

    [Fact]
    public void Изменение_позиции_считается_от_стартового_запаса()
    {
        var ledger = AgentLedger.Build(Account, Markets);

        Assert.Equal([10m, -20m], ledger.Assets.Select(asset => asset.Change));
        Assert.Equal([1, 0], ledger.Assets.Select(asset => asset.Buys));
    }

    private static AgentAccountSnapshot BuildAccount()
    {
        // Деньги: 100 000 − 1 010 + 980 = 99 970. Портфель: 99 970 + 60 × 102 + 30 × 48.
        const decimal cash = 99_970m;
        const decimal initial = 100_000m + 50m * 100m + 50m * 50m;
        return new("Trend1", AgentType.TrendFollowing, cash, 0m, 100_000m,
            cash + 60m * 102m + 30m * 48m, initial,
            new Dictionary<Guid, AgentPositionSnapshot>
            {
                // Стоимость покупки: запас по цене открытия плюс покупки,
                // минус доля проданного по средней цене — как ведёт её бэкенд.
                [Glen] = new(Glen, 60m, 0m, 50m, 102m) { CostBasis = 50m * 100m + 10m * 101m },
                [Kvan] = new(Kvan, 30m, 0m, 50m, 48m) { CostBasis = 30m * 50m, RealizedPnl = 20m * (49m - 50m) }
            });
    }

    private static AgentStatisticsReport Report(AgentTradeRecord[] trades, (int Seconds, decimal Price)[] prices) =>
        new("Trend1", trades, prices.Select(point => new AgentPricePoint(Start.AddSeconds(point.Seconds), point.Price, 0m)).ToArray());

    private static AgentTradeRecord Trade(int seconds, OrderSide side, decimal price, decimal quantity) =>
        new(Start.AddSeconds(seconds), side, price, quantity);
}
