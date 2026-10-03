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
    public decimal RealizedPnl => Positions.Values.Sum(position => position.RealizedPnl);

    public decimal UnrealizedPnl => Positions.Values.Sum(position => position.UnrealizedPnl);

    public decimal TotalPnl => RealizedPnl + UnrealizedPnl;

    public decimal AvailableCash => Cash - ReservedCash;
}
