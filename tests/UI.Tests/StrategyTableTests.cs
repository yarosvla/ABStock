using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Агенты на «Торгах» свёрнуты по стратегиям и показаны глазами одного
/// актива (DESIGN.md 16.2).
/// </summary>
public sealed class StrategyTableTests
{
    private static readonly Guid Glen = Guid.NewGuid();
    private static readonly Guid Kvan = Guid.NewGuid();

    [Fact]
    public void Позиция_и_её_изменение_берутся_в_выбранном_активе()
    {
        var accounts = new[]
        {
            Account("Trend1", AgentType.TrendFollowing, glen: 80, kvan: 10),
            Account("Trend2", AgentType.TrendFollowing, glen: 70, kvan: 50)
        };

        var row = Build(accounts, []).Single();

        Assert.Equal(2, row.Count);
        Assert.Equal(150m, row.Position);
        Assert.Equal(50m, row.PositionChange);
    }

    [Fact]
    public void Ждущая_здесь_стратегия_говорит_что_торгует_другим_активом()
    {
        var accounts = new[] { Account("News1", AgentType.NewsDriven, glen: 50, kvan: 56) };
        var here = new[] { Decision("News1", TradeAction.Hold, "жду новости") };
        var elsewhere = new Dictionary<string, IReadOnlyList<AgentDecision>>
        {
            ["KVAN"] = [Decision("News1", TradeAction.Buy, "новость задела KVAN сильнее всего")]
        };

        var row = Build(accounts, here, elsewhere).Single();

        Assert.Equal("торгуют KVAN", row.Action);
        Assert.True(row.TradesElsewhere);
        Assert.Equal("новость задела KVAN сильнее всего", row.Explanation);
    }

    [Fact]
    public void Сделка_в_этом_активе_важнее_соседнего()
    {
        var accounts = new[] { Account("Trend1", AgentType.TrendFollowing, glen: 60, kvan: 50) };
        var here = new[] { Decision("Trend1", TradeAction.Buy, "GLEN растёт — иду за движением") };
        var elsewhere = new Dictionary<string, IReadOnlyList<AgentDecision>>
        {
            ["KVAN"] = [Decision("Trend1", TradeAction.Sell, "KVAN падает")]
        };

        var row = Build(accounts, here, elsewhere).Single();

        Assert.Equal("покупает GLEN", row.Action);
        Assert.False(row.TradesElsewhere);
    }

    [Fact]
    public void Стратегии_идут_в_порядке_раздела_16_4()
    {
        var accounts = new[]
        {
            Account("News1", AgentType.NewsDriven, 50, 50),
            Account("Maker1", AgentType.MarketMaker, 50, 50),
            Account("Trend1", AgentType.TrendFollowing, 50, 50),
            Account("Counter1", AgentType.CounterTrend, 50, 50)
        };

        Assert.Equal(
            [AgentType.TrendFollowing, AgentType.CounterTrend, AgentType.MarketMaker, AgentType.NewsDriven],
            Build(accounts, []).Select(row => row.Type));
    }

    [Fact]
    public void Итог_по_замкнутому_рынку_даёт_ноль_изменения()
    {
        var accounts = new[]
        {
            Account("Trend1", AgentType.TrendFollowing, glen: 70, kvan: 50),
            Account("Maker1", AgentType.MarketMaker, glen: 30, kvan: 50)
        };

        var totals = StrategyTable.Totals(Build(accounts, []));

        Assert.Equal(100m, totals.Position);
        Assert.Equal(0m, totals.PositionChange);
    }

    private static IReadOnlyList<StrategyRow> Build(
        IReadOnlyList<AgentAccountSnapshot> accounts,
        IReadOnlyList<AgentDecision> here,
        IReadOnlyDictionary<string, IReadOnlyList<AgentDecision>>? elsewhere = null) =>
        StrategyTable.Build(Glen, "GLEN", 100m, accounts, here, elsewhere ?? new Dictionary<string, IReadOnlyList<AgentDecision>>());

    private static AgentAccountSnapshot Account(string name, AgentType type, decimal glen, decimal kvan) =>
        new(name, type, 100_000m, 0m, 100_000m, 100_000m + (glen + kvan) * 100m, 110_000m,
            new Dictionary<Guid, AgentPositionSnapshot>
            {
                [Glen] = new(Glen, glen, 0m, 50m, 100m),
                [Kvan] = new(Kvan, kvan, 0m, 50m, 100m)
            });

    private static AgentDecision Decision(string agent, TradeAction action, string explanation) =>
        new(agent, action, explanation, []);
}
