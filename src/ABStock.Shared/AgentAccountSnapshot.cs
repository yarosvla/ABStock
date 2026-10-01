namespace ABStock.Shared;

public sealed record AgentAccountSnapshot(
    string AgentName,
    AgentType AgentType,
    decimal Cash,
    decimal ReservedCash,
    decimal InitialCash,
    decimal PortfolioValue,
    decimal InitialPortfolioValue,
    IReadOnlyDictionary<Guid, AgentPositionSnapshot> Positions
)
{
    public decimal AvailableCash => Cash - ReservedCash;
}
