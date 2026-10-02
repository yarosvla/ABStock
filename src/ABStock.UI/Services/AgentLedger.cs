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

/// <param name="Pnl">
/// P/L по активу: сколько стоит позиция сейчас минус сколько стоил стартовый
/// запас по цене открытия, плюс деньги, полученные и потраченные на сделках
/// этим активом. Сумма по активам — P/L всего портфеля: деньги агента
/// меняются только сделками.
/// </param>
public sealed record LedgerAsset(
    string Symbol,
    decimal Quantity,
    decimal Change,
    decimal Value,
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
/// P/L по активу выводится из сделок и стартового запаса, а не берётся у
/// бэкенда: контракт его пока не отдаёт, а из истории он честно считается.
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

        var assets = markets.Select(market => Asset(account, market)).ToArray();

        return new LedgerView(
            trades,
            assets,
            Equity(account, markets),
            Math.Round(account.InitialPortfolioValue, 0, MidpointRounding.AwayFromZero),
            Math.Round(account.PortfolioValue, 0, MidpointRounding.AwayFromZero),
            Math.Round(account.PortfolioValue - account.InitialPortfolioValue, 2, MidpointRounding.AwayFromZero));
    }

    private static LedgerAsset Asset(AgentAccountSnapshot account, LedgerMarket market)
    {
        var position = account.Positions.GetValueOrDefault(market.AssetId);
        var quantity = position?.Quantity ?? 0m;
        var initial = position?.InitialQuantity ?? 0m;
        var records = market.Report?.Trades ?? [];

        // Деньги, которые принесли сделки этим активом: продажи плюс, покупки минус.
        var cashFlow = records.Sum(trade => (trade.Side == OrderSide.Sell ? 1m : -1m) * trade.Price * trade.Quantity);
        var pnl = quantity * market.Price - initial * market.Open + cashFlow;

        return new LedgerAsset(
            market.Symbol,
            Math.Round(quantity, 1, MidpointRounding.AwayFromZero),
            Math.Round(quantity - initial, 1, MidpointRounding.AwayFromZero),
            Math.Round(quantity * market.Price, 0, MidpointRounding.AwayFromZero),
            Math.Round(pnl, 2, MidpointRounding.AwayFromZero),
            records.Count(trade => trade.Side == OrderSide.Buy),
            records.Count(trade => trade.Side == OrderSide.Sell));
    }

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
