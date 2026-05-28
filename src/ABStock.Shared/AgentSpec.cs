namespace ABStock.Shared;

public record AgentSpec(AgentType Type, decimal InitialCash, decimal InitialPosition = 0);