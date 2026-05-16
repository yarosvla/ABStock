using ABStock.AI.Models;

namespace ABStock.AI.Services;

public interface IAssetProfileService
{
    AssetProfile CreateProfile(AssetProfileRequest request);
}