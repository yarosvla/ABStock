namespace ABStock.Shared;

public sealed record MarketSubmitResult(
    Guid SessionId,
    Guid AssetId,
    SubmitResult Result
);
