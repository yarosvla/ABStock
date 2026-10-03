namespace ABStock.Shared;

public sealed record Asset(
    Guid AssetId,
    AssetProfile Profile,
    decimal StartPrice,
    DateTimeOffset CreatedAt
)
{
    public string Ticker { get; init; } = string.Empty;

    public string Industry { get; init; } = string.Empty;

    public bool IncludeGovernmentSupport { get; init; }

    public int? GrowthPotential { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    public DateTimeOffset? ArchivedAt { get; init; }

    public bool IsArchived => ArchivedAt is not null;

    public string Name => Profile.Name;

    public string Description => Profile.Description;

    public AssetType AssetType => Profile.AssetType;
}
