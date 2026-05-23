using ABStock.Shared;

namespace ABStock.AI.Models
{
    public sealed record NewsAnalysisRequest
    {
        public required String NewsText { get; init; }
        public required AssetProfile Profile { get; init; }
    }
}