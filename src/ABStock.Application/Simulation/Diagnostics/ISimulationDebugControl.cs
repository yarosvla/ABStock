using ABStock.Shared;

namespace ABStock.Application.Simulation.Diagnostics;

public interface ISimulationDebugControl
{
    SubmitResult SubmitOrder(SimulationDebugOrderRequest request);

    AgentSnapshot AddAgent(AgentSpec spec);
}
