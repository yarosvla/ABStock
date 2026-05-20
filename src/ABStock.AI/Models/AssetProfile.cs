namespace ABStock.AI.Models
{
    public sealed record AssetProfile
    {
        public required String AssetName { get; init; }
        public IReadOnlyList<String> PositiveFactors { get; init; } = [];
        public IReadOnlyList<String> NegativeFactors { get; init; } = [];
        public IReadOnlyList<String> Risks { get; init; } = [];
        // public IReadOnlyList<String> Keywords { get; init; } = [];

        public decimal NewsSensitivity { get; init; }
    }
}