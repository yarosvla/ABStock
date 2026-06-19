using ABStock.AI.Internal;
using ABStock.AI.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockAI(this IServiceCollection services, IConfiguration configuration)
    {
        /*
        services.AddSingleton<INewsProcessingService>(
            _ => new NewsProcessingService(new StubFinBertAnalyzer(), new StubAspectMatcher()));
        services.AddSingleton<IAssetProfileService, AssetProfileService>();
        */
        services.AddSingleton<IFinBertAnalyzer,
            RealFinBertAnalyzer>();

        services.AddSingleton<IFactorMatcher,
            RealFactorMatcher>();

        services.AddSingleton<IEmbeddingService>(
            _ => new OpenAIEmbeddingService(
                configuration["OpenAI:ApiKey"]!));

        services.AddSingleton<INewsProcessingService,
            NewsProcessingService>();

        services.AddSingleton<IAssetProfileService,
            GptAssetProfileService>();
            
        services.AddSingleton<IProfilePromptBuilder,
            ProfilePromptBuilder>();

        return services;
    }
}
