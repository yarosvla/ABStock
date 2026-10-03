using ABStock.AI.Internal;
using ABStock.AI.Services;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using DotNetEnv;

namespace ABStock.AI.Extensions;

public static class ServiceCollectionExtensions
{
    private const string DefaultServiceUrl = "http://127.0.0.1:8000/";

    public static IServiceCollection AddABStockAI(this IServiceCollection services, IConfiguration configuration)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var envPath = Path.Combine(directory.FullName, ".env");

            if (File.Exists(envPath))
            {
                Env.Load(envPath);
                break;
            }

            directory = directory.Parent;
        }

        var apiKey =
            Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? configuration["OpenAI:ApiKey"];

        var settings = new OpenAISettings
        {
            ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            // Слэш в конце обязателен: без него относительный путь "analyze"
            // заменил бы последний сегмент адреса, а не дописался к нему.
            ServiceUrl = new Uri((configuration["AI:ServiceUrl"] ?? DefaultServiceUrl).TrimEnd('/') + "/")
        };

        services.AddSingleton(settings);

        // FinBERT и генерация профиля — один Python-сервис (Python/main.py).
        services.AddHttpClient<IFinBertAnalyzer, RealFinBertAnalyzer>(client =>
            client.BaseAddress = settings.ServiceUrl);

        // 40–50 факторов от gpt-4o-mini — это десятки секунд, до трёх попыток.
        services.AddHttpClient<IAssetProfileService, GptAssetProfileService>(client =>
        {
            client.BaseAddress = settings.ServiceUrl;
            client.Timeout = TimeSpan.FromMinutes(3);
        });

        // Без ключа приложение всё равно запускается: профиль соберётся
        // запасным алгоритмом, а «Новости» покажут причину, по которой
        // разбор не выполнен.
        services.AddSingleton<IEmbeddingService>(
            settings.ApiKey is { } key
                ? new OpenAIEmbeddingService(key)
                : new UnavailableEmbeddingService());

        services.AddSingleton<IFactorMatcher,
            RealFactorMatcher>();

        services.AddTransient<INewsProcessingService,
            NewsProcessingService>();

        services.AddSingleton<IProfilePromptBuilder,
            ProfilePromptBuilder>();

        return services;
    }
}
