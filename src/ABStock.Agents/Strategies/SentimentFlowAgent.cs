using ABStock.Shared;

namespace ABStock.Agents.Strategies;

/// <summary>
/// Models aggregate, exogenous market sentiment (net retail/uninformed order flow).
/// Each tick it derives a target price from a smooth multi-period wave plus a small
/// random walk and takes liquidity in that direction, producing organic rises and
/// falls in the price series instead of a flat, mean-reverting line. Used as the
/// ambient demand driver of the demo simulation; it is hidden from the trading UI.
/// </summary>
public class SentimentFlowAgent : AgentBase
{
    /// <summary>Stable name of the single driver instance, used by the UI to filter it out.</summary>
    public const string AgentName = AgentNames.SentimentFlow;

    // Wave shape: a long swing for the macro trend plus a shorter swing for texture.
    private const decimal PrimaryAmplitude = 0.075m;   // ~7.5% macro swing
    private const decimal SecondaryAmplitude = 0.03m;  // ~3% texture
    private const double PrimaryPeriod = 56d;          // ticks per macro cycle
    private const double SecondaryPeriod = 21d;        // ticks per texture cycle
    private const decimal NoiseStep = 0.0035m;         // per-tick random-walk magnitude
    private const decimal NoiseLimit = 0.04m;          // clamp for the random walk
    private const decimal LevelStep = 0.0025m;         // matches MarketMaker ladder spacing
    private const decimal MaxOrderQuantity = 13m;

    private readonly Random _random;
    private decimal _baselinePrice;
    private decimal _noise;
    private long _tick;

    public SentimentFlowAgent(decimal initialCash, decimal initialPosition = 0, int? seed = 12345)
        : base(AgentName, AgentType.SentimentFlow, initialCash, initialPosition)
    {
        _random = seed is { } s ? new Random(s) : new Random();
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        if (_baselinePrice <= 0m)
        {
            _baselinePrice = snapshot.LastPrice;
        }

        _tick++;
        _noise = Math.Clamp(_noise + ((decimal)_random.NextDouble() - 0.5m) * 2m * NoiseStep, -NoiseLimit, NoiseLimit);

        var primary = PrimaryAmplitude * (decimal)Math.Sin(2d * Math.PI * _tick / PrimaryPeriod);
        var secondary = SecondaryAmplitude * (decimal)Math.Sin(2d * Math.PI * _tick / SecondaryPeriod + 0.6d);
        var target = _baselinePrice * (1m + primary + secondary + _noise);

        var lastPrice = snapshot.LastPrice;
        var gap = target - lastPrice;
        var threshold = lastPrice * 0.0015m;

        if (Math.Abs(gap) <= threshold)
        {
            return HoldDecision($"Within band of target {target:F2}");
        }

        var quantity = QuantityFor(gap, lastPrice);

        if (gap > 0m)
        {
            var referenceAsk = snapshot.BestAsk ?? lastPrice;
            if (!CanBuy(referenceAsk, quantity))
            {
                return HoldDecision("Bullish sentiment, but insufficient funds");
            }

            var order = CreateMarketOrder(OrderSide.Buy, quantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"Sentiment lifting price toward {target:F2}", [order]);
        }

        if (!CanSell(quantity))
        {
            return HoldDecision("Bearish sentiment, but insufficient position");
        }

        var sellOrder = CreateMarketOrder(OrderSide.Sell, quantity);
        return new AgentDecision(State.AgentName, TradeAction.Sell,
            $"Sentiment pulling price toward {target:F2}", [sellOrder]);
    }

    // Size the order to the number of MarketMaker ladder levels the gap spans, so the
    // price approaches the target over a few ticks rather than jumping in one candle.
    private static decimal QuantityFor(decimal gap, decimal lastPrice)
    {
        if (lastPrice <= 0m)
        {
            return 1m;
        }

        var levelsToCover = Math.Abs(gap) / lastPrice / LevelStep;
        var quantity = Math.Ceiling(levelsToCover * 1.5m);
        return Math.Clamp(quantity, 1m, MaxOrderQuantity);
    }
}
