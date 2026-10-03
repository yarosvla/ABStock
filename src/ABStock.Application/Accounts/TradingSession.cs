using ABStock.Application.Markets;
using ABStock.Exchange.Engine;
using ABStock.Shared;

namespace ABStock.Application.Accounts;

internal sealed class TradingSession(IMarketSession markets) : ITradingSession
{
    private readonly object _sync = new();
    private readonly OrderValidator _orderValidator = new();
    private readonly Dictionary<string, AgentAccount> _accounts = new(StringComparer.Ordinal);
    private readonly HashSet<Guid> _acceptedOrderIds = new();

    public Guid SessionId => markets.SessionId;

    public event Action<MarketState>? OnMarketChanged;
    public event Action<MarketSubmitResult>? OnOrdersSubmitted;
    public event Action<IReadOnlyList<AgentAccountSnapshot>>? OnAccountsChanged;

    public MarketState AddMarket(Guid assetId)
    {
        MarketState state;
        IReadOnlyList<AgentAccountSnapshot> accounts;

        lock (_sync)
        {
            if (markets.GetMarkets().Any(market => market.AssetId == assetId))
            {
                return markets.GetMarket(assetId);
            }

            state = markets.AddMarket(assetId);
            foreach (var account in _accounts.Values)
            {
                account.InitializeMarket(assetId, state.Snapshot.LastPrice);
            }

            accounts = GetAgentAccountsLocked();
        }

        OnAccountsChanged?.Invoke(accounts);
        OnMarketChanged?.Invoke(state);
        return state;
    }

    public AgentAccountSnapshot AddAgent(AgentAccountSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(spec.AgentName);
        ArgumentNullException.ThrowIfNull(spec.InitialPositions);
        if (!Enum.IsDefined(spec.AgentType) || spec.InitialCash < 0m || spec.InitialPosition < 0m)
        {
            throw new ArgumentException("Agent type must be valid and initial cash and position must be non-negative.", nameof(spec));
        }

        var copiedSpec = spec with
        {
            AgentName = spec.AgentName.Trim(),
            InitialPositions = new Dictionary<Guid, decimal>(spec.InitialPositions)
        };
        AgentAccountSnapshot snapshot;
        IReadOnlyList<AgentAccountSnapshot> accounts;

        lock (_sync)
        {
            if (_accounts.ContainsKey(copiedSpec.AgentName))
            {
                throw new ArgumentException("Agent account name must be unique within the session.", nameof(spec));
            }

            foreach (var position in copiedSpec.InitialPositions)
            {
                if (position.Value < 0m)
                {
                    throw new ArgumentException("Initial positions must be non-negative.", nameof(spec));
                }

                markets.GetMarket(position.Key);
            }

            var states = markets.GetMarkets();
            var account = new AgentAccount(copiedSpec, states);
            _accounts.Add(account.AgentName, account);
            snapshot = account.GetSnapshot(states);
            accounts = GetAgentAccountsLocked();
        }

        OnAccountsChanged?.Invoke(accounts);
        return snapshot;
    }

    public AgentAccountSnapshot GetAgentAccount(string agentName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        lock (_sync)
        {
            return GetAccount(agentName).GetSnapshot(markets.GetMarkets());
        }
    }

    public IReadOnlyList<AgentAccountSnapshot> GetAgentAccounts()
    {
        lock (_sync)
        {
            return GetAgentAccountsLocked();
        }
    }

    public MarketState GetMarket(Guid assetId, int depth = 5)
    {
        lock (_sync)
        {
            return markets.GetMarket(assetId, depth);
        }
    }

    public IReadOnlyList<MarketState> GetMarkets(int depth = 5)
    {
        lock (_sync)
        {
            return markets.GetMarkets(depth);
        }
    }

    public IReadOnlyList<Order> GetOpenOrders(Guid assetId)
    {
        lock (_sync)
        {
            return markets.GetOpenOrders(assetId);
        }
    }

    public MarketSubmitResult Submit(Guid assetId, Order order) => SubmitMany(assetId, [order]);

