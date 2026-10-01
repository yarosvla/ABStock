namespace ABStock.Application.Markets;

public interface IMarketSessionFactory
{
    IMarketSession Create();
}
