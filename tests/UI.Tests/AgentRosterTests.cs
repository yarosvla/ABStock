using ABStock.Application.Simulation;
using ABStock.Shared;
using ABStock.UI.Services;

namespace ABStock.UI.Tests;

/// <summary>
/// Правила таблицы «Агенты в сессии» из разделов 9.8 и 10 DESIGN.md: группа
/// из одного экземпляра не получает строку группы, дословно совпавшее
/// объяснение пишется один раз, суммы строки группы сходятся с экземплярами,
/// нумерация идёт внутри типа.
/// </summary>
public class AgentRosterTests
{
    // Девять агентов четырёх типов — состав с артборда 01:
    // три трендовых, два контр-трендовых, один маркет-мейкер, три новостных.
    private static AgentSnapshot Agent(
        string name,
        AgentType type,
        decimal cash = 100_000m,
        decimal position = 0m,
        decimal portfolio = 100_000m,
        decimal initialCash = 100_000m,
        decimal initialPortfolio = 100_000m) =>
        new(name, type, cash, position, portfolio, initialCash, initialPortfolio);

    private static AgentDecision Decision(string name, TradeAction action, string explanation) =>
        new(name, action, explanation, []);

    private static AgentSnapshot[] NineAgents() =>
    [
        Agent("TrendFollowingAgent", AgentType.TrendFollowing),
        Agent("TrendFollowingAgent 2", AgentType.TrendFollowing),
        Agent("TrendFollowingAgent 3", AgentType.TrendFollowing),
        Agent("CounterTrendAgent", AgentType.CounterTrend),
        Agent("CounterTrendAgent 2", AgentType.CounterTrend),
        Agent("MarketMakerAgent", AgentType.MarketMaker),
        Agent("NewsDrivenAgent", AgentType.NewsDriven),
        Agent("NewsDrivenAgent 2", AgentType.NewsDriven),
        Agent("NewsDrivenAgent 3", AgentType.NewsDriven)
    ];

    [Fact]
    public void Типы_идут_в_порядке_перечисления_а_экземпляры_нумеруются_внутри_типа()
    {
        var view = AgentRoster.Build(NineAgents(), []);

        var titles = view.Rows.Select(row => row.Title).ToArray();

        Assert.Equal(
        [
            "Трендовый ×3", "Трендовый 1", "Трендовый 2", "Трендовый 3",
            "Контр-тренд ×2", "Контр-тренд 1", "Контр-тренд 2",
            "Маркет-мейкер 1",
            "Новостной ×3", "Новостной 1", "Новостной 2", "Новостной 3"
        ], titles);
    }

    [Fact]
    public void Группа_из_одного_экземпляра_строки_группы_не_получает()
    {
        var view = AgentRoster.Build(NineAgents(), []);

        var marketMakerRows = view.Rows.Where(row => row.Type == AgentType.MarketMaker).ToArray();

        var row = Assert.Single(marketMakerRows);
        Assert.Equal(AgentRowKind.Instance, row.Kind);
        Assert.Equal("Маркет-мейкер 1", row.Title);
    }

    [Fact]
    public void Единственный_экземпляр_типа_несёт_точку_цвета_сам()
    {
        var view = AgentRoster.Build(NineAgents(), []);

        var marketMaker = view.Rows.Single(row => row.Type == AgentType.MarketMaker);
        Assert.True(marketMaker.ShowTypeDot);

        // У типа со строкой группы точку несёт она, а не экземпляры.
        var trendRows = view.Rows.Where(row => row.Type == AgentType.TrendFollowing).ToArray();
        Assert.True(trendRows[0].ShowTypeDot);
        Assert.All(trendRows.Skip(1), row => Assert.False(row.ShowTypeDot));
    }

