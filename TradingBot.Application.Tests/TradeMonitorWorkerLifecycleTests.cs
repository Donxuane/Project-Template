using System.Data;
using System.Reflection;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class TradeMonitorWorkerLifecycleTests
{
    [Fact]
    public async Task PlacesCloseOrder_WithoutDirectPositionFinalization()
    {
        var position = new Position
        {
            Id = 23,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            AveragePrice = 617.24m,
            StopLossPrice = 617.30m,
            ExitPrice = null,
            ExitReason = null,
            RealizedPnl = 0m,
            UnrealizedPnl = 0m,
            IsOpen = true,
            IsClosing = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            OpenedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var positionRepository = new FakePositionRepository([position]);
        var orderRepository = new FakeOrderRepository(activeCloseOrder: false);
        var mediator = new CapturingMediator(success: true);
        var worker = BuildWorker(
            positionRepository,
            orderRepository,
            new FixedPriceCacheService(617.20m),
            mediator,
            new AllowIdempotencyService(),
            new AllowPositionExecutionGuard());

        await InvokeMonitorOpenPositionsAsync(worker);

        Assert.Equal(1, mediator.PlaceOrderCalls);
        Assert.NotNull(mediator.LastCommand);
        Assert.Equal(OrderSource.TradeMonitorWorker, mediator.LastCommand!.OrderSource);
        Assert.Equal(CloseReason.StopLoss, mediator.LastCommand.CloseReason);
        Assert.Equal(position.Id, mediator.LastCommand.ParentPositionId);
        Assert.Equal(OrderSide.SELL, mediator.LastCommand.Side);
        Assert.Equal(0.01m, mediator.LastCommand.Quantity);

        Assert.Equal(1, positionRepository.TryMarkCalls);
        Assert.Equal(0, positionRepository.ClearClosingCalls);
        Assert.Empty(positionRepository.UpsertedSnapshots);
    }

    [Fact]
    public async Task ActiveCloseOrder_PreventsDuplicateCloseRequest()
    {
        var position = new Position
        {
            Id = 59,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            AveragePrice = 617.24m,
            StopLossPrice = 617.30m,
            IsOpen = true,
            IsClosing = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            OpenedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var positionRepository = new FakePositionRepository([position]);
        var orderRepository = new FakeOrderRepository(activeCloseOrder: true);
        var mediator = new CapturingMediator(success: true);
        var worker = BuildWorker(
            positionRepository,
            orderRepository,
            new FixedPriceCacheService(617.20m),
            mediator,
            new AllowIdempotencyService(),
            new AllowPositionExecutionGuard());

        await InvokeMonitorOpenPositionsAsync(worker);

        Assert.Equal(0, mediator.PlaceOrderCalls);
        Assert.Equal(0, positionRepository.TryMarkCalls);
        Assert.Equal(0, positionRepository.ClearClosingCalls);
        Assert.Empty(positionRepository.UpsertedSnapshots);
    }

    [Fact]
    public async Task PositionCloseLock_FailsWhenAlreadyClosingOrUnavailable()
    {
        var position = new Position
        {
            Id = 77,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            AveragePrice = 617.24m,
            StopLossPrice = 617.30m,
            IsOpen = true,
            IsClosing = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            OpenedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var positionRepository = new FakePositionRepository([position]) { TryMarkResult = false };
        var worker = BuildWorker(
            positionRepository,
            new FakeOrderRepository(activeCloseOrder: false),
            new FixedPriceCacheService(617.20m),
            new CapturingMediator(success: true),
            new AllowIdempotencyService(),
            new AllowPositionExecutionGuard());

        await InvokeMonitorOpenPositionsAsync(worker);

        Assert.Equal(1, positionRepository.TryMarkCalls);
        Assert.Equal(0, positionRepository.ClearClosingCalls);
    }

    [Fact]
    public async Task ClearsCloseLock_WhenCloseOrderPlacementFails()
    {
        var position = new Position
        {
            Id = 88,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            AveragePrice = 617.24m,
            StopLossPrice = 617.30m,
            IsOpen = true,
            IsClosing = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            OpenedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var positionRepository = new FakePositionRepository([position]);
        var worker = BuildWorker(
            positionRepository,
            new FakeOrderRepository(activeCloseOrder: false),
            new FixedPriceCacheService(617.20m),
            new CapturingMediator(success: false),
            new AllowIdempotencyService(),
            new AllowPositionExecutionGuard());

        await InvokeMonitorOpenPositionsAsync(worker);

        Assert.Equal(1, positionRepository.TryMarkCalls);
        Assert.Equal(1, positionRepository.ClearClosingCalls);
    }

    [Fact]
    public async Task DoesNotClearCloseLock_AfterSuccessfulCloseOrderPlacement()
    {
        var position = new Position
        {
            Id = 99,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            AveragePrice = 617.24m,
            StopLossPrice = 617.30m,
            IsOpen = true,
            IsClosing = false,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            OpenedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        var positionRepository = new FakePositionRepository([position]);
        var worker = BuildWorker(
            positionRepository,
            new FakeOrderRepository(activeCloseOrder: false),
            new FixedPriceCacheService(617.20m),
            new CapturingMediator(success: true),
            new AllowIdempotencyService(),
            new AllowPositionExecutionGuard());

        await InvokeMonitorOpenPositionsAsync(worker);

        Assert.Equal(1, positionRepository.TryMarkCalls);
        Assert.Equal(0, positionRepository.ClearClosingCalls);
    }

    [Theory]
    [InlineData(true, false, 0.01, true)]
    [InlineData(true, true, 0.01, false)]
    [InlineData(false, false, 0.01, false)]
    [InlineData(true, false, 0.0, false)]
    public async Task TryMarkPositionClosing_FollowsDurableLockContract(bool isOpen, bool isClosing, decimal quantity, bool expected)
    {
        var position = new Position
        {
            Id = 501,
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = quantity,
            IsOpen = isOpen,
            IsClosing = isClosing
        };
        var repository = new FakePositionRepository([position]);

        var result = await repository.TryMarkPositionClosingAsync(position.Id, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    private static TradeMonitorWorker BuildWorker(
        IPositionRepository positionRepository,
        IOrderRepository orderRepository,
        IPriceCacheService priceCacheService,
        IMediator mediator,
        ITradeIdempotencyService idempotencyService,
        IPositionExecutionGuard positionExecutionGuard)
    {
        var provider = new DictionaryServiceProvider(new Dictionary<Type, object>
        {
            [typeof(IPositionRepository)] = positionRepository,
            [typeof(IOrderRepository)] = orderRepository,
            [typeof(IPriceCacheService)] = priceCacheService,
            [typeof(IMediator)] = mediator,
            [typeof(ITradeIdempotencyService)] = idempotencyService,
            [typeof(IPositionExecutionGuard)] = positionExecutionGuard
        });

        var scopeFactory = new SingleScopeFactory(provider);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TradeMonitoring:MaxTradeDurationMinutes"] = "60",
                ["TradeMonitoring:EnableStopLossExit"] = "true",
                ["TradeMonitoring:EnableTakeProfitExit"] = "false",
                ["TradeMonitoring:EnableTimeExit"] = "false",
                ["TradeMonitoring:EnableTrailingStop"] = "false",
                ["TradeMonitoring:EnableBreakEvenStop"] = "false"
            })
            .Build();

        return new TradeMonitorWorker(scopeFactory, configuration, NullLogger<TradeMonitorWorker>.Instance);
    }

    private static async Task InvokeMonitorOpenPositionsAsync(TradeMonitorWorker worker)
    {
        var settingsType = typeof(TradeMonitorWorker).GetNestedType("TradeMonitoringSettings", BindingFlags.NonPublic);
        var method = typeof(TradeMonitorWorker).GetMethod("MonitorOpenPositionsAsync", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(settingsType);
        Assert.NotNull(method);

        var settings = Activator.CreateInstance(settingsType!);
        settingsType!.GetProperty("IntervalSeconds")!.SetValue(settings, 10);
        settingsType.GetProperty("MaxTradeDurationMinutes")!.SetValue(settings, 60);
        settingsType.GetProperty("CloseOrderMaxRetries")!.SetValue(settings, 1);
        settingsType.GetProperty("CloseOrderRetryDelayMs")!.SetValue(settings, 100);
        settingsType.GetProperty("CloseIdempotencyWindowSeconds")!.SetValue(settings, 300);
        settingsType.GetProperty("EnableTimeExit")!.SetValue(settings, false);
        settingsType.GetProperty("EnableStopLossExit")!.SetValue(settings, true);
        settingsType.GetProperty("EnableTakeProfitExit")!.SetValue(settings, false);
        settingsType.GetProperty("EnableTrailingStop")!.SetValue(settings, false);
        settingsType.GetProperty("TrailingStopPercent")!.SetValue(settings, 0.5m);
        settingsType.GetProperty("EnableBreakEvenStop")!.SetValue(settings, false);
        settingsType.GetProperty("BreakEvenTriggerPercent")!.SetValue(settings, 0.5m);

        var task = (Task)method!.Invoke(worker, new[] { settings!, CancellationToken.None })!;
        await task;
    }

    private sealed class SingleScopeFactory(IServiceProvider provider) : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new SingleScope(provider);
    }

    private sealed class SingleScope(IServiceProvider provider) : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; } = provider;
        public void Dispose()
        {
        }
    }

    private sealed class DictionaryServiceProvider(Dictionary<Type, object> services) : IServiceProvider
    {
        public object? GetService(Type serviceType)
            => services.TryGetValue(serviceType, out var service) ? service : null;
    }

    private sealed class FixedPriceCacheService(decimal price) : IPriceCacheService
    {
        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<decimal?>(price);

        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class AllowIdempotencyService : ITradeIdempotencyService
    {
        public Task<bool> TryRegisterDecisionAsync(string decisionId, int windowSeconds, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class AllowPositionExecutionGuard : IPositionExecutionGuard
    {
        public Task<PositionExecutionGuardResult> EvaluateAsync(PositionExecutionGuardRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PositionExecutionGuardResult
            {
                IsAllowed = true,
                Reason = "ok",
                OpenPositionQuantity = request.RequestedQuantity
            });
    }

    private sealed class CapturingMediator(bool success) : IMediator
    {
        public int PlaceOrderCalls { get; private set; }
        public PlaceSpotOrderCommand? LastCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PlaceSpotOrderCommand command)
            {
                PlaceOrderCalls++;
                LastCommand = command;
                object result = new PlaceSpotOrderResult
                {
                    Success = success,
                    Order = success
                        ? new Order
                        {
                            Id = 101,
                            ExchangeOrderId = 202,
                            Symbol = command.Symbol,
                            Side = command.Side,
                            Quantity = command.Quantity,
                            Status = OrderStatuses.FILLED,
                            ProcessingStatus = ProcessingStatus.OrderPlaced
                        }
                        : null,
                    Error = success ? null : "failed"
                };
                return Task.FromResult((TResponse)result);
            }

            throw new NotSupportedException();
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse> => Send((IRequest<TResponse>)request, cancellationToken);
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => Task.CompletedTask;
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => EmptyAsync<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => EmptyAsync<object?>();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;

        private static async IAsyncEnumerable<T> EmptyAsync<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakeOrderRepository(bool activeCloseOrder) : IOrderRepository
    {
        public Task<long> InsertAsync(Order order, CancellationToken cancellationToken = default) => Task.FromResult(1L);
        public Task UpdateAsync(Order order, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Order?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<Order?> GetByExchangeOrderIdAsync(long exchangeOrderId, CancellationToken cancellationToken = default) => Task.FromResult<Order?>(null);
        public Task<IReadOnlyList<Order>> GetOpenOrdersAsync(TradingSymbol? symbol = null, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetFilledOrdersAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusAsync(ProcessingStatus processingStatus, int? limit = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<bool> HasActiveCloseOrderForPositionAsync(long parentPositionId, CancellationToken cancellationToken = default) => Task.FromResult(activeCloseOrder);
        public Task<IReadOnlyList<Order>> GetOpenOrdersForWorkerAsync(IDbTransaction transaction, TradingSymbol? symbol, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
        public Task<IReadOnlyList<Order>> GetOrdersByProcessingStatusForWorkerAsync(IDbTransaction transaction, ProcessingStatus processingStatus, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Order>>([]);
    }

    private sealed class FakePositionRepository(IReadOnlyList<Position> openPositions) : IPositionRepository
    {
        public bool? TryMarkResult { get; set; }
        public int TryMarkCalls { get; private set; }
        public int ClearClosingCalls { get; private set; }
        public List<Position> UpsertedSnapshots { get; } = [];

        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default)
        {
            UpsertedSnapshots.Add(Clone(position));
            return Task.FromResult(position.Id == 0 ? 1L : position.Id);
        }

        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<Position?>(openPositions.FirstOrDefault(x => x.Id == id));

        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<Position?>(openPositions.FirstOrDefault(x => x.Symbol == symbol && x.IsOpen));

        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(openPositions);

        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Position>>([]);

        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
        {
            TryMarkCalls++;
            if (TryMarkResult.HasValue)
                return Task.FromResult(TryMarkResult.Value);

            var position = openPositions.FirstOrDefault(x => x.Id == positionId);
            if (position is null)
                return Task.FromResult(false);

            if (!position.IsOpen || position.IsClosing || position.Quantity <= 0m)
                return Task.FromResult(false);

            position.IsClosing = true;
            return Task.FromResult(true);
        }

        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
        {
            ClearClosingCalls++;
            var position = openPositions.FirstOrDefault(x => x.Id == positionId);
            if (position is not null)
                position.IsClosing = false;
            return Task.CompletedTask;
        }

        private static Position Clone(Position position)
        {
            return new Position
            {
                Id = position.Id,
                Symbol = position.Symbol,
                Side = position.Side,
                Quantity = position.Quantity,
                AveragePrice = position.AveragePrice,
                StopLossPrice = position.StopLossPrice,
                TakeProfitPrice = position.TakeProfitPrice,
                ExitPrice = position.ExitPrice,
                ExitReason = position.ExitReason,
                OpenedAt = position.OpenedAt,
                ClosedAt = position.ClosedAt,
                RealizedPnl = position.RealizedPnl,
                UnrealizedPnl = position.UnrealizedPnl,
                CreatedAt = position.CreatedAt,
                UpdatedAt = position.UpdatedAt,
                IsOpen = position.IsOpen,
                IsClosing = position.IsClosing
            };
        }
    }
}
