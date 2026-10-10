using ABStock.Shared;

namespace ABStock.Agents.Strategies;

public class NewsDrivenAgent : AgentBase
{
    private const decimal MinimumMatchScore = 0.60m;
    private const decimal MinimumConviction = 0.10m;

    /// <summary>
    /// Сколько шагов новость остаётся в игре. Реакция в один шаг объёмом в одну
    /// бумагу тонула в лестнице маркет-мейкера: цена после сильной новости не
    /// сдвигалась вовсе. Теперь агент отыгрывает новость несколько шагов,
    /// и с каждым шагом всё меньшим объёмом.
    /// </summary>
    private const int NewsHorizon = 10;

    /// <summary>Объём первого шага на единицу множителя уверенности.</summary>
    private const decimal NewsQuantityFactor = 3m;

    private readonly decimal _orderQuantity;
    private readonly Dictionary<Guid, (NewsSignal Signal, int Age)> _active = [];

    public NewsDrivenAgent(decimal initialCash, decimal initialPosition = 0, decimal orderQuantity = 1m)
        : base("NewsDriven", AgentType.NewsDriven, initialCash, initialPosition)
    {
        _orderQuantity = orderQuantity;
    }

    public override AgentDecision Decide(MarketSnapshot snapshot, NewsSignal? newsSignal)
    {
        if (newsSignal is null)
            return HoldDecision("новостей в сессии не было — сигнала нет, заявки не выставляются");

        if (newsSignal.Polarity == SignalPolarity.Positive)
        {
            if (snapshot.BestAsk is null)
                return HoldDecision("новость позитивная, но покупать не у кого — асков нет");

            if (!CanBuy(snapshot.BestAsk.Value, _orderQuantity))
                return HoldDecision("новость позитивная, но денег на покупку не хватает");

            var order = CreateMarketOrder(OrderSide.Buy, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"позитивная новость, уверенность {newsSignal.Confidence:F2} — покупаю по рынку", [order])
            {
                NewsId = newsSignal.NewsId
            };
        }

        if (newsSignal.Polarity == SignalPolarity.Negative)
        {
            if (snapshot.BestBid is null)
                return HoldDecision("новость негативная, но продавать некому — бидов нет");

            if (!CanSell(_orderQuantity))
                return HoldDecision("новость негативная, но позиции для продажи нет");

            var order = CreateMarketOrder(OrderSide.Sell, _orderQuantity);
            return new AgentDecision(State.AgentName, TradeAction.Sell,
                $"негативная новость, уверенность {newsSignal.Confidence:F2} — продаю по рынку", [order])
            {
                NewsId = newsSignal.NewsId
            };
        }

        return HoldDecision($"новость нейтральная — держу позицию");
    }

    public override AgentDecision Decide(AgentMarketContext context, NewsSignal? newsSignal)
    {
        if (context.Asset is not { } asset)
        {
            return Decide(context.Snapshot, newsSignal);
        }

        var symbol = AssetStrategyPolicy.Symbol(asset);
        var age = 0;
        if (newsSignal is not null)
        {
            _active[asset.AssetId] = (newsSignal, 0);
        }
        else if (_active.TryGetValue(asset.AssetId, out var active) && active.Age + 1 < NewsHorizon)
        {
            age = active.Age + 1;
            newsSignal = active.Signal;
            _active[asset.AssetId] = (newsSignal, age);
        }
        else
        {
            return HoldDecision(active.Signal is null
                ? $"{symbol}: новостей для актива не было — сигналов нет"
                : $"{symbol}: новость отыграна — жду следующей");
        }

        if (newsSignal.MatchScore < MinimumMatchScore)
        {
            return HoldDecision(
                $"{symbol}: совпадение с профилем {newsSignal.MatchScore:P0} ниже порога {MinimumMatchScore:P0} — жду");
        }

        var conviction = Math.Abs(newsSignal.ImpactScore) * Math.Clamp(newsSignal.MatchScore, 0m, 1m);
        if (conviction < MinimumConviction || newsSignal.ImpactScore == 0m)
        {
            return HoldDecision(
                $"{symbol}: влияние {newsSignal.ImpactScore:+0.00;-0.00;0.00} при совпадении {newsSignal.MatchScore:P0} слишком слабое — жду");
        }

        // Первый шаг — полный объём, дальше затухание до пятой части.
        var decay = Math.Max(0.2m, 1m - (decimal)age / NewsHorizon);
        var quantity = Math.Max(0.01m, Math.Round(
            _orderQuantity * NewsQuantityFactor * ConvictionMultiplier(conviction) * decay, 2,
            MidpointRounding.AwayFromZero));
        var when = age == 0 ? string.Empty : $"новость {age} {StepsWord(age)} назад, ";

        if (newsSignal.ImpactScore > 0m)
        {
            if (context.Snapshot.BestAsk is null)
            {
                return HoldDecision($"{symbol}: влияние положительное, но покупать не у кого — асков нет");
            }

            if (!CanBuy(context.Snapshot.BestAsk.Value, quantity))
            {
                return HoldDecision($"{symbol}: влияние положительное, но доступных денег на {quantity:F2} не хватает");
            }

            var order = CreateMarketOrder(OrderSide.Buy, quantity);
            return new AgentDecision(State.AgentName, TradeAction.Buy,
                $"{symbol}: {when}влияние {newsSignal.ImpactScore:+0.00;-0.00;0.00}, совпадение {newsSignal.MatchScore:P0} — покупаю {quantity:F2} по рынку",
                [order])
            {
                NewsId = newsSignal.NewsId
            };
        }

        if (context.Snapshot.BestBid is null)
        {
            return HoldDecision($"{symbol}: влияние отрицательное, но продавать некому — бидов нет");
        }

        if (!CanSell(quantity))
        {
            return HoldDecision($"{symbol}: влияние отрицательное, но доступной позиции на {quantity:F2} не хватает");
        }

        var sellOrder = CreateMarketOrder(OrderSide.Sell, quantity);
        return new AgentDecision(State.AgentName, TradeAction.Sell,
            $"{symbol}: {when}влияние {newsSignal.ImpactScore:+0.00;-0.00;0.00}, совпадение {newsSignal.MatchScore:P0} — продаю {quantity:F2} по рынку",
            [sellOrder])
        {
            NewsId = newsSignal.NewsId
        };
    }

    private static string StepsWord(int n) => (n % 10, n % 100) switch
    {
        (1, not 11) => "шаг",
        (2 or 3 or 4, not (12 or 13 or 14)) => "шага",
        _ => "шагов"
    };

    /// <summary>
    /// Плавно, а не ступенями: при ступенях актив, задетый вдвое слабее,
    /// попадал в ту же ступень, получал тот же объём и на экране мог вырасти
    /// сильнее «главного» — веер новости и график противоречили друг другу.
    /// </summary>
    private static decimal ConvictionMultiplier(decimal conviction) =>
        Math.Clamp(conviction * 2m, 0.3m, 2.5m);
}
