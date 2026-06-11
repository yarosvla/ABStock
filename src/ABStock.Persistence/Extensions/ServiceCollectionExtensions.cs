using ABStock.Application.MarketHistory;
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

        services.RemoveAll<IMarketHistoryStore>();
        services.RemoveAll<IMarketCandleReader>();
        services.TryAddSingleton<IMarketHistoryStore, EfMarketHistoryStore>();
        services.TryAddSingleton<IMarketCandleReader, EfMarketCandleReader>();

        return services;
    }
}