    [Fact]
    public void Суммы_строки_группы_равны_суммам_её_экземпляров_по_каждой_колонке()
    {
        AgentSnapshot[] agents =
        [
            Agent("A", AgentType.TrendFollowing, cash: 412_380m, position: 1_240m,
                  portfolio: 566_338m, initialPortfolio: 553_824.40m),
            Agent("B", AgentType.TrendFollowing, cash: 268_900m, position: 2_080m,
                  portfolio: 527_153m, initialPortfolio: 517_968.80m),
            Agent("C", AgentType.TrendFollowing, cash: 531_460m, position: -320m,
                  portfolio: 491_729m, initialPortfolio: 496_241.60m)
        ];

        var view = AgentRoster.Build(agents, []);

        var group = view.Rows.Single(row => row.Kind == AgentRowKind.Group);
        var instances = view.Rows.Where(row => row.Kind == AgentRowKind.Instance).ToArray();

        Assert.Equal(instances.Sum(row => row.Cash), group.Cash);
        Assert.Equal(instances.Sum(row => row.Position), group.Position);
        Assert.Equal(instances.Sum(row => row.Portfolio), group.Portfolio);
        Assert.Equal(instances.Sum(row => row.Pnl), group.Pnl);

        Assert.Equal(1_212_740m, group.Cash);
        Assert.Equal(3_000m, group.Position);
        Assert.Equal(1_585_220m, group.Portfolio);
    }

    [Fact]
    public void Итог_страницы_равен_сумме_строк_экземпляров()
    {
        AgentSnapshot[] agents =
        [
            Agent("A", AgentType.TrendFollowing, cash: 412_380m, portfolio: 566_338m,
                  initialPortfolio: 553_824.40m),
            Agent("B", AgentType.CounterTrend, cash: 486_220m, portfolio: 367_026m,
                  initialPortfolio: 373_144.40m)
        ];

        var view = AgentRoster.Build(agents, []);
        var instances = view.Rows.Where(row => row.Kind == AgentRowKind.Instance).ToArray();

        Assert.Equal(instances.Sum(row => row.Pnl), view.TotalPnl);
        Assert.Equal(instances.Sum(row => row.Portfolio), view.TotalPortfolio);
        Assert.Equal(instances.Sum(row => row.Cash), view.TotalCash);
    }

    [Fact]
    public void Дословно_совпавшее_объяснение_пишется_один_раз_в_строке_группы()
    {
        const string same = "новостей в сессии не было — сигнала нет, заявки не выставляются";

        var agents = NineAgents();
        AgentDecision[] decisions =
        [
            Decision("NewsDrivenAgent", TradeAction.Hold, same),
            Decision("NewsDrivenAgent 2", TradeAction.Hold, same),
            Decision("NewsDrivenAgent 3", TradeAction.Hold, same)
        ];

        var view = AgentRoster.Build(agents, decisions);
        var newsRows = view.Rows.Where(row => row.Type == AgentType.NewsDriven).ToArray();

        Assert.Equal(same, newsRows[0].Explanation);
        Assert.Null(newsRows[0].Action);

        // В строках экземпляров — прочерк, а не три одинаковые строки подряд.
        Assert.All(newsRows.Skip(1), row =>
        {
            Assert.Null(row.Explanation);
            Assert.Null(row.Action);
        });
    }

    [Fact]
    public void Разные_объяснения_остаются_в_строках_экземпляров()
    {
        var agents = NineAgents();
        AgentDecision[] decisions =
        [
            Decision("TrendFollowingAgent", TradeAction.Buy, "краткосрочная средняя выше долгосрочной"),
            Decision("TrendFollowingAgent 2", TradeAction.Buy, "цена держится выше средней за 20 шагов"),
            Decision("TrendFollowingAgent 3", TradeAction.Sell, "краткосрочная средняя ушла под долгосрочную")
        ];

        var view = AgentRoster.Build(agents, decisions);
        var trendRows = view.Rows.Where(row => row.Type == AgentType.TrendFollowing).ToArray();

        // Строка группы текстовых колонок не заполняет (раздел 9.8).
        Assert.Null(trendRows[0].Explanation);
        Assert.Null(trendRows[0].Action);

        Assert.Equal("покупает", trendRows[1].Action);
        Assert.Equal("краткосрочная средняя выше долгосрочной", trendRows[1].Explanation);
        Assert.Equal("продаёт", trendRows[3].Action);
    }

    [Fact]
    public void Совпадение_объяснений_не_склеивает_группу_из_одного_экземпляра()
    {
        AgentSnapshot[] agents = [Agent("MarketMakerAgent", AgentType.MarketMaker)];
        AgentDecision[] decisions = [Decision("MarketMakerAgent", TradeAction.Hold, "спред 0,08 ₽")];

        var view = AgentRoster.Build(agents, decisions);

        var row = Assert.Single(view.Rows);
        Assert.Equal(AgentRowKind.Instance, row.Kind);
        Assert.Equal("спред 0,08 ₽", row.Explanation);
    }

