using ABStock.Application.Simulation;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockApplication(this IServiceCollection services)
    {
        services.AddSingleton<ISimulationRunner, SimulationRunner>();
        return services;
        

    }
}
