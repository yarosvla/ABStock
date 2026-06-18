using ABStock.Application.Simulation;
using ABStock.Application.Simulation.Diagnostics;
using ABStock.Application.MarketHistory;
using ABStock.Agents;
using ABStock.Exchange.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ABStock.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockApplication(this IServiceCollection services)
    {
        services.AddABStockExchange();
        services.TryAddSingleton<IAgentFactory, AgentFactory>();
        services.TryAddSingleton<IMarketHistoryStore, NullMarketHistoryStore>();
        services.TryAddSingleton<IMarketCandleReader, NullMarketCandleReader>();
        services.TryAddSingleton<ISimulationHistoryReader, NullSimulationHistoryReader>();
        services.TryAddSingleton<IAgentStatisticsReader, NullAgentStatisticsReader>();
        services.TryAddSingleton<SimulationRunner>();
        services.TryAddSingleton<ISimulationRunner>(provider => provider.GetRequiredService<SimulationRunner>());
        return services;
    }

    public static IServiceCollection AddABStockSimulationDiagnostics(this IServiceCollection services)
    {
        services.RemoveAll<ISimulationRunner>();
        services.TryAddSingleton<DebugSimulationRunner>();
        services.TryAddSingleton<ISimulationRunner>(provider => provider.GetRequiredService<DebugSimulationRunner>());
        services.TryAddSingleton<ISimulationDebugControl>(provider => provider.GetRequiredService<DebugSimulationRunner>());
        return services;
    }
}
