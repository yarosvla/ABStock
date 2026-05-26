namespace ABStock.Exchange.Engine;

public sealed class ExchangeEngineFactory : IExchangeEngineFactory
{
    public IExchangeEngine Create(decimal startPrice)
    {
        return new ExchangeEngine(startPrice);
    }
}
