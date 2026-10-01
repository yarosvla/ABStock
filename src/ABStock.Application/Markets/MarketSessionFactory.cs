using ABStock.Application.Assets;
using ABStock.Exchange.Engine;

namespace ABStock.Application.Markets;

public sealed class MarketSessionFactory(
    IAssetCatalog assetCatalog,
    IExchangeEngineFactory exchangeEngineFactory) : IMarketSessionFactory
{
    public IMarketSession Create() => new MarketSession(assetCatalog, exchangeEngineFactory);
}
