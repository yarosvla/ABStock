using ABStock.Shared;

namespace ABStock.Exchange.Engine;

public interface IExchangeEngine
{
    MarketSnapshot Submit(Order order);

    MarketSnapshot SubmitMany(IEnumerable<Order> orders);

    SubmitResult SubmitWithResult(Order order);

    SubmitResult SubmitManyWithResult(IEnumerable<Order> orders);

    MarketSnapshot GetSnapshot();

    OrderBookSnapshot GetOrderBookSnapshot(int depth = 5);
}
