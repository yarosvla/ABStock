using ABStock.Application.MarketHistory;
using ABStock.Application.Assets;
using ABStock.Persistence.Assets;
using ABStock.Persistence.MarketHistory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ABStock.Persistence.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddABStockPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        services.AddDbContextFactory<AbStockDbContext>(options =>
            options.UseSqlite(connectionString));

        services.TryAddSingleton<StorageInitializer>();
        services.TryAddSingleton<IAssetStartPricePolicy, AssetStartPricePolicy>();
        services.RemoveAll<IAssetCatalog>();
        services.RemoveAll<IMarketHistoryStore>();
        services.RemoveAll<IMarketCandleReader>();
        services.RemoveAll<ISimulationHistoryReader>();
        services.RemoveAll<IAgentStatisticsReader>();
        services.RemoveAll<ITradingSessionHistoryReader>();
        services.TryAddSingleton<IAssetCatalog, EfAssetCatalog>();
        services.TryAddSingleton<IMarketHistoryStore, EfMarketHistoryStore>();
        services.TryAddSingleton<IMarketCandleReader, EfMarketCandleReader>();
        services.TryAddSingleton<ISimulationHistoryReader, EfSimulationHistoryReader>();
        services.TryAddSingleton<ITradingSessionHistoryReader, EfTradingSessionHistoryReader>();
        services.TryAddSingleton<IAgentStatisticsReader, EfAgentStatisticsReader>();

        return services;
    }
}
