using ABStock.Shared;

namespace ABStock.Exchange.Tests;

/// <summary>
/// Инвариант отображения стакана (DESIGN.md 11): лучший бид строго меньше
/// лучшего аска, спред строго больше нуля при непустых сторонах.
/// Нарушение — дефект отображения, а не свойство рынка.
/// </summary>
public sealed class OrderBookDisplayTests
{
    [Theory]
    [InlineData(124.971, true, 124.98)]   // аск округляется ВВЕРХ
    [InlineData(124.971, false, 124.97)]  // бид округляется ВНИЗ
    [InlineData(124.970, true, 124.97)]   // точное значение не сдвигается
    [InlineData(124.970, false, 124.97)]
    public void ToDisplayPrice_RoundsOutwardFromSpread(decimal price, bool isAsk, decimal expected) =>
        Assert.Equal(expected, OrderBookDisplay.ToDisplayPrice(price, isAsk));

    [Fact]
    public void Aggregate_CollapsesLevelsSharingDisplayPrice()
    {
        IReadOnlyList<OrderBookLevel> levels =
        [
            new(124.971m, 2m, 1),
            new(124.975m, 3m, 2),
            new(124.990m, 1m, 1)
        ];

        var result = OrderBookDisplay.Aggregate(levels, isAsk: true);

        Assert.Equal(2, result.Count);
        Assert.Equal(124.98m, result[0].Price);
        Assert.Equal(5m, result[0].Quantity);
        Assert.Equal(3, result[0].OrdersCount);
    }

    [Theory]
    // Уровни по разные стороны спреда, различающиеся в третьем знаке:
    // именно они схлопывались арифметическим округлением в одну цену.
    [InlineData(124.9701, 124.9749)]
    [InlineData(124.9749, 124.9751)]
    [InlineData(100.0001, 100.0099)]
    public void Aggregate_KeepsBookUncrossed(decimal rawBid, decimal rawAsk)
    {
        Assert.True(rawBid < rawAsk, "предусловие: сырой стакан не пересечён");

        var bids = OrderBookDisplay.Aggregate([new(rawBid, 1m, 1)], isAsk: false);
        var asks = OrderBookDisplay.Aggregate([new(rawAsk, 1m, 1)], isAsk: true);

        var bestBid = bids[0].Price;
        var bestAsk = asks[0].Price;

        Assert.True(bestBid < bestAsk,
            $"стакан пересечён: бид {bestBid} не меньше аска {bestAsk}");
        Assert.True(bestAsk - bestBid > 0m,
            $"спред не больше нуля: {bestAsk - bestBid}");
    }

    [Fact]
    public void Aggregate_PreservesTotals()
    {
        IReadOnlyList<OrderBookLevel> levels =
        [
            new(124.971m, 2.5m, 1),
            new(124.975m, 3.5m, 2),
            new(125.010m, 4m, 3)
        ];

        var result = OrderBookDisplay.Aggregate(levels, isAsk: true);

        Assert.Equal(levels.Sum(l => l.Quantity), result.Sum(l => l.Quantity));
        Assert.Equal(levels.Sum(l => l.OrdersCount), result.Sum(l => l.OrdersCount));
    }
}
