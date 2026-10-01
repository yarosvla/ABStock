namespace ABStock.Persistence.Entities;

public sealed class TradingSessionEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long TickIntervalTicks { get; set; }
    public List<SessionMarketEntity> Markets { get; set; } = [];
}
