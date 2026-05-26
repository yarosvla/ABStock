using ABStock.Application.Simulation;
using ABStock.Exchange.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockApplication(this IServiceCollection services)
    {
        services.AddABStockExchange();
        services.AddSingleton<ISimulationRunner, SimulationRunner>();
        return services;
    }
}
