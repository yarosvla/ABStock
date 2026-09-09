using ABStock.Application.MarketHistory;
using ABStock.Shared;
using Microsoft.EntityFrameworkCore;

namespace ABStock.Persistence.MarketHistory;

internal sealed class EfAgentStatisticsReader(IDbContextFactory<AbStockDbContext> contextFactory) : IAgentStatisticsReader
{
    public AgentStatisticsReport? GetReport(Guid runId, string agentName)
    {
        if (runId == Guid.Empty || string.IsNullOrWhiteSpace(agentName))
        {
            return null;
        }

        using var db = contextFactory.CreateDbContext();

        var tradeEntities = db.Trades
            .AsNoTracking()
            .Where(trade => trade.SimulationRunId == runId
                && (trade.BuyerAgentName == agentName || trade.SellerAgentName == agentName))
            .AsEnumerable()
            .OrderBy(trade => trade.ExecutedAt)
            .ToArray();

        var trades = new List<AgentTradeRecord>(tradeEntities.Length);
        foreach (var trade in tradeEntities)
        {
            if (trade.BuyerAgentName == agentName)
            {
                trades.Add(new AgentTradeRecord(trade.ExecutedAt, OrderSide.Buy, trade.Price, trade.Quantity));
            }

            if (trade.SellerAgentName == agentName)
            {
                trades.Add(new AgentTradeRecord(trade.ExecutedAt, OrderSide.Sell, trade.Price, trade.Quantity));
            }
        }

        var priceSeries = db.MarketTicks
            .AsNoTracking()
            .Where(tick => tick.SimulationRunId == runId)
            .AsEnumerable()
            .OrderBy(tick => tick.CapturedAt)
            .Select(tick => new AgentPricePoint(tick.CapturedAt, tick.LastPrice, tick.TotalVolume))
            .ToArray();

        return new AgentStatisticsReport(agentName, trades, priceSeries);
    }
}
