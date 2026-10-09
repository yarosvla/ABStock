using ABStock.Shared;

namespace ABStock.Agents.Strategies;

/// <summary>
/// Пульс актива глазами одного агента: каким было последнее изменение цены
/// и сколько шагов подряд по активу не было сделок.
/// </summary>
/// <remarks>
/// Движение считается от последней <b>другой</b> цены, а не от предпоследней
/// сделки: несколько сделок одного шага по одной цене дают «0 %», и агенты,
/// которые ждут движения, ждали бы его вечно — рынок замирал через полминуты.
/// По той же причине тишина не может длиться бесконечно: после нескольких
/// шагов без сделок агент пробует рынок малым объёмом, и движение снова есть.
/// </remarks>
internal sealed class MomentumPulse
{
    private const double Participation = 0.65;

    private const int DefaultPatience = 4;

    private readonly Dictionary<Guid, (long Trades, int Idle)> _byAsset = [];
    private readonly int _patience;
    private readonly Random? _random;

    /// <param name="random">
    /// Источник разнородности экземпляра. Без него агент детерминирован: входит
    /// по каждому сигналу, объём без разброса, терпение — <see cref="DefaultPatience"/> шага.
    /// </param>
    public MomentumPulse(Random? random)
    {
        _random = random;
        // У каждого экземпляра своё терпение: 3–6 шагов.
        _patience = random is null ? DefaultPatience : 3 + random.Next(4);
    }

    /// <summary>
    /// Входит ли агент по сигналу в этот шаг. Одинаковые агенты, которые
    /// реагируют на каждый сигнал разом, превращают цену в качели между двумя
    /// уровнями; часть пропущенных шагов делает толпу разнородной.
    /// </summary>
    public bool Acts() => _random is null || _random.NextDouble() < Participation;

    /// <summary>Объём с разбросом ±40 % — сделки разного размера, как у живых участников.</summary>
    public decimal Jitter(decimal quantity) => _random is null
        ? quantity
        : Math.Max(0.01m, Math.Round(quantity * (decimal)(0.6 + _random.NextDouble() * 0.8), 2, MidpointRounding.AwayFromZero));

    /// <summary>Направление пробы, когда цена ещё ни разу не менялась.</summary>
    public bool CoinFlip() => _random is null || _random.NextDouble() < 0.5;

    /// <summary>Учитывает новый шаг и возвращает число шагов подряд без сделок.</summary>
    public int Observe(Guid assetId, MarketSnapshot snapshot)
    {
        var idle = _byAsset.TryGetValue(assetId, out var seen) && seen.Trades == snapshot.TotalTradeCount
            ? seen.Idle + 1
            : 0;
        _byAsset[assetId] = (snapshot.TotalTradeCount, idle);
        return idle;
    }

    public bool IsRestless(int idle) => idle >= _patience;

    /// <summary>
    /// Предыдущая отличающаяся цена и последняя. null — цена в окне не менялась.
    /// </summary>
    public static (decimal From, decimal To)? LastMove(MarketSnapshot snapshot)
    {
        var prices = snapshot.RecentPrices;
        if (prices.Count < 2)
        {
            return null;
        }

        var last = prices[^1];
        for (var i = prices.Count - 2; i >= 0; i--)
        {
            if (prices[i] != last)
            {
                return (prices[i], last);
            }
        }

        return null;
    }

    public static decimal Percent((decimal From, decimal To) move) =>
        move.From == 0m ? 0m : (move.To - move.From) / move.From * 100m;
}