    [Fact]
    public void Пустое_объяснение_это_отсутствие_данных_а_не_текст()
    {
        AgentSnapshot[] agents =
        [
            Agent("A", AgentType.TrendFollowing),
            Agent("B", AgentType.TrendFollowing)
        ];
        AgentDecision[] decisions =
        [
            Decision("A", TradeAction.Buy, "   "),
            Decision("B", TradeAction.Buy, "")
        ];

        var view = AgentRoster.Build(agents, decisions);

        // Пустые тексты не считаются «дословно совпавшими»: склеивать в строку
        // группы нечего, а в экземплярах останется прочерк.
        var group = view.Rows.Single(row => row.Kind == AgentRowKind.Group);
        Assert.Null(group.Explanation);
        Assert.All(view.Rows.Where(row => row.Kind == AgentRowKind.Instance),
            row => Assert.Null(row.Explanation));
    }

    [Fact]
    public void Hold_с_заявками_по_обе_стороны_это_держит_спред()
    {
        var order = (OrderSide side) => new Order(
            Guid.NewGuid(), "MarketMakerAgent", side, OrderType.Limit, 124.12m, 100m, DateTimeOffset.Now);

        AgentSnapshot[] agents = [Agent("MarketMakerAgent", AgentType.MarketMaker)];
        AgentDecision[] both =
        [
            new("MarketMakerAgent", TradeAction.Hold, "заявки по обе стороны",
                [order(OrderSide.Buy), order(OrderSide.Sell)])
        ];

        Assert.Equal("держит спред", AgentRoster.Build(agents, both).Rows[0].Action);

        AgentDecision[] none = [new("MarketMakerAgent", TradeAction.Hold, "жду", [])];
        Assert.Equal("ждёт", AgentRoster.Build(agents, none).Rows[0].Action);
    }

    [Fact]
    public void Агент_без_решения_за_тик_действия_не_показывает()
    {
        AgentSnapshot[] agents = [Agent("A", AgentType.TrendFollowing)];

        var row = Assert.Single(AgentRoster.Build(agents, []).Rows);

        Assert.Null(row.Action);
        Assert.Null(row.Explanation);
    }

    [Fact]
    public void Торговали_за_сессию_считаются_по_отличию_денег_от_стартовых()
    {
        AgentSnapshot[] agents =
        [
            Agent("A", AgentType.TrendFollowing, cash: 98_000m, initialCash: 100_000m),
            Agent("B", AgentType.TrendFollowing, cash: 100_000m, initialCash: 100_000m),
            Agent("C", AgentType.NewsDriven, cash: 100_000m, initialCash: 100_000m)
        ];

        var view = AgentRoster.Build(agents, []);

        Assert.Equal(1, view.TradingCount);
        Assert.Equal(3, view.AgentCount);
        Assert.Equal(2, view.TypeCount);
    }

    [Fact]
    public void Пустой_состав_даёт_пустую_таблицу_а_не_исключение()
    {
        var view = AgentRoster.Build([], []);

        Assert.Empty(view.Rows);
        Assert.Equal(0, view.AgentCount);
        Assert.Equal(0m, view.TotalPnl);
    }

    [Fact]
    public void Ссылка_на_детальную_строится_по_внутреннему_имени_а_не_по_видимому()
    {
        var view = AgentRoster.Build(NineAgents(), []);

        var first = view.Rows.First(row => row.Kind == AgentRowKind.Instance);

        Assert.Equal("Трендовый 1", first.Title);
        Assert.Equal("TrendFollowingAgent", first.AgentName);

        // У строки группы внутреннего имени нет — она не кликается.
        Assert.Null(view.Rows.First(row => row.Kind == AgentRowKind.Group).AgentName);
    }

    [Fact]
    public void Имена_экземпляров_на_списке_и_на_детальной_совпадают()
    {
        var agents = NineAgents();
        var view = AgentRoster.Build(agents, []);

        foreach (var row in view.Rows.Where(row => row.Kind == AgentRowKind.Instance))
        {
            Assert.Equal(row.Title, AgentDisplay.GetInstanceName(agents, row.AgentName!));
        }
    }

    [Fact]
    public void Без_состава_сессии_показывается_внутреннее_имя_агента()
    {
        Assert.Equal("TrendFollowingAgent", AgentDisplay.GetInstanceName([], "TrendFollowingAgent"));
        Assert.Equal("Неизвестный", AgentDisplay.GetInstanceName(NineAgents(), "Неизвестный"));
    }
}
