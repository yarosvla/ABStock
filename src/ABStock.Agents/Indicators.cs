namespace ABStock.Agents;

public static class Indicators
{
    public static decimal Sma(IReadOnlyList<decimal> prices, int period)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(period);

        var count = Math.Min(period, prices.Count);
        if (count == 0)
        {
            return 0m;
        }

        var sum = 0m;
        for (var index = prices.Count - count; index < prices.Count; index++)
        {
            sum += prices[index];
        }

        return sum / count;
    }
}
