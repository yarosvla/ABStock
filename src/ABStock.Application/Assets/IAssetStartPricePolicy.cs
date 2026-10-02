namespace ABStock.Application.Assets;

public interface IAssetStartPricePolicy
{
    decimal Calculate(CreateAssetRequest request);
}
