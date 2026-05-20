namespace ABStock.Shared;

public record Order(
    Guid Id,
    string AgentName,
    OrderSide Side,
    OrderType Type,
    decimal Price,
    int Quantity,
    DateTimeOffset CreatedAt
);