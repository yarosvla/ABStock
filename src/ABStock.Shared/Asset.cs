namespace ABStock.Shared;

public sealed record Asset(
    Guid AssetId,
    AssetProfile Profile,
    decimal StartPrice,
    DateTimeOffset CreatedAt
)
{
    public string Name => Profile.Name;

    public string Description => Profile.Description;

    public AssetType AssetType => Profile.AssetType;
}
