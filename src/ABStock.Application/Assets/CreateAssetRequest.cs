using ABStock.Shared;

namespace ABStock.Application.Assets;

public sealed record CreateAssetRequest(
    AssetProfile Profile,
    decimal StartPrice
);
