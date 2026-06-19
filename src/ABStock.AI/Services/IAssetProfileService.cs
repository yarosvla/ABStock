using ABStock.Shared;
using ABStock.AI.Models;

namespace ABStock.AI.Services;

public interface IAssetProfileService
{
    Task<AssetProfile> CreateProfileAsync(AssetProfileRequest request, CancellationToken ct = default);
}