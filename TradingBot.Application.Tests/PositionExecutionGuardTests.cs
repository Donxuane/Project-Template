using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class PositionExecutionGuardTests
{
    [Fact]
    public async Task SpotBuy_WithNoOpenPosition_IsAllowed()
    {
        var guard = CreateGuard(openPosition: null, allowAddToPosition: false);
        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            RequestedSide = OrderSide.BUY,
            RequestedQuantity = 0.01m
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task SpotBuy_WithOpenLong_AndAddDisabled_IsBlocked()
    {
        var guard = CreateGuard(
            openPosition: new Position
            {
                Id = 1,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.02m,
                IsOpen = true
            },
            allowAddToPosition: false);

        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            RequestedSide = OrderSide.BUY,
            RequestedQuantity = 0.01m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal("Spot BUY skipped because an open long position already exists and add-to-position is disabled.", result.Reason);
    }

    [Fact]
    public async Task SpotSell_WithNoOpenLong_IsBlocked()
    {
        var guard = CreateGuard(openPosition: null, allowAddToPosition: false);
        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.01m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal("Spot SELL skipped because no open long position exists.", result.Reason);
    }

    [Fact]
    public async Task SpotSell_WithOpenLong_IsAllowed()
    {
        var guard = CreateGuard(
            openPosition: new Position
            {
                Id = 2,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.02m,
                IsOpen = true
            },
            allowAddToPosition: false);
        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.01m
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task SpotSell_WithInFlightCloseOrder_IsBlocked()
    {
        var guard = CreateGuard(
            openPosition: new Position
            {
                Id = 64,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.15m,
                IsOpen = true
            },
            allowAddToPosition: false,
            hasInFlightCloseOrder: true);

        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.15m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal("Execution skipped - close order already in-flight for position.", result.Reason);
    }

    [Fact]
    public async Task SpotSell_QuantityGreaterThanOpenPosition_IsBlocked()
    {
        var guard = CreateGuard(
            openPosition: new Position
            {
                Id = 3,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.01m,
                IsOpen = true
            },
            allowAddToPosition: false);
        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.02m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("exceeds open long quantity", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FuturesIntent_IsBlockedBeforeSpotExecution()
    {
        var guard = CreateGuard(openPosition: null, allowAddToPosition: false);
        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Futures,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.OpenShort,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.01m
        });

        Assert.False(result.IsAllowed);
        Assert.Equal("Futures execution intent is not supported by the current spot execution pipeline.", result.Reason);
    }

    [Fact]
    public async Task TradeMonitor_ProtectiveCloseWithOpenPosition_IsAllowed()
    {
        var guard = CreateGuard(
            openPosition: new Position
            {
                Id = 4,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.05m,
                IsOpen = true
            },
            allowAddToPosition: false);
        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Hold,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.05m,
            IsProtectiveExit = true
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task TradeMonitor_ProtectiveCloseWithNoOpenPosition_IsBlockedSafely()
    {
        var guard = CreateGuard(openPosition: null, allowAddToPosition: false);
        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Hold,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.05m,
            IsProtectiveExit = true
        });

        Assert.False(result.IsAllowed);
        Assert.Equal("Spot SELL skipped because no open long position exists.", result.Reason);
    }

    [Fact]
    public async Task OpenLongCircuitBreaker_BlocksWhenConsecutiveLossesReached()
    {
        var now = DateTime.UtcNow;
        var guard = CreateGuard(
            openPosition: null,
            allowAddToPosition: false,
            closedPositions:
            [
                new Position { ClosedAt = now.AddMinutes(-5), RealizedPnl = -0.10m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-10), RealizedPnl = -0.20m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-20), RealizedPnl = -0.30m, IsOpen = false }
            ],
            circuitBreakerEnabled: true,
            maxConsecutiveLosses: 3,
            sessionLossLimitQuote: 10m);

        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            RequestedSide = OrderSide.BUY,
            RequestedQuantity = 0.01m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("consecutive realized losses", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenLongCircuitBreaker_BlocksWhenSessionLossLimitBreached()
    {
        var now = DateTime.UtcNow;
        var guard = CreateGuard(
            openPosition: null,
            allowAddToPosition: false,
            closedPositions:
            [
                new Position { ClosedAt = now.AddMinutes(-5), RealizedPnl = -0.40m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-10), RealizedPnl = -0.35m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-15), RealizedPnl = 0.05m, IsOpen = false }
            ],
            circuitBreakerEnabled: true,
            maxConsecutiveLosses: 10,
            sessionLossLimitQuote: 0.50m);

        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            RequestedSide = OrderSide.BUY,
            RequestedQuantity = 0.01m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("session realized loss limit", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenLongCircuitBreaker_DoesNotBlockCloseLong()
    {
        var now = DateTime.UtcNow;
        var guard = CreateGuard(
            openPosition: new Position
            {
                Id = 7,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.05m,
                IsOpen = true
            },
            allowAddToPosition: false,
            closedPositions:
            [
                new Position { ClosedAt = now.AddMinutes(-5), RealizedPnl = -0.40m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-10), RealizedPnl = -0.35m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-15), RealizedPnl = -0.25m, IsOpen = false }
            ],
            circuitBreakerEnabled: true,
            maxConsecutiveLosses: 2,
            sessionLossLimitQuote: 0.20m);

        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            RequestedSide = OrderSide.SELL,
            RequestedQuantity = 0.05m,
            IsProtectiveExit = true
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task OpenLongCircuitBreaker_LookbackExcludesOldLosses()
    {
        var now = DateTime.UtcNow;
        var guard = CreateGuard(
            openPosition: null,
            allowAddToPosition: false,
            closedPositions:
            [
                new Position { ClosedAt = now.AddHours(-2), RealizedPnl = -0.40m, IsOpen = false },
                new Position { ClosedAt = now.AddHours(-26), RealizedPnl = -0.35m, IsOpen = false },
                new Position { ClosedAt = now.AddHours(-30), RealizedPnl = -0.25m, IsOpen = false }
            ],
            circuitBreakerEnabled: true,
            lookbackHours: 24,
            maxConsecutiveLosses: 2,
            sessionLossLimitQuote: 1.00m);

        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            RequestedSide = OrderSide.BUY,
            RequestedQuantity = 0.01m
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task OpenLongCircuitBreaker_DefaultDisabled_PreservesOldBehavior()
    {
        var now = DateTime.UtcNow;
        var guard = CreateGuard(
            openPosition: null,
            allowAddToPosition: false,
            closedPositions:
            [
                new Position { ClosedAt = now.AddMinutes(-5), RealizedPnl = -1.00m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-10), RealizedPnl = -1.00m, IsOpen = false },
                new Position { ClosedAt = now.AddMinutes(-15), RealizedPnl = -1.00m, IsOpen = false }
            ],
            circuitBreakerEnabled: false);

        var result = await guard.EvaluateAsync(new PositionExecutionGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            RequestedSide = OrderSide.BUY,
            RequestedQuantity = 0.01m
        });

        Assert.True(result.IsAllowed);
    }

    private static PositionExecutionGuard CreateGuard(
        Position? openPosition,
        bool allowAddToPosition,
        bool hasInFlightCloseOrder = false,
        IReadOnlyList<Position>? closedPositions = null,
        bool circuitBreakerEnabled = false,
        int lookbackHours = 24,
        int maxConsecutiveLosses = 3,
        decimal sessionLossLimitQuote = 1.00m)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:AllowAddToPosition"] = allowAddToPosition ? "true" : "false",
                ["Trading:MaxOpenPositionsPerSymbol"] = "1",
                ["Trading:OpenLongCircuitBreaker:Enabled"] = circuitBreakerEnabled ? "true" : "false",
                ["Trading:OpenLongCircuitBreaker:LookbackHours"] = lookbackHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:OpenLongCircuitBreaker:MaxConsecutiveRealizedLosingTrades"] = maxConsecutiveLosses.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:OpenLongCircuitBreaker:SessionRealizedLossLimitQuote"] = sessionLossLimitQuote.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
            .Build();

        return new PositionExecutionGuard(
            configuration,
            new FakePositionRepository(openPosition, closedPositions ?? []),
            new FakeOrderRepository(hasInFlightCloseOrder),
            NullLogger<PositionExecutionGuard>.Instance);
    }

    private sealed class FakeOrderRepository(bool hasInFlightCloseOrder) : IOrderRepository
    {
        public Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default) => Task.FromResult(order.Id);
        public Task UpdateAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetFilledOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusAsync(ProcessingStatus processingStatus, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<int> GetInFlightOpeningOrderCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<bool> HasInFlightClosingOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(hasInFlightCloseOrder);
        public Task<bool> HasActiveCloseOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(hasInFlightCloseOrder);
        public Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(System.Data.IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(System.Data.IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
    }

    private sealed class FakePositionRepository(Position? openPosition, IReadOnlyList<Position> closedPositions) : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>(openPosition is null ? [] : [openPosition]);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult(closedPositions);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
