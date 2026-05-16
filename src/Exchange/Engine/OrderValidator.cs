using ABStock.Exchange.Domain;

namespace ABStock.Exchange.Engine;

public sealed class OrderValidator
{
    public void Validate(Order? order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (string.IsNullOrWhiteSpace(order.Id))
        {
            throw new ArgumentException("Order id is required.", nameof(order));
        }

        if (string.IsNullOrWhiteSpace(order.AgentId))
        {
            throw new ArgumentException("Agent id is required.", nameof(order));
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

        if (order.Type == OrderType.Market)
        {
            throw new NotSupportedException("Market orders are not supported yet. Use a limit order.");
        }

        ValidateLimitOrder(order);
    }

    private static void ValidateLimitOrder(Order order)
    {
        if (order.LimitPrice is null)
        {
            throw new ArgumentException("Limit order price is required.", nameof(order));
        }

        if (order.LimitPrice <= 0)
        {
            throw new ArgumentException("Limit order price must be positive.", nameof(order));
        }
    }
}
