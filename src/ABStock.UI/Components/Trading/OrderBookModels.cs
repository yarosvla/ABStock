namespace ABStock.UI.Components.Trading;

/// <summary>
/// A single price level rendered inside the order book. <see cref="DepthStyle"/> is a ready-to-use
/// inline style ("width: NN%;") so the view stays free of formatting logic.
/// </summary>
public sealed record OrderBookRow(
    string Price,
    string Volume,
    string Total,
    int Depth,
    string DepthStyle,
    bool IsEmpty);

/// <summary>
/// The spread/last-price divider shown between asks and bids in a classic order book.
/// </summary>
public sealed record OrderBookCenter(
    string LastPrice,
    string DirectionClass,
    string DirectionIcon,
    string Spread,
    string SpreadPercent,
    bool HasData);

/// <summary>
/// View model for the <c>OrderBook</c> component: asks (rendered high→low, best ask next to the
/// center), the spread divider, and bids (rendered high→low, best bid next to the center).
/// </summary>
public sealed record OrderBookViewData(
    IReadOnlyList<OrderBookRow> Asks,
    IReadOnlyList<OrderBookRow> Bids,
    OrderBookCenter Center);
