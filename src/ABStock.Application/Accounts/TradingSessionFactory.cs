using ABStock.Application.Markets;

namespace ABStock.Application.Accounts;

public sealed class TradingSessionFactory(IMarketSessionFactory marketSessionFactory) : ITradingSessionFactory
{
    public ITradingSession Create() => new TradingSession(marketSessionFactory.Create());
}
