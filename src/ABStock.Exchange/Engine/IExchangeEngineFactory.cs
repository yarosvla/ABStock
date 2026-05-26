namespace ABStock.Exchange.Engine;

public interface IExchangeEngineFactory
{
    IExchangeEngine Create(decimal startPrice);
}
