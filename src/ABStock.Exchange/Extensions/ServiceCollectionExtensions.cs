using ABStock.Exchange.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ABStock.Exchange.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockExchange(this IServiceCollection services)
    {
        services.TryAddSingleton<IExchangeEngineFactory, ExchangeEngineFactory>();
        return services;
    }
}
