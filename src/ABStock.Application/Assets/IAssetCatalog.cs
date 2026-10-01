using ABStock.Shared;

namespace ABStock.Application.Assets;

public interface IAssetCatalog
{
    Asset Create(CreateAssetRequest request);

    Asset? Get(Guid assetId);

    IReadOnlyList<Asset> GetAll();
}
