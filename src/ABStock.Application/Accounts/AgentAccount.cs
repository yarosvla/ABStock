using System.Collections.ObjectModel;
using ABStock.Shared;

namespace ABStock.Application.Accounts;

internal sealed class AgentAccount
{
    private readonly Dictionary<Guid, decimal> _positions;
    private readonly Dictionary<Guid, decimal> _initialPositions;
    private readonly Dictionary<Guid, decimal> _costBases;
    private readonly Dictionary<Guid, decimal> _realizedPnls = new();
    private readonly Dictionary<Guid, decimal> _reservedPositions = new();

    public string AgentName { get; }
    public AgentType AgentType { get; }
    public decimal Cash { get; private set; }
    public decimal InitialCash { get; }
    public decimal InitialPortfolioValue { get; private set; }
    public decimal DefaultInitialPosition { get; }
    public decimal ReservedCash { get; private set; }
    public decimal AvailableCash => Cash - ReservedCash;

    public AgentAccount(AgentAccountSpec spec, IReadOnlyList<MarketState> markets)
    {
        AgentName = spec.AgentName.Trim();
        AgentType = spec.AgentType;
        Cash = InitialCash = spec.InitialCash;
        DefaultInitialPosition = spec.InitialPosition;
        _positions = markets.ToDictionary(market => market.AssetId,
            market => spec.InitialPositions.GetValueOrDefault(market.AssetId, DefaultInitialPosition));
        _initialPositions = new Dictionary<Guid, decimal>(_positions);
        _costBases = markets.ToDictionary(market => market.AssetId,
            market => GetPosition(market.AssetId) * market.Snapshot.LastPrice);
        InitialPortfolioValue = InitialCash + _costBases.Values.Sum();
    }

    public decimal GetPosition(Guid assetId) => _positions.GetValueOrDefault(assetId);

    public decimal GetAvailablePosition(Guid assetId) =>
        GetPosition(assetId) - _reservedPositions.GetValueOrDefault(assetId);

    public void InitializeMarket(Guid assetId, decimal startPrice)
    {
        // Starting inventory is contributed capital, not trading profit.
        var costBasis = DefaultInitialPosition * startPrice;
        var baseline = InitialPortfolioValue + costBasis;
        _positions.Add(assetId, DefaultInitialPosition);
        _initialPositions.Add(assetId, DefaultInitialPosition);
        _costBases.Add(assetId, costBasis);
        InitialPortfolioValue = baseline;
    }

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

        var costBasis = _costBases.GetValueOrDefault(assetId) + cost;
        var position = GetPosition(assetId) + quantity;
        Cash -= cost;
        _positions[assetId] = position;
        _costBases[assetId] = costBasis;
    }

    public void Sell(Guid assetId, decimal price, decimal quantity)
    {
        var position = GetPosition(assetId);
        if (quantity > position)
        {
            throw new InvalidOperationException("Trade exceeds account position.");
        }

        var costBasis = _costBases.GetValueOrDefault(assetId);
        // Full liquidation removes the entire basis, avoiding a rounding residue.
        var soldCost = quantity == position ? costBasis : costBasis * (quantity / position);
        var proceeds = price * quantity;
        var realizedPnl = _realizedPnls.GetValueOrDefault(assetId) + proceeds - soldCost;
        Cash += proceeds;
        _positions[assetId] = position - quantity;
        _costBases[assetId] = costBasis - soldCost;
        _realizedPnls[assetId] = realizedPnl;
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
                market.Snapshot.LastPrice)
            {
                CostBasis = _costBases.GetValueOrDefault(market.AssetId),
                RealizedPnl = _realizedPnls.GetValueOrDefault(market.AssetId)
            });

        return new AgentAccountSnapshot(
            AgentName, AgentType, Cash, ReservedCash, InitialCash,
            Cash + positions.Values.Sum(position => position.MarketValue),
            InitialPortfolioValue,
            new ReadOnlyDictionary<Guid, AgentPositionSnapshot>(positions));
    }
}
