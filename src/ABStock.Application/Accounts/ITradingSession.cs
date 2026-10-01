using ABStock.Application.Markets;
using ABStock.Shared;

namespace ABStock.Application.Accounts;

public interface ITradingSession : IMarketSession
{
    event Action<IReadOnlyList<AgentAccountSnapshot>>? OnAccountsChanged;

    AgentAccountSnapshot AddAgent(AgentAccountSpec spec);

    AgentAccountSnapshot GetAgentAccount(string agentName);

    IReadOnlyList<AgentAccountSnapshot> GetAgentAccounts();
}
