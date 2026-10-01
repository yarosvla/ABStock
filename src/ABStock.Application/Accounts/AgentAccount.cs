using System.Collections.ObjectModel;
using ABStock.Shared;

namespace ABStock.Application.Accounts;

internal sealed class AgentAccount
{
    private readonly Dictionary<Guid, decimal> _positions;
    private readonly Dictionary<Guid, decimal> _initialPositions;
    private readonly Dictionary<Guid, decimal> _reservedPositions = new();

    public string AgentName { get; }
    public AgentType AgentType { get; }
    public decimal Cash { get; private set; }
    public decimal InitialCash { get; }
    public decimal InitialPortfolioValue { get; }
    public decimal ReservedCash { get; private set; }
    public decimal AvailableCash => Cash - ReservedCash;

    public AgentAccount(AgentAccountSpec spec, IReadOnlyList<MarketState> markets)
    {
        AgentName = spec.AgentName.Trim();
        AgentType = spec.AgentType;
        Cash = InitialCash = spec.InitialCash;
        _positions = new Dictionary<Guid, decimal>(spec.InitialPositions);
        _initialPositions = new Dictionary<Guid, decimal>(spec.InitialPositions);
        InitialPortfolioValue = InitialCash + markets.Sum(market =>
            GetPosition(market.AssetId) * market.Snapshot.LastPrice);
    }

    public decimal GetPosition(Guid assetId) => _positions.GetValueOrDefault(assetId);

    public decimal GetAvailablePosition(Guid assetId) =>
        GetPosition(assetId) - _reservedPositions.GetValueOrDefault(assetId);

    public void ResetReservations()
    {
        ReservedCash = 0m;
        _reservedPositions.Clear();
    }

    public void Reserve(Guid assetId, Order order)
    {
        if (order.Side == OrderSide.Buy)
        {
            ReservedCash += order.Price!.Value * order.Quantity;
        }
        else
        {
            _reservedPositions[assetId] = _reservedPositions.GetValueOrDefault(assetId) + order.Quantity;
        }
    }

    public void Buy(Guid assetId, decimal price, decimal quantity)
    {
        var cost = price * quantity;
        if (cost > Cash)
        {
            throw new InvalidOperationException("Trade exceeds account cash.");
        }

        Cash -= cost;
        _positions[assetId] = GetPosition(assetId) + quantity;
    }

    public void Sell(Guid assetId, decimal price, decimal quantity)
    {
        if (quantity > GetPosition(assetId))
        {
            throw new InvalidOperationException("Trade exceeds account position.");
        }

        Cash += price * quantity;
        _positions[assetId] = GetPosition(assetId) - quantity;
    }

    public AgentAccountSnapshot GetSnapshot(IReadOnlyList<MarketState> markets)
    {
        var positions = markets.ToDictionary(
            market => market.AssetId,
            market => new AgentPositionSnapshot(
                market.AssetId,
                GetPosition(market.AssetId),
                _reservedPositions.GetValueOrDefault(market.AssetId),
                _initialPositions.GetValueOrDefault(market.AssetId),
                market.Snapshot.LastPrice));

        return new AgentAccountSnapshot(
            AgentName, AgentType, Cash, ReservedCash, InitialCash,
            Cash + positions.Values.Sum(position => position.MarketValue),
            InitialPortfolioValue,
            new ReadOnlyDictionary<Guid, AgentPositionSnapshot>(positions));
    }
}
