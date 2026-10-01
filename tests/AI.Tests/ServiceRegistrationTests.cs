using ABStock.AI.Extensions;
using ABStock.AI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.AI.Tests;

/// <summary>
/// Контейнер собирает сервисы AI и без ключа OpenAI: приложение должно
/// запускаться, а не падать на старте.
/// </summary>
public class ServiceRegistrationTests
{
    [Fact]
    public void Services_resolve_without_api_key()
    {
        var configuration = new ConfigurationBuilder().Build();

        using var provider = new ServiceCollection()
            .AddLogging()
            .AddABStockAI(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        Assert.NotNull(provider.GetRequiredService<IAssetProfileService>());
        Assert.NotNull(provider.GetRequiredService<INewsProcessingService>());
    }
}
