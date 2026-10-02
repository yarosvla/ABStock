using ABStock.Application.MarketHistory;
using ABStock.Shared;

namespace ABStock.UI.Services;

/// <summary>Рынок агента: тикер, цена открытия, текущая цена и история его прогона.</summary>
/// <param name="Report">Сделки агента и ряд цен в этом активе; null — история недоступна.</param>
public sealed record LedgerMarket(
    Guid AssetId,
    string Symbol,
    decimal Open,
    decimal Price,
    AgentStatisticsReport? Report);

/// <summary>Сделка агента с тикером — строка рельса «Сделки агента».</summary>
public sealed record LedgerTrade(DateTimeOffset At, string Symbol, OrderSide Side, decimal Price, decimal Quantity);

/// <param name="AverageEntryPrice">Средняя цена покупки бумаг в позиции; null — позиции нет.</param>
/// <param name="Realized">Зафиксированный P/L: проданные бумаги против их средней цены покупки.</param>
/// <param name="Unrealized">Открытый P/L: бумаги в позиции по текущей цене против их средней цены.</param>
/// <param name="Pnl">
/// P/L по активу — зафиксированный плюс открытый. Стартовый запас бэкенд
/// оценивает по цене открытия, поэтому сумма по активам — P/L всего портфеля.
/// </param>
public sealed record LedgerAsset(
    string Symbol,
    decimal Quantity,
    decimal Change,
    decimal Value,
    decimal? AverageEntryPrice,
    decimal Realized,
    decimal Unrealized,
    decimal Pnl,
    int Buys,
    int Sells);

public sealed record LedgerView(
    IReadOnlyList<LedgerTrade> Trades,
    IReadOnlyList<LedgerAsset> Assets,
    IReadOnlyList<ChartPoint> Equity,
    decimal InitialPortfolio,
    decimal Portfolio,
    decimal Pnl);

/// <summary>
/// Книга одного агента по всем активам сессии. У общего агента один кошелёк,
/// а история лежит по прогонам активов: здесь она сводится обратно в одно
/// целое — сделки с тикером, P/L по каждому активу и кривая капитала.
///
/// P/L по активу — из позиции бэкенда: он ведёт стоимость покупки по
/// каждой сделке, а не восстанавливает её по окну истории.
/// </summary>
public static class AgentLedger
{
    public static LedgerView Build(AgentAccountSnapshot account, IReadOnlyList<LedgerMarket> markets)
    {
        var trades = markets
            .SelectMany(market => (market.Report?.Trades ?? [])
                .Select(trade => new LedgerTrade(trade.ExecutedAt, market.Symbol, trade.Side, trade.Price, trade.Quantity)))
            .OrderByDescending(trade => trade.At)
            .ToArray();

        var positions = markets.Select(market => account.Positions.GetValueOrDefault(market.AssetId)).ToArray();

        // Итог по активам — копейками, которые сходятся с общим итогом. Если
        // округлять каждую строку отдельно, шесть активов расходятся с «Общим»
        // на несколько копеек, а таблицу на защите складывают первой (раздел 10.1).
        var exact = positions.Select(position => position?.TotalPnl ?? 0m).ToArray();
        var totals = Apportion(exact);

        var assets = markets.Select((market, index) => Asset(market, positions[index], totals[index])).ToArray();

        return new LedgerView(
            trades,
            assets,
            Equity(account, markets),
            Math.Round(account.InitialPortfolioValue, 0, MidpointRounding.AwayFromZero),
            Math.Round(account.PortfolioValue, 0, MidpointRounding.AwayFromZero),
            assets.Sum(asset => asset.Pnl));
    }

    /// <summary>
    /// Округление до копеек методом наибольших остатков: каждое значение
    /// округляется вниз, недостающие до округлённой суммы копейки получают
    /// строки с самыми большими отброшенными долями.
    /// </summary>
    private static decimal[] Apportion(IReadOnlyList<decimal> values)
    {
        var cents = values.Select(value => value * 100m).ToArray();
        var floors = cents.Select(decimal.Floor).ToArray();
        var missing = (int)(Math.Round(cents.Sum(), 0, MidpointRounding.AwayFromZero) - floors.Sum());

        foreach (var index in Enumerable.Range(0, cents.Length).OrderByDescending(index => cents[index] - floors[index]).Take(missing))
        {
            floors[index] += 1m;
        }

        return floors.Select(value => value / 100m).ToArray();
    }

    private static LedgerAsset Asset(LedgerMarket market, AgentPositionSnapshot? position, decimal pnl)
    {
        var quantity = position?.Quantity ?? 0m;
        var initial = position?.InitialQuantity ?? 0m;
        var records = market.Report?.Trades ?? [];

        // Строка тоже сходится: открытый P/L — итог строки минус зафиксированный.
        var realized = Round2(position?.RealizedPnl ?? 0m);

        return new LedgerAsset(
            market.Symbol,
            Math.Round(quantity, 1, MidpointRounding.AwayFromZero),
            Math.Round(quantity - initial, 1, MidpointRounding.AwayFromZero),
            Math.Round(quantity * market.Price, 0, MidpointRounding.AwayFromZero),
            position?.AverageEntryPrice is { } average ? Round2(average) : null,
            realized,
            pnl - realized,
            pnl,
            records.Count(trade => trade.Side == OrderSide.Buy),
            records.Count(trade => trade.Side == OrderSide.Sell));
    }

    private static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Капитал во времени: деньги плюс бумаги всех активов по ценам на тот
    /// момент. Шкала времени — объединение рядов цен всех прогонов; цена
    /// актива между его точками — последняя известная.
    /// </summary>
    private static IReadOnlyList<ChartPoint> Equity(AgentAccountSnapshot account, IReadOnlyList<LedgerMarket> markets)
    {
        var allTrades = markets
            .SelectMany(market => (market.Report?.Trades ?? []).Select(trade => (market.AssetId, Trade: trade)))
            .OrderBy(item => item.Trade.ExecutedAt)
            .ToArray();

        // Стартовые деньги — нынешние минус всё, что сделки принесли.
        var cash = account.Cash - allTrades.Sum(item =>
            (item.Trade.Side == OrderSide.Sell ? 1m : -1m) * item.Trade.Price * item.Trade.Quantity);

        var positions = markets.ToDictionary(
            market => market.AssetId,
            market => account.Positions.GetValueOrDefault(market.AssetId)?.InitialQuantity ?? 0m);
        var prices = markets.ToDictionary(market => market.AssetId, market => market.Open);

        var timeline = markets
            .SelectMany(market => (market.Report?.PriceSeries ?? []).Select(point => (market.AssetId, point.Time, point.Price)))
            .OrderBy(point => point.Time)
            .ToArray();

        var points = new List<ChartPoint>(timeline.Length);
        var tradeIndex = 0;

        foreach (var (assetId, time, price) in timeline)
        {
            while (tradeIndex < allTrades.Length && allTrades[tradeIndex].Trade.ExecutedAt <= time)
            {
                var (tradeAsset, trade) = allTrades[tradeIndex];
                var sign = trade.Side == OrderSide.Buy ? 1m : -1m;
                cash -= sign * trade.Price * trade.Quantity;
                positions[tradeAsset] += sign * trade.Quantity;
                tradeIndex++;
            }

            prices[assetId] = price;
            var equity = cash + positions.Sum(pair => pair.Value * prices[pair.Key]);
            points.Add(new ChartPoint(time.ToUnixTimeSeconds(), equity));
        }

        return ChartSeries.DedupeBySecond(points);
    }
}
