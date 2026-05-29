using ABStock.Shared;

namespace ABStock.Exchange.Engine;

public sealed class OrderValidator
{
    public void Validate(Order? order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (order.Id == Guid.Empty)
        {
            throw new ArgumentException("Order id is required.", nameof(order));
        }

        if (string.IsNullOrWhiteSpace(order.AgentName))
        {
            throw new ArgumentException("Agent name is required.", nameof(order));
        }

        if (!Enum.IsDefined(order.Side))
        {
            throw new ArgumentException("Order side is invalid.", nameof(order));
        }

        if (!Enum.IsDefined(order.Type))
        {
            throw new ArgumentException("Order type is invalid.", nameof(order));
        }

        if (order.Quantity <= 0)
        {
            throw new ArgumentException("Order quantity must be positive.", nameof(order));
        }

        if (order.Type == OrderType.Limit)
        {
            ValidateLimitOrder(order);
            return;
        }

        ValidateMarketOrder(order);
    }

    private static void ValidateLimitOrder(Order order)
    {
        if (order.Price is null)
        {
            throw new ArgumentException("Limit order price is required.", nameof(order));
        }

        if (order.Price <= 0)
        {
            throw new ArgumentException("Limit order price must be positive.", nameof(order));
        }
    }

    private static void ValidateMarketOrder(Order order)
    {
        if (order.Price is not null)
        {
            throw new ArgumentException("Market order price must be empty.", nameof(order));
        }
    }
}
