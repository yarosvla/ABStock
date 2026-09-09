using ABStock.Shared;

namespace ABStock.AI.Models
{
    public sealed record AssetProfileRequest
    {
        public required AssetType AssetType { get; init; }
        public required String Name { get; init; }
        public required String Description { get; init; }
        public string Industry { get; init; } = String.Empty;
        public bool IncludeGovernmentSupport { get; init; }

        /// <summary>
        /// Необязательная подсказка пользователя, 0–100. null — поле не заполнено:
        /// генератор не должен додумывать за пользователя ни фактор роста,
        /// ни риск «ограниченный апсайд».
        /// </summary>
        public int? GrowthPotential { get; init; }
    }
}
