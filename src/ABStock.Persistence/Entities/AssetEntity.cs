namespace ABStock.Persistence.Entities;

public sealed class AssetEntity
{
    public Guid Id { get; set; }
    public string ProfileJson { get; set; } = string.Empty;
    public decimal StartPrice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<SessionMarketEntity> Markets { get; set; } = [];
}
