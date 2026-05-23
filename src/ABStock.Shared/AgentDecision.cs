namespace ABStock.Shared;

public record AgentDecision(
    string AgentName,
    TradeAction Action,
    string Explanation,
    IReadOnlyList<Order> Orders
);