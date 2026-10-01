using ABStock.Application.Accounts;
using ABStock.Application.Assets;
using ABStock.Application.Extensions;
using ABStock.Application.Markets;
using ABStock.Exchange.Engine;
using ABStock.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace ABStock.Application.Tests;

public sealed class TradingSessionIntegrationTests
{
    [Fact]
    public void AddAgent_HasOneCashBalanceAndPositionsByAsset()
    {
        var (session, _, first, second) = CreateTwoMarkets();

        var account = AddAgent(session, "Buyer", 500m, (first.AssetId, 2m), (second.AssetId, 3m));

        Assert.Equal(500m, account.Cash);
        Assert.Equal(500m, account.AvailableCash);
        Assert.Equal(2m, account.Positions[first.AssetId].Quantity);
        Assert.Equal(3m, account.Positions[second.AssetId].Quantity);
        Assert.Equal(1300m, account.PortfolioValue);
        Assert.Equal(1300m, account.InitialPortfolioValue);
        Assert.Equal(500m, account.InitialCash);
        Assert.Single(session.GetAgentAccounts());
    }

    [Fact]
    public void RestingBuyOnFirstMarket_ReservesSharedCashForSecondMarket()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 150m);

        session.Submit(first.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m));
        var rejected = session.Submit(second.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m));
        var accepted = session.Submit(second.AssetId, LimitOrder("Buyer", OrderSide.Buy, 50m));

        Assert.Single(rejected.Result.RejectedOrders);
        Assert.Single(accepted.Result.AcceptedOrders);
        var account = session.GetAgentAccount("Buyer");
        Assert.Equal(150m, account.Cash);
        Assert.Equal(150m, account.ReservedCash);
        Assert.Equal(0m, account.AvailableCash);
        Assert.Single(session.GetOpenOrders(first.AssetId));
        Assert.Single(session.GetOpenOrders(second.AssetId));
    }

    [Fact]
    public void SellCannotUsePositionOfAnotherAssetOrAlreadyReservedPosition()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Seller", 0m, (first.AssetId, 3m));

        var accepted = session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 100m, 3m));
        var wrongAsset = session.Submit(second.AssetId, LimitOrder("Seller", OrderSide.Sell, 100m));
        var reserved = session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 101m));

        Assert.Single(accepted.Result.AcceptedOrders);
        Assert.Single(wrongAsset.Result.RejectedOrders);
        Assert.Single(reserved.Result.RejectedOrders);
        var account = session.GetAgentAccount("Seller");
        Assert.Equal(3m, account.Positions[first.AssetId].ReservedQuantity);
        Assert.Equal(0m, account.Positions[first.AssetId].AvailableQuantity);
        Assert.Equal(0m, account.Positions[second.AssetId].Quantity);
    }

    [Fact]
    public void SellReservationsAreIndependentForEachAsset()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Seller", 0m, (first.AssetId, 3m), (second.AssetId, 4m));

        session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 100m, 2m));
        session.Submit(second.AssetId, LimitOrder("Seller", OrderSide.Sell, 200m, 4m));

        var account = session.GetAgentAccount("Seller");
        Assert.Equal(1m, account.Positions[first.AssetId].AvailableQuantity);
        Assert.Equal(2m, account.Positions[first.AssetId].ReservedQuantity);
        Assert.Equal(0m, account.Positions[second.AssetId].AvailableQuantity);
        Assert.Equal(4m, account.Positions[second.AssetId].ReservedQuantity);
    }

    [Fact]
    public void PartialLimitFill_SettlesAccountsAndReservesOnlyRemainder()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 1100m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 4m));
        var buy = LimitOrder("Buyer", OrderSide.Buy, 110m, 10m);
        var sell = LimitOrder("Seller", OrderSide.Sell, 100m, 4m);

        var result = session.SubmitMany(first.AssetId, [buy, sell]);

        Assert.Equal(105m, Assert.Single(result.Result.Trades).Price);
        var buyer = session.GetAgentAccount("Buyer");
        var seller = session.GetAgentAccount("Seller");
        Assert.Equal(680m, buyer.Cash);
        Assert.Equal(660m, buyer.ReservedCash);
        Assert.Equal(20m, buyer.AvailableCash);
        Assert.Equal(4m, buyer.Positions[first.AssetId].Quantity);
        Assert.Equal(0m, buyer.Positions[second.AssetId].Quantity);
        Assert.Equal(420m, seller.Cash);
        Assert.Equal(0m, seller.Positions[first.AssetId].Quantity);
        Assert.Equal(0m, seller.Positions[first.AssetId].ReservedQuantity);
        Assert.Equal(1100m, buyer.Cash + seller.Cash);
        Assert.Equal(OrderExecutionStatus.PartiallyFilled,
            Assert.Single(result.Result.OrderReports, report => report.Order?.Id == buy.Id).Status);
        Assert.Equal(OrderExecutionStatus.Filled,
            Assert.Single(result.Result.OrderReports, report => report.Order?.Id == sell.Id).Status);
        Assert.Equal(6m, Assert.Single(session.GetOpenOrders(first.AssetId)).Quantity);

        Assert.True(session.CancelOrder(first.AssetId, buy.Id));
        Assert.Equal(680m, session.GetAgentAccount("Buyer").AvailableCash);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").ReservedCash);
    }

    [Fact]
    public void BatchReportsReflectFinalFillsOfEarlierOrders()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 200m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 2m));
        var sell = LimitOrder("Seller", OrderSide.Sell, 100m, 2m);
        var buy = LimitOrder("Buyer", OrderSide.Buy, 100m, 2m);

        var result = session.SubmitMany(first.AssetId, [sell, buy]);

        Assert.Equal(2, result.Result.OrderReports.Count);
        Assert.All(result.Result.OrderReports, report =>
        {
            Assert.Equal(OrderExecutionStatus.Filled, report.Status);
            Assert.Equal(2m, report.FilledQuantity);
            Assert.Equal(0m, report.RemainingQuantity);
        });
        Assert.Equal(0m, session.GetAgentAccount("Buyer").AvailableCash);
        Assert.Equal(200m, session.GetAgentAccount("Seller").Cash);
    }

    [Fact]
    public void MarketBuy_ChecksAllPriceLevelsBeforeExecuting()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 250m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 2m));
        session.SubmitMany(first.AssetId,
        [
            LimitOrder("Seller", OrderSide.Sell, 100m),
            LimitOrder("Seller", OrderSide.Sell, 200m)
        ]);

        var result = session.Submit(first.AssetId, MarketOrder("Buyer", OrderSide.Buy, 2m));

        Assert.Single(result.Result.RejectedOrders);
        Assert.Empty(result.Result.Trades);
        Assert.Equal(250m, session.GetAgentAccount("Buyer").Cash);
        Assert.Equal(0m, session.GetMarket(first.AssetId).Snapshot.Volume);
        Assert.Equal(2, session.GetOpenOrders(first.AssetId).Count);
    }

    [Fact]
    public void MarketBuy_WithEnoughCashSettlesMultipleLevels()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 300m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 2m));
        session.SubmitMany(first.AssetId,
        [
            LimitOrder("Seller", OrderSide.Sell, 100m),
            LimitOrder("Seller", OrderSide.Sell, 200m)
        ]);

        var result = session.Submit(first.AssetId, MarketOrder("Buyer", OrderSide.Buy, 2m));

        Assert.Equal(2, result.Result.Trades.Count);
        Assert.Equal(150m, Assert.Single(result.Result.OrderReports).AveragePrice);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").Cash);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").ReservedCash);
        Assert.Equal(2m, session.GetAgentAccount("Buyer").Positions[first.AssetId].Quantity);
        Assert.Equal(300m, session.GetAgentAccount("Seller").Cash);
        Assert.Empty(session.GetOpenOrders(first.AssetId));
    }

    [Fact]
    public void MarketBuy_CostIgnoresOwnAsks()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 150m, (first.AssetId, 1m));
        AddAgent(session, "Seller", 0m, (first.AssetId, 2m));
        session.Submit(first.AssetId, LimitOrder("Buyer", OrderSide.Sell, 50m));
        session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 100m, 2m));

        var result = session.Submit(first.AssetId, MarketOrder("Buyer", OrderSide.Buy, 2m));

        Assert.Single(result.Result.RejectedOrders);
        Assert.Empty(result.Result.Trades);
        Assert.Equal(150m, session.GetAgentAccount("Buyer").Cash);
        Assert.Equal(2, session.GetOpenOrders(first.AssetId).Count);
    }

    [Fact]
    public void MarketBuy_ReservesNothingForUnfilledRemainder()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 100m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 1m));
        session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 100m));

        var result = session.Submit(first.AssetId, MarketOrder("Buyer", OrderSide.Buy, 10m));

        Assert.Single(result.Result.AcceptedOrders);
        var report = Assert.Single(result.Result.OrderReports);
        Assert.Equal(OrderExecutionStatus.PartiallyFilled, report.Status);
        Assert.Equal(1m, report.FilledQuantity);
        Assert.Equal(9m, report.RemainingQuantity);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").ReservedCash);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").Cash);
        Assert.Empty(session.GetOpenOrders(first.AssetId));
    }

    [Fact]
    public void MarketBuy_WithoutLiquidityExpiresWithoutSpendingOrReserving()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 0m);

        var result = session.Submit(first.AssetId, MarketOrder("Buyer", OrderSide.Buy, 1m));

        Assert.Equal(OrderExecutionStatus.Expired, Assert.Single(result.Result.OrderReports).Status);
        Assert.Empty(result.Result.Trades);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").ReservedCash);
    }

    [Fact]
    public void MarketSell_UsesOnlySelectedAssetsAvailablePosition()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Seller", 0m, (first.AssetId, 2m));
        AddAgent(session, "Buyer", 100m);
        session.Submit(first.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m));

        var wrongAsset = session.Submit(second.AssetId, MarketOrder("Seller", OrderSide.Sell, 1m));
        var filled = session.Submit(first.AssetId, MarketOrder("Seller", OrderSide.Sell, 1m));

        Assert.Single(wrongAsset.Result.RejectedOrders);
        Assert.Single(filled.Result.Trades);
        Assert.Equal(100m, session.GetAgentAccount("Seller").Cash);
        Assert.Equal(1m, session.GetAgentAccount("Seller").Positions[first.AssetId].Quantity);
        Assert.Equal(0m, session.GetAgentAccount("Seller").Positions[second.AssetId].Quantity);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").ReservedCash);
    }

    [Fact]
    public void InvalidOrdersDoNotConsumeOrIncreaseBudget()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 100m);

        var result = session.SubmitMany(first.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, -100m, 10m),
            LimitOrder("Buyer", OrderSide.Buy, 99m),
            LimitOrder("Buyer", OrderSide.Buy, 2m)
        ]);

        Assert.Single(result.Result.AcceptedOrders);
        Assert.Equal(2, result.Result.RejectedOrders.Count);
        Assert.Equal(99m, session.GetAgentAccount("Buyer").ReservedCash);
        Assert.Equal(1m, session.GetAgentAccount("Buyer").AvailableCash);
        Assert.Single(session.GetOpenOrders(first.AssetId));
    }

    [Fact]
    public void SelfTradingRejectionDoesNotReserveFundsForLaterOrders()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Maker", 200m, (first.AssetId, 1m));
        session.Submit(first.AssetId, LimitOrder("Maker", OrderSide.Sell, 100m));

        var result = session.SubmitMany(first.AssetId,
        [
            LimitOrder("Maker", OrderSide.Buy, 110m),
            LimitOrder("Maker", OrderSide.Buy, 99m)
        ]);

        Assert.Single(result.Result.RejectedOrders);
        Assert.Single(result.Result.AcceptedOrders);
        Assert.Equal(99m, session.GetAgentAccount("Maker").ReservedCash);
        Assert.Equal(101m, session.GetAgentAccount("Maker").AvailableCash);
        Assert.Empty(result.Result.Trades);
    }

    [Fact]
    public void CancelOrdersOnOneMarket_FreesOnlyThatMarketsCashReserve()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 200m);
        var firstOrder = LimitOrder("Buyer", OrderSide.Buy, 100m);
        session.Submit(first.AssetId, firstOrder);
        session.Submit(second.AssetId, LimitOrder("Buyer", OrderSide.Buy, 80m));

        Assert.False(session.CancelOrder(second.AssetId, firstOrder.Id));
        Assert.Equal(180m, session.GetAgentAccount("Buyer").ReservedCash);
        Assert.Equal(1, session.CancelOrdersByAgent(first.AssetId, "Buyer"));
        Assert.Equal(80m, session.GetAgentAccount("Buyer").ReservedCash);
        Assert.Equal(120m, session.GetAgentAccount("Buyer").AvailableCash);
        Assert.Single(session.GetOpenOrders(second.AssetId));
    }

    [Fact]
    public void CancellingSell_FreesOnlySelectedAssetsPosition()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Seller", 0m, (first.AssetId, 2m), (second.AssetId, 3m));
        var firstOrder = LimitOrder("Seller", OrderSide.Sell, 100m, 2m);
        session.Submit(first.AssetId, firstOrder);
        session.Submit(second.AssetId, LimitOrder("Seller", OrderSide.Sell, 200m, 3m));

        session.CancelOrder(first.AssetId, firstOrder.Id);

        var account = session.GetAgentAccount("Seller");
        Assert.Equal(2m, account.Positions[first.AssetId].AvailableQuantity);
        Assert.Equal(0m, account.Positions[first.AssetId].ReservedQuantity);
        Assert.Equal(0m, account.Positions[second.AssetId].AvailableQuantity);
        Assert.Equal(3m, account.Positions[second.AssetId].ReservedQuantity);
    }

    [Fact]
    public void AddingMarket_DoesNotCopyCashOrInventPositions()
    {
        var (session, catalog, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 150m, (first.AssetId, 2m));
        session.Submit(first.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m));
        var newAsset = CreateAsset(catalog, "Third", 300m);

        session.AddMarket(newAsset.AssetId);
        session.AddMarket(newAsset.AssetId);

        var account = session.GetAgentAccount("Buyer");
        Assert.Equal(150m, account.Cash);
        Assert.Equal(100m, account.ReservedCash);
        Assert.Equal(50m, account.AvailableCash);
        Assert.Equal(350m, account.InitialPortfolioValue);
        Assert.Equal(0m, account.Positions[newAsset.AssetId].Quantity);
        Assert.Equal(0m, account.Positions[newAsset.AssetId].InitialQuantity);
        Assert.Equal(3, session.GetMarkets().Count);
    }

    [Fact]
    public void PortfolioValueUsesPricesOfAllAssetsAndKeepsInitialBaseline()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Investor", 500m, (first.AssetId, 2m), (second.AssetId, 3m));
        AddAgent(session, "Buyer", 200m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 1m));
        session.SubmitMany(first.AssetId,
        [
            LimitOrder("Buyer", OrderSide.Buy, 110m),
            LimitOrder("Seller", OrderSide.Sell, 100m)
        ]);

        var investor = session.GetAgentAccount("Investor");

        Assert.Equal(1310m, investor.PortfolioValue);
        Assert.Equal(1300m, investor.InitialPortfolioValue);
        Assert.Equal(105m, investor.Positions[first.AssetId].LastPrice);
        Assert.Equal(200m, investor.Positions[second.AssetId].LastPrice);
        Assert.Equal(500m, investor.Cash);
    }

    [Fact]
    public void UnknownAgentOrdersAreRejectedWithoutChangingMarkets()
    {
        var (session, _, first, _) = CreateTwoMarkets();

        var result = session.Submit(first.AssetId, LimitOrder("Unknown", OrderSide.Buy, 100m));

        Assert.Single(result.Result.RejectedOrders);
        Assert.Equal(OrderExecutionStatus.Rejected, Assert.Single(result.Result.OrderReports).Status);
        Assert.Empty(session.GetOpenOrders(first.AssetId));
        Assert.Throws<KeyNotFoundException>(() => session.GetAgentAccount("Unknown"));
    }

    [Fact]
    public void DuplicateAccountNameCannotResetExistingFundsOrPositions()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 100m, (first.AssetId, 2m));

        Assert.Throws<ArgumentException>(() => AddAgent(session, " Buyer ", 10000m));
        Assert.Equal(100m, session.GetAgentAccount("Buyer").Cash);
        Assert.Equal(2m, session.GetAgentAccount("Buyer").Positions[first.AssetId].Quantity);
        Assert.Single(session.GetAgentAccounts());
    }

    [Fact]
    public void InvalidAccountSpecsDoNotCreateAccounts()
    {
        var (session, _, first, _) = CreateTwoMarkets();

        Assert.Throws<ArgumentNullException>(() => session.AddAgent(null!));
        Assert.ThrowsAny<ArgumentException>(() => AddAgent(session, " ", 100m));
        Assert.Throws<ArgumentException>(() => AddAgent(session, "Buyer", -1m));
        Assert.Throws<ArgumentException>(() => session.AddAgent(new AgentAccountSpec("Buyer", (AgentType)999, 100m)));
        Assert.Throws<ArgumentNullException>(() => session.AddAgent(
            new AgentAccountSpec("Buyer", AgentType.TrendFollowing, 100m) { InitialPositions = null! }));
        Assert.Throws<ArgumentException>(() => AddAgent(session, "Buyer", 100m, (first.AssetId, -1m)));
        Assert.Throws<KeyNotFoundException>(() => AddAgent(session, "Buyer", 100m, (Guid.NewGuid(), 1m)));
        Assert.Empty(session.GetAgentAccounts());
    }

    [Fact]
    public void InitialPositionsAndAccountSnapshotsCannotBeChangedExternally()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        var positions = new Dictionary<Guid, decimal> { [first.AssetId] = 2m };
        var original = session.AddAgent(new AgentAccountSpec("Buyer", AgentType.TrendFollowing, 100m)
        {
            InitialPositions = positions
        });
        positions[first.AssetId] = 999m;
        session.Submit(first.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m));

        Assert.Equal(0m, original.ReservedCash);
        Assert.Equal(2m, session.GetAgentAccount("Buyer").Positions[first.AssetId].Quantity);
        Assert.Throws<NotSupportedException>(() =>
            ((IDictionary<Guid, AgentPositionSnapshot>)original.Positions).Clear());
        Assert.Equal(100m, session.GetAgentAccount("Buyer").ReservedCash);
    }

    [Fact]
    public void AgentsCanBeRegisteredBeforeAddingMarkets()
    {
        var catalog = new InMemoryAssetCatalog();
        var session = CreateSession(catalog);

        var initial = AddAgent(session, "Buyer", 100m);
        Assert.Empty(initial.Positions);
        var asset = CreateAsset(catalog, "Energy", 100m);
        session.AddMarket(asset.AssetId);

        var account = session.GetAgentAccount("Buyer");
        Assert.Equal(100m, account.InitialPortfolioValue);
        Assert.Equal(100m, account.Cash);
        Assert.Equal(0m, account.Positions[asset.AssetId].Quantity);
    }

    [Fact]
    public void ReusedOrderIdCannotReserveOrSettleTwiceAcrossMarkets()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 200m);
        var order = LimitOrder("Buyer", OrderSide.Buy, 100m);
        session.Submit(first.AssetId, order);

        var reused = session.Submit(second.AssetId, order);
        session.CancelOrder(first.AssetId, order.Id);
        var afterCancel = session.Submit(first.AssetId, order);

        Assert.Single(reused.Result.RejectedOrders);
        Assert.Single(afterCancel.Result.RejectedOrders);
        Assert.Equal(200m, session.GetAgentAccount("Buyer").AvailableCash);
        Assert.Empty(session.GetOpenOrders(first.AssetId));
        Assert.Empty(session.GetOpenOrders(second.AssetId));
    }

    [Fact]
    public void ReusingFilledOrderIdDoesNotApplyTradeTwice()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 200m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 2m));
        session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 100m, 2m));
        var order = MarketOrder("Buyer", OrderSide.Buy, 1m);
        session.Submit(first.AssetId, order);

        var result = session.Submit(first.AssetId, order);

        Assert.Single(result.Result.RejectedOrders);
        Assert.Equal(100m, session.GetAgentAccount("Buyer").Cash);
        Assert.Equal(1m, session.GetAgentAccount("Buyer").Positions[first.AssetId].Quantity);
        Assert.Equal(1m, session.GetMarket(first.AssetId).Snapshot.Volume);
    }

    [Fact]
    public async Task ConcurrentOrdersCannotReserveSameCashOnTwoMarkets()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 100m);

        var results = await Task.WhenAll(
            Task.Run(() => session.Submit(first.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m))),
            Task.Run(() => session.Submit(second.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m))));

        Assert.Equal(1, results.Sum(result => result.Result.AcceptedOrders.Count));
        Assert.Equal(1, results.Sum(result => result.Result.RejectedOrders.Count));
        Assert.Equal(100m, session.GetAgentAccount("Buyer").ReservedCash);
        Assert.Equal(0m, session.GetAgentAccount("Buyer").AvailableCash);
    }

    [Fact]
    public async Task ConcurrentMarketBuysCannotSpendSameCashOnTwoMarkets()
    {
        var (session, _, first, second) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 100m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 1m), (second.AssetId, 1m));
        session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 80m));
        session.Submit(second.AssetId, LimitOrder("Seller", OrderSide.Sell, 80m));

        var results = await Task.WhenAll(
            Task.Run(() => session.Submit(first.AssetId, MarketOrder("Buyer", OrderSide.Buy, 1m))),
            Task.Run(() => session.Submit(second.AssetId, MarketOrder("Buyer", OrderSide.Buy, 1m))));

        Assert.Equal(1, results.Sum(result => result.Result.Trades.Count));
        Assert.Equal(1, results.Sum(result => result.Result.RejectedOrders.Count));
        Assert.Equal(20m, session.GetAgentAccount("Buyer").Cash);
        Assert.Equal(80m, session.GetAgentAccount("Seller").Cash);
        Assert.Equal(1m, session.GetAgentAccount("Buyer").Positions.Values.Sum(position => position.Quantity));
    }

    [Fact]
    public void EventsPublishAccountsAfterSettlementAndReservationRefresh()
    {
        var (session, _, first, _) = CreateTwoMarkets();
        AddAgent(session, "Buyer", 200m);
        AddAgent(session, "Seller", 0m, (first.AssetId, 1m));
        session.Submit(first.AssetId, LimitOrder("Seller", OrderSide.Sell, 100m));
        IReadOnlyList<AgentAccountSnapshot>? received = null;
        session.OnAccountsChanged += accounts => received = accounts;
        session.OnOrdersSubmitted += _ => Assert.Equal(100m, session.GetAgentAccount("Buyer").Cash);

        session.Submit(first.AssetId, MarketOrder("Buyer", OrderSide.Buy, 1m));

        Assert.NotNull(received);
        var buyer = Assert.Single(received, account => account.AgentName == "Buyer");
        Assert.Equal(100m, buyer.Cash);
        Assert.Equal(0m, buyer.ReservedCash);
        Assert.Equal(1m, buyer.Positions[first.AssetId].Quantity);
        Assert.Equal(0m, Assert.Single(received, account => account.AgentName == "Seller")
            .Positions[first.AssetId].ReservedQuantity);
    }

    [Fact]
    public void NewSessionDoesNotShareAccountStateWithPreviousSession()
    {
        var catalog = new InMemoryAssetCatalog();
        var asset = CreateAsset(catalog, "Energy", 100m);
        var first = CreateSession(catalog);
        first.AddMarket(asset.AssetId);
        AddAgent(first, "Buyer", 100m);
        first.Submit(asset.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m));

        var second = CreateSession(catalog);
        second.AddMarket(asset.AssetId);
        AddAgent(second, "Buyer", 100m);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(100m, second.GetAgentAccount("Buyer").AvailableCash);
        Assert.Equal(0m, second.GetAgentAccount("Buyer").ReservedCash);
        Assert.Equal(0m, first.GetAgentAccount("Buyer").AvailableCash);
    }

    [Fact]
    public void DependencyInjectionCreatesTradingSessionUsingSharedCatalog()
    {
        using var provider = new ServiceCollection().AddABStockApplication().BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAssetCatalog>();
        var asset = CreateAsset(catalog, "Energy", 100m);
        var session = provider.GetRequiredService<ITradingSessionFactory>().Create();

        session.AddMarket(asset.AssetId);
        AddAgent(session, "Buyer", 100m);
        session.Submit(asset.AssetId, LimitOrder("Buyer", OrderSide.Buy, 100m));

        Assert.Equal(asset.AssetId, Assert.Single(session.GetMarkets()).AssetId);
        Assert.Equal(100m, session.GetAgentAccount("Buyer").ReservedCash);
    }

    private static (ITradingSession Session, IAssetCatalog Catalog, Asset First, Asset Second) CreateTwoMarkets()
    {
        var catalog = new InMemoryAssetCatalog();
        var first = CreateAsset(catalog, "Energy", 100m);
        var second = CreateAsset(catalog, "Metal", 200m);
        var session = CreateSession(catalog);
        session.AddMarket(first.AssetId);
        session.AddMarket(second.AssetId);
        return (session, catalog, first, second);
    }

    private static ITradingSession CreateSession(IAssetCatalog catalog) =>
        new TradingSessionFactory(new MarketSessionFactory(catalog, new ExchangeEngineFactory())).Create();

    private static Asset CreateAsset(IAssetCatalog catalog, string name, decimal price) =>
        catalog.Create(new CreateAssetRequest(new AssetProfile(name, AssetType.Stock, $"Description of {name}.", [], 0.9m), price));

    private static AgentAccountSnapshot AddAgent(
        ITradingSession session, string name, decimal cash, params (Guid AssetId, decimal Quantity)[] positions) =>
        session.AddAgent(new AgentAccountSpec(name, AgentType.TrendFollowing, cash)
        {
            InitialPositions = positions.ToDictionary(position => position.AssetId, position => position.Quantity)
        });

    private static Order LimitOrder(string agentName, OrderSide side, decimal price, decimal quantity = 1m) =>
        new(Guid.NewGuid(), agentName, side, OrderType.Limit, price, quantity, DateTimeOffset.UtcNow);

    private static Order MarketOrder(string agentName, OrderSide side, decimal quantity) =>
        new(Guid.NewGuid(), agentName, side, OrderType.Market, null, quantity, DateTimeOffset.UtcNow);
}
