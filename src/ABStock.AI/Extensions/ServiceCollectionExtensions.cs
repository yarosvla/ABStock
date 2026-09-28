using ABStock.AI.Internal;
using ABStock.AI.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.AI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockAI(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient();

        services.AddSingleton<IFinBertAnalyzer,
            RealFinBertAnalyzer>();

        services.AddSingleton<IFactorMatcher,
            RealFactorMatcher>();

        var apiKey =
            Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? configuration["OpenAI:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not configured.");
        }

        services.AddSingleton<IEmbeddingService>(
            _ => new OpenAIEmbeddingService(apiKey));

        services.AddSingleton<INewsProcessingService,
            NewsProcessingService>();

        services.AddSingleton<IAssetProfileService,
            GptAssetProfileService>();
            
        services.AddSingleton<IProfilePromptBuilder,
            ProfilePromptBuilder>();

        return services;
    }
}
