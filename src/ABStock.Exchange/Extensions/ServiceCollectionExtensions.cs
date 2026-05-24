using ABStock.Exchange.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Exchange.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockExchange(this IServiceCollection services)
    {
        services.AddSingleton<IExchangeEngineFactory, ExchangeEngineFactory>();
        return services;
    }
}
