namespace ABStock.AI.Models
{
    public sealed record AssetProfileRequest
    {
        public required AssetType AssetType { get; init; }
        public required String Name { get; init; }

        public required String Description { get; init; }
    }
}