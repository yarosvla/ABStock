using ABStock.Shared;

namespace ABStock.Application.Simulation.Diagnostics;

public sealed record SimulationDebugOrderRequest(
    string AgentName,
    OrderSide Side,
    OrderType Type,
    decimal? Price,
    decimal Quantity
);
