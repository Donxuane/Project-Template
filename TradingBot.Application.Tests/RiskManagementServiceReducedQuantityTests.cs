using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.MarketData;
using TradingBot.Domain.Models.Trading;
using TradingBot.Percistance.Services.Main;
using Xunit;

namespace TradingBot.Application.Tests;

public class RiskManagementServiceReducedQuantityTests
{
    [Fact]
    public async Task ReducedQuantity_BelowMinOrderQuote_IsRejectedSafely()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RiskSettings:MinOrderQuote"] = "5",
                ["RiskSettings:MaxOrderQuote"] = "1000",
                ["RiskSettings:MaxPositionQuote"] = "1000",
                ["RiskSettings:MaxExposurePercent"] = "80",
                ["RiskSettings:MaxPositionSize"] = "10",
                ["RiskSettings:EnableDailyLossLimit"] = "false",
                ["RiskSettings:AllowShortSelling"] = "false",
                ["RiskSettings:MinimumRiskScore"] = "0",
                ["RiskSettings:QuoteAsset"] = "USDT"
            })
            .Build();

        var orderRepository = new FakeOrderRepository();
        var service = new RiskManagementService(
            configuration,
            new FakePositionRepository(),
            orderRepository,
            new FakeBalanceRepository(),
            new FakePriceCacheService(1000m),
            NullLogger<RiskManagementService>.Instance);

        var result = await service.CheckOrderAsync(
            TradingSymbol.SOLUSDT,
            OrderSide.BUY,
            quantity: 0.0025m,
            price: 1000m,
            requiresReducedPositionSize: true,
            tradingMode: TradingMode.Spot,
            rawSignal: TradeSignal.Buy,
            executionIntent: TradeExecutionIntent.OpenLong);

        Assert.False(result.IsAllowed);
        Assert.True(result.RequiresReducedPositionSize);
        Assert.Contains("below minimum", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GlobalMaxOpenPositions_NoOpenNoInFlight_AllowsBuy()
    {
        var configuration = BuildMaxOpenConfig(maxOpenPositions: 1);
        var orderRepository = new FakeOrderRepository { InFlightOpeningOrderCount = 0 };
        var service = new RiskManagementService(
            configuration,
            new FakePositionRepository(),
            orderRepository,
            new FakeBalanceRepository(),
            new FakePriceCacheService(1000m),
            NullLogger<RiskManagementService>.Instance);

        var result = await service.CheckOrderAsync(
            TradingSymbol.BNBUSDT,
            OrderSide.BUY,
            quantity: 0.01m,
            price: 1000m,
            requiresReducedPositionSize: false,
            tradingMode: TradingMode.Spot,
            rawSignal: TradeSignal.Buy,
            executionIntent: TradeExecutionIntent.OpenLong);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task GlobalMaxOpenPositions_WithPersistedOpenPosition_BlocksNewBuy()
    {
        var configuration = BuildMaxOpenConfig(maxOpenPositions: 1);
        var orderRepository = new FakeOrderRepository { InFlightOpeningOrderCount = 0 };
        var service = new RiskManagementService(
            configuration,
            new FakePositionRepository([CreateOpenPosition(TradingSymbol.ETHUSDT)]),
            orderRepository,
            new FakeBalanceRepository(),
            new FakePriceCacheService(1000m),
            NullLogger<RiskManagementService>.Instance);

        var result = await service.CheckOrderAsync(
            TradingSymbol.BNBUSDT,
            OrderSide.BUY,
            quantity: 0.01m,
            price: 1000m,
            requiresReducedPositionSize: false,
            tradingMode: TradingMode.Spot,
            rawSignal: TradeSignal.Buy,
            executionIntent: TradeExecutionIntent.OpenLong);

        Assert.False(result.IsAllowed);
        Assert.Contains("including in-flight opening orders", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GlobalMaxOpenPositions_WithInFlightOpeningBuy_BlocksNewBuy()
    {
        var configuration = BuildMaxOpenConfig(maxOpenPositions: 1);
        var orderRepository = new FakeOrderRepository { InFlightOpeningOrderCount = 1 };
        var service = new RiskManagementService(
            configuration,
            new FakePositionRepository(),
            orderRepository,
            new FakeBalanceRepository(),
            new FakePriceCacheService(1000m),
            NullLogger<RiskManagementService>.Instance);

        var result = await service.CheckOrderAsync(
            TradingSymbol.SOLUSDT,
            OrderSide.BUY,
            quantity: 0.01m,
            price: 1000m,
            requiresReducedPositionSize: false,
            tradingMode: TradingMode.Spot,
            rawSignal: TradeSignal.Buy,
            executionIntent: TradeExecutionIntent.OpenLong);

        Assert.False(result.IsAllowed);
        Assert.Contains("including in-flight opening orders", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GlobalMaxOpenPositions_FilledBuyWaitingForPositionWorker_CountsAsInFlight()
    {
        var configuration = BuildMaxOpenConfig(maxOpenPositions: 1);
        var orderRepository = new FakeOrderRepository { InFlightOpeningOrderCount = 1 };
        var service = new RiskManagementService(
            configuration,
            new FakePositionRepository(),
            orderRepository,
            new FakeBalanceRepository(),
            new FakePriceCacheService(1000m),
            NullLogger<RiskManagementService>.Instance);

        var result = await service.CheckOrderAsync(
            TradingSymbol.BNBUSDT,
            OrderSide.BUY,
            quantity: 0.01m,
            price: 1000m,
            requiresReducedPositionSize: false,
            tradingMode: TradingMode.Spot,
            rawSignal: TradeSignal.Buy,
            executionIntent: TradeExecutionIntent.OpenLong);

        Assert.False(result.IsAllowed);
    }

    [Fact]
    public async Task GlobalMaxOpenPositions_PositionUpdatedBuy_DoesNotDoubleCountAsInFlight()
    {
        var configuration = BuildMaxOpenConfig(maxOpenPositions: 2);
        var orderRepository = new FakeOrderRepository { InFlightOpeningOrderCount = 0 };
        var service = new RiskManagementService(
            configuration,
            new FakePositionRepository([CreateOpenPosition(TradingSymbol.ETHUSDT)]),
            orderRepository,
            new FakeBalanceRepository(),
            new FakePriceCacheService(1000m),
            NullLogger<RiskManagementService>.Instance);

        var result = await service.CheckOrderAsync(
            TradingSymbol.BNBUSDT,
            OrderSide.BUY,
            quantity: 0.01m,
            price: 1000m,
            requiresReducedPositionSize: false,
            tradingMode: TradingMode.Spot,
            rawSignal: TradeSignal.Buy,
            executionIntent: TradeExecutionIntent.OpenLong);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task GlobalMaxOpenPositions_CloseLongSell_IsNotBlockedByGlobalCap()
    {
        var configuration = BuildMaxOpenConfig(maxOpenPositions: 1);
        var orderRepository = new FakeOrderRepository { InFlightOpeningOrderCount = 5 };
        var service = new RiskManagementService(
            configuration,
            new FakePositionRepository([CreateOpenPosition(TradingSymbol.ETHUSDT)]),
            orderRepository,
            new FakeBalanceRepository(
            [
                new BalanceSnapshot
                {
                    Asset = "USDT",
                    AssetId = Assets.USDT,
                    Free = 1000m,
                    Locked = 0m,
                    UpdatedAt = DateTime.UtcNow
                },
                new BalanceSnapshot
                {
                    Asset = "ETH",
                    AssetId = Assets.ETH,
                    Free = 1m,
                    Locked = 0m,
                    UpdatedAt = DateTime.UtcNow
                }
            ]),
            new FakePriceCacheService(1000m),
            NullLogger<RiskManagementService>.Instance);

        var result = await service.CheckOrderAsync(
            TradingSymbol.ETHUSDT,
            OrderSide.SELL,
            quantity: 0.01m,
            price: 1000m,
            requiresReducedPositionSize: false,
            tradingMode: TradingMode.Spot,
            rawSignal: TradeSignal.Sell,
            executionIntent: TradeExecutionIntent.CloseLong);

        Assert.True(result.IsAllowed);
    }

    private static IConfiguration BuildMaxOpenConfig(int maxOpenPositions)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RiskSettings:MinOrderQuote"] = "5",
                ["RiskSettings:MaxOrderQuote"] = "1000",
                ["RiskSettings:MaxPositionQuote"] = "1000",
                ["RiskSettings:MaxExposurePercent"] = "80",
                ["RiskSettings:MaxPositionSize"] = "10",
                ["RiskSettings:EnableDailyLossLimit"] = "false",
                ["RiskSettings:AllowShortSelling"] = "false",
                ["RiskSettings:MinimumRiskScore"] = "0",
                ["RiskSettings:QuoteAsset"] = "USDT",
                ["RiskSettings:MaxOpenPositions"] = maxOpenPositions.ToString()
            })
            .Build();
    }

    private static Position CreateOpenPosition(TradingSymbol symbol)
    {
        return new Position
        {
            Id = 1,
            Symbol = symbol,
            Side = OrderSide.BUY,
            Quantity = 0.02m,
            AveragePrice = 1000m,
            IsOpen = true,
            OpenedAt = DateTime.UtcNow.AddMinutes(-5)
        };
    }

    private sealed class FakePriceCacheService(decimal price) : IPriceCacheService
    {
        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<decimal?>(price);

        public Task<PriceSnapshot?> GetCachedPriceSnapshotAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<PriceSnapshot?>(new PriceSnapshot
            {
                Price = price,
                AsOfUtc = DateTime.UtcNow,
                Source = "RedisTicker"
            });

        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakePositionRepository(IReadOnlyList<Position>? openPositions = null) : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<Position?>(null);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(openPositions?.FirstOrDefault(x => x.Symbol == symbol && x.IsOpen));
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Position>>(openPositions ?? []);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public int InFlightOpeningOrderCount { get; set; }

        public Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task UpdateAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetFilledOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusAsync(ProcessingStatus processingStatus, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<int> GetInFlightOpeningOrderCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(InFlightOpeningOrderCount);
        public Task<bool> HasInFlightClosingOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> HasActiveCloseOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
    }

    private sealed class FakeBalanceRepository(IReadOnlyList<BalanceSnapshot>? balances = null) : IBalanceRepository
    {
        public Task<long> InsertAsync(BalanceSnapshot snapshot, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task UpsertLatestAsync(List<BalanceSnapshot> snapshot, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<BalanceSyncWriteResult> UpsertLatestAndAppendHistoryAsync(
            IReadOnlyList<BalanceSnapshot> snapshots,
            string source,
            string? syncCorrelationId,
            bool forceSnapshot = false,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new BalanceSyncWriteResult
            {
                AssetsFetched = snapshots.Count,
                LatestRowsUpserted = snapshots.Count,
                HistoryRowsInserted = snapshots.Count
            });

        public Task<BalanceSnapshot?> GetLatestByAssetAsync(string asset, Assets assetId, CancellationToken cancellationToken = default)
            => Task.FromResult<BalanceSnapshot?>(null);

        public Task<BalanceSnapshot?> GetLatestAsync(string asset, TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<BalanceSnapshot?>(null);

        public Task<IReadOnlyList<BalanceSnapshot>> GetLatestForAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BalanceSnapshot>>(balances ??
            [
                new BalanceSnapshot
                {
                    Asset = "USDT",
                    AssetId = Assets.USDT,
                    Free = 1000m,
                    Locked = 0m,
                    UpdatedAt = DateTime.UtcNow
                }
            ]);

        public Task<IReadOnlyList<BalanceSnapshot>> GetStaleBalancesAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BalanceSnapshot>>([]);
    }
}