    public MarketSubmitResult SubmitMany(Guid assetId, IEnumerable<Order> orders)
    {
        ArgumentNullException.ThrowIfNull(orders);
        var batch = orders.ToArray();
        MarketSubmitResult result;
        MarketState state;
        IReadOnlyList<AgentAccountSnapshot> accounts;

        lock (_sync)
        {
            markets.GetMarket(assetId);
            var accepted = new List<Order>();
            var rejected = new List<RejectedOrder>();
            var trades = new List<Trade>();

            foreach (var order in batch)
            {
                var reason = GetRejectionReasonLocked(assetId, order);
                if (reason is not null)
                {
                    rejected.Add(new RejectedOrder(order, reason));
                    continue;
                }

                var submitted = markets.Submit(assetId, order).Result;
                accepted.AddRange(submitted.AcceptedOrders);
                rejected.AddRange(submitted.RejectedOrders);
                trades.AddRange(submitted.Trades);
                foreach (var acceptedOrder in submitted.AcceptedOrders)
                {
                    _acceptedOrderIds.Add(acceptedOrder.Id);
                }

                foreach (var trade in submitted.Trades)
                {
                    GetAccount(trade.BuyerAgentName).Buy(assetId, trade.Price, trade.Quantity);
                    GetAccount(trade.SellerAgentName).Sell(assetId, trade.Price, trade.Quantity);
                }

                RefreshReservationsLocked();
            }

            state = markets.GetMarket(assetId);
            var reports = OrderExecutionReportBuilder.Build(accepted, trades, markets.GetOpenOrders(assetId))
                .Concat(rejected.Select(rejection =>
                    OrderExecutionReportBuilder.CreateRejected(rejection.Order, rejection.Reason)))
                .ToArray();
            result = new MarketSubmitResult(SessionId, assetId,
                new SubmitResult(state.Snapshot, trades.ToArray(), accepted.ToArray(), rejected.ToArray())
                {
                    OrderReports = reports
                });
            accounts = GetAgentAccountsLocked();
        }

        OnAccountsChanged?.Invoke(accounts);
        OnOrdersSubmitted?.Invoke(result);
        OnMarketChanged?.Invoke(state);
        return result;
    }

    public bool CancelOrder(Guid assetId, Guid orderId)
    {
        MarketState state;
        IReadOnlyList<AgentAccountSnapshot> accounts;
        lock (_sync)
        {
            if (!markets.CancelOrder(assetId, orderId))
            {
                return false;
            }

            RefreshReservationsLocked();
            state = markets.GetMarket(assetId);
            accounts = GetAgentAccountsLocked();
        }

        OnAccountsChanged?.Invoke(accounts);
        OnMarketChanged?.Invoke(state);
        return true;
    }

    public int CancelOrdersByAgent(Guid assetId, string agentName)
    {
        int cancelled;
        MarketState state;
        IReadOnlyList<AgentAccountSnapshot> accounts;
        lock (_sync)
        {
            cancelled = markets.CancelOrdersByAgent(assetId, agentName);
            if (cancelled == 0)
            {
                return 0;
            }

            RefreshReservationsLocked();
            state = markets.GetMarket(assetId);
            accounts = GetAgentAccountsLocked();
        }

        OnAccountsChanged?.Invoke(accounts);
        OnMarketChanged?.Invoke(state);
        return cancelled;
    }

    private string? GetRejectionReasonLocked(Guid assetId, Order? order)
    {
        try
        {
            _orderValidator.Validate(order);
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        if (_acceptedOrderIds.Contains(order!.Id))
        {
            return "Order id has already been accepted in this session.";
        }

        if (!_accounts.TryGetValue(order.AgentName, out var account))
        {
            return $"Agent '{order.AgentName}' has no account in this session.";
        }

        return AccountOrderGuard.GetRejectionReason(assetId, order, account, markets.GetOpenOrders(assetId));
    }

    private void RefreshReservationsLocked()
    {
        foreach (var account in _accounts.Values)
        {
            account.ResetReservations();
        }

        foreach (var market in markets.GetMarkets())
        {
            foreach (var order in markets.GetOpenOrders(market.AssetId))
            {
                GetAccount(order.AgentName).Reserve(market.AssetId, order);
            }
        }
    }

    private AgentAccount GetAccount(string agentName) =>
        _accounts.TryGetValue(agentName, out var account)
            ? account
            : throw new KeyNotFoundException($"Agent '{agentName}' has no account in this session.");

    private IReadOnlyList<AgentAccountSnapshot> GetAgentAccountsLocked()
    {
        var states = markets.GetMarkets();
        return Array.AsReadOnly(_accounts.Values.Select(account => account.GetSnapshot(states)).ToArray());
    }
}
