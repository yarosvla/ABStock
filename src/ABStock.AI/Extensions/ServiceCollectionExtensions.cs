using ABStock.AI.Internal;
using ABStock.AI.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockAI(this IServiceCollection services)
    {
        services.AddSingleton<INewsProcessingService>(
            _ => new NewsProcessingService(new StubFinBertAnalyzer(), new ProfileAspectMatcher()));
        services.AddSingleton<IAssetProfileService, AssetProfileService>();
        return services;
    }
}
