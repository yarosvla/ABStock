using ABStock.Application.Simulation;
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
        services.TryAddSingleton<ISimulationRunner, SimulationRunner>();
        return services;
    }
}
