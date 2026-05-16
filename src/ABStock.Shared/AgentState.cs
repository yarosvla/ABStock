namespace ABStock.Shared;

public record AgentState(
    string AgentName,
    AgentType AgentType,
    decimal Cash,
    int Position
);