using System.Reflection;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class PositionWorkerLifecycleConsistencyTests
{
    private static readonly MethodInfo AreAllExecutionsProcessedMethod =
        typeof(PositionWorker).GetMethod("AreAllExecutionsProcessed", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo IsExpectedAlreadyAccountedClosePathMethod =
        typeof(PositionWorker).GetMethod("IsExpectedAlreadyAccountedClosePath", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo MapExitReasonMethod =
        typeof(PositionWorker).GetMethod("MapExitReason", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ResolvePositionForOrderMethod =
        typeof(PositionWorker).GetMethod("ResolvePositionForOrderAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void ProcessedTrades_CanBeCompletedToPositionUpdated()
    {
        var trades = new List<TradeExecution>
        {
            CreateExecution(1, DateTime.UtcNow.AddSeconds(-5)),
            CreateExecution(2, DateTime.UtcNow)
        };

        var result = (bool)AreAllExecutionsProcessedMethod.Invoke(null, new object[] { trades })!;
        Assert.True(result);
    }

    [Fact]
    public void UnprocessedTrades_CannotBeCompletedUntilMarked()
    {
        var trades = new List<TradeExecution>
        {
            CreateExecution(1, DateTime.UtcNow),
            CreateExecution(2, null)
        };

        var result = (bool)AreAllExecutionsProcessedMethod.Invoke(null, new object[] { trades })!;
        Assert.False(result);
    }

    [Fact]
    public void AlreadyClosedParent_TradeMonitorClosePath_IsExpectedAlreadyAccounted()
    {
        var order = new Order
        {
            Side = OrderSide.SELL,
            OrderSource = OrderSource.TradeMonitorWorker,
            CloseReason = CloseReason.MaxDuration,
            ParentPositionId = 23
        };
        var parentPosition = new Position
        {
            Id = 23,
            IsOpen = false
        };

        var result = (bool)IsExpectedAlreadyAccountedClosePathMethod.Invoke(null, new object[] { order, parentPosition })!;
        Assert.True(result);
    }

    [Fact]
    public void UnexpectedZeroProcessedPath_IsNotExpectedAlreadyAccounted()
    {
        var order = new Order
        {
            Side = OrderSide.SELL,
            OrderSource = OrderSource.DecisionWorker,
            CloseReason = CloseReason.OppositeSignal,
            ParentPositionId = 59
        };
        var parentPosition = new Position
        {
            Id = 59,
            IsOpen = true
        };

        var result = (bool)IsExpectedAlreadyAccountedClosePathMethod.Invoke(null, new object[] { order, parentPosition })!;
        Assert.False(result);
    }

    [Fact]
    public void MaxDurationCloseReason_MapsToTimeExitReason()
    {
        var mapped = (PositionExitReason?)MapExitReasonMethod.Invoke(null, new object[] { CloseReason.MaxDuration });
        Assert.Equal(PositionExitReason.Time, mapped);
    }

    [Fact]
    public async Task ResolvePositionForOrder_HonorsParentPositionIdWithoutFallback()
    {
        var order = new Order
        {
            ParentPositionId = 42,
            Symbol = TradingSymbol.BNBUSDT
        };
        var repository = new ResolvePositionRepository
        {
            PositionById = null,
            OpenPosition = new Position { Id = 99, Symbol = TradingSymbol.BNBUSDT, IsOpen = true, Quantity = 0.01m }
        };

        var task = (Task<Position?>)ResolvePositionForOrderMethod.Invoke(
            null,
            new object[] { order, repository, CancellationToken.None })!;
        var resolved = await task;

        Assert.Null(resolved);
    }

    private static TradeExecution CreateExecution(long id, DateTime? processedAt)
    {
        return new TradeExecution
        {
            Id = id,
            OrderId = 100,
            ExchangeOrderId = 200,
            ExchangeTradeId = id,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.SELL,
            Price = 618.01m,
            Quantity = 0.01m,
            ExecutedAt = DateTime.UtcNow,
            PositionProcessedAt = processedAt
        };
    }

    private sealed class ResolvePositionRepository : IPositionRepository
    {
        public Position? PositionById { get; set; }
        public Position? OpenPosition { get; set; }

        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(PositionById);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult(OpenPosition);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
