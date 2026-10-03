using ABStock.Shared;

namespace ABStock.Application.Assets;

public interface IAssetCatalog
{
    Asset Create(CreateAssetRequest request);

    Asset? Get(Guid assetId);

    IReadOnlyList<Asset> GetAll(bool includeArchived = false);

    Asset Update(Guid assetId, UpdateAssetRequest request);

    Asset Archive(Guid assetId);
}
