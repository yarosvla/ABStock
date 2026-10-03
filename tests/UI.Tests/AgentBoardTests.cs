using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// «Агенты» при общих агентах: один кошелёк, позиции по активам. Проверяется
/// арифметика, которую на защите проверяют первой (раздел 10.1).
/// </summary>
public sealed class AgentBoardTests
{
    private static readonly Guid Glen = Guid.NewGuid();
    private static readonly Guid Kvan = Guid.NewGuid();

    private static readonly BoardMarket[] Markets =
    [
        new(Glen, "GLEN", 100m, []),
        new(Kvan, "KVAN", 50m, [])
    ];

    [Fact]
    public void Замкнутый_рынок_даёт_нулевое_изменение_позиций_по_каждому_активу()
    {
        var board = AgentBoard.Build(
        [
            Account("Trend1", AgentType.TrendFollowing, glen: 60m, kvan: 40m),
            Account("Maker1", AgentType.MarketMaker, glen: 40m, kvan: 60m)
        ], Markets);

        Assert.All(board.Changes, change => Assert.Equal(0m, change.Change));
        Assert.Equal([10m, -10m], board.Groups[0].Changes.Select(change => change.Change));
    }

    [Fact]
    public void Итоги_равны_сумме_строк()
    {
        var board = AgentBoard.Build(
        [
            Account("Trend1", AgentType.TrendFollowing, 60m, 40m, cash: 95_000.4m, pnl: 12.345m),
            Account("Trend2", AgentType.TrendFollowing, 40m, 60m, cash: 105_000.4m, pnl: -2.344m)
        ], Markets);

        Assert.Equal(board.Groups.Sum(group => group.Cash), board.Cash);
        Assert.Equal(board.Groups.Sum(group => group.Pnl), board.Pnl);
        // Сумма округлённых строк, а не округлённая сумма (раздел 10).
        Assert.Equal(12.35m - 2.34m, board.Pnl);
    }

    [Fact]
    public void Матрица_делит_бумаги_стратегии_по_активам()
    {
        var board = AgentBoard.Build([Account("Trend1", AgentType.TrendFollowing, glen: 60m, kvan: 80m)], Markets);

        var row = Assert.Single(board.Holdings);
        // 60 × 100 = 6 000 и 80 × 50 = 4 000: в бумагах 60 % и 40 %.
        Assert.Equal([60m, 40m], row.Cells.Select(cell => cell.Share));
        Assert.Equal(100m, row.Cells.Sum(cell => cell.Share));
    }

    [Fact]
    public void Экземпляры_нумеруются_внутри_типа()
    {
        var board = AgentBoard.Build(
        [
            Account("Trend1", AgentType.TrendFollowing, 50m, 50m),
            Account("Maker1", AgentType.MarketMaker, 50m, 50m),
            Account("Trend2", AgentType.TrendFollowing, 50m, 50m)
        ], Markets);

        Assert.Equal(["Трендовый 1", "Трендовый 2"], board.Groups[0].Instances.Select(item => item.Title));
        Assert.Equal("Маркет-мейкер 1", board.Groups[1].Instances[0].Title);
    }

    [Fact]
    public void Стратегия_называет_актив_где_действовала()
    {
        var markets = new BoardMarket[]
        {
            new(Glen, "GLEN", 100m, [Decision("Trend1", TradeAction.Hold, "жду")]),
            new(Kvan, "KVAN", 50m, [Decision("Trend1", TradeAction.Sell, "KVAN падает — продаю")])
        };

        var group = AgentBoard.Build([Account("Trend1", AgentType.TrendFollowing, 50m, 50m)], markets).Groups.Single();

        Assert.Equal("продают KVAN", group.Action);
        Assert.Equal("KVAN падает — продаю", group.Explanation);
    }

    private static AgentAccountSnapshot Account(
        string name, AgentType type, decimal glen, decimal kvan, decimal cash = 100_000m, decimal pnl = 0m) =>
        new(name, type, cash, 0m, 100_000m, 107_500m + pnl, 107_500m,
            new Dictionary<Guid, AgentPositionSnapshot>
            {
                [Glen] = new(Glen, glen, 0m, 50m, 100m),
                [Kvan] = new(Kvan, kvan, 0m, 50m, 50m)
            });

    private static AgentDecision Decision(string agent, TradeAction action, string explanation) =>
        new(agent, action, explanation, []);
}
