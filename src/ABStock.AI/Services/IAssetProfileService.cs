using ABStock.AI.Models;
using ABStock.Shared;

namespace ABStock.AI.Services;

public interface IAssetProfileService
{
    AssetProfile CreateProfile(AssetProfileRequest request);
}