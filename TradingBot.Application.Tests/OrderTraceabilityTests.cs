using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.BackgroundHostService;
using TradingBot.Application.BackgroundHostService.Services;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class OrderTraceabilityTests
{
    [Fact]
    public async Task DecisionExecution_UsesDecisionWorkerSource_AndNoneCloseReason()
    {
        var mediator = new CapturingMediator();
        var positions = new FakePositionRepository();
        var service = new TradeExecutionService(
            mediator,
            BuildConfiguration(),
            positions,
            NullLogger<TradeExecutionService>.Instance);

        await service.ExecuteMarketOrderAsync(
            new TradeExecutionRequest
            {
                CorrelationId = "corr-123",
                DecisionId = "dec-123",
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.01m,
                TradingMode = TradingMode.Spot,
                RawSignal = TradeSignal.Buy,
                ExecutionIntent = TradeExecutionIntent.OpenLong
            },
            CancellationToken.None);

        Assert.NotNull(mediator.LastPlaceSpotOrderCommand);
        Assert.Equal(OrderSource.DecisionWorker, mediator.LastPlaceSpotOrderCommand!.OrderSource);
        Assert.Equal(CloseReason.None, mediator.LastPlaceSpotOrderCommand.CloseReason);
        Assert.Equal("corr-123", mediator.LastPlaceSpotOrderCommand.CorrelationId);
    }

    [Fact]
    public async Task DecisionCloseLong_SetsParentPositionId_AndOppositeSignalCloseReason()
    {
        var mediator = new CapturingMediator();
        var positions = new FakePositionRepository
        {
            OpenPosition = new Position
            {
                Id = 42,
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.03m,
                AveragePrice = 623m,
                IsOpen = true
            }
        };

        var service = new TradeExecutionService(
            mediator,
            BuildConfiguration(),
            positions,
            NullLogger<TradeExecutionService>.Instance);

        await service.ExecuteMarketOrderAsync(
            new TradeExecutionRequest
            {
                CorrelationId = "corr-close",
                DecisionId = "dec-close",
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.SELL,
                Quantity = 0.01m,
                TradingMode = TradingMode.Spot,
                RawSignal = TradeSignal.Sell,
                ExecutionIntent = TradeExecutionIntent.CloseLong
            },
            CancellationToken.None);

        Assert.NotNull(mediator.LastPlaceSpotOrderCommand);
        Assert.Equal(42, mediator.LastPlaceSpotOrderCommand!.ParentPositionId);
        Assert.Equal(CloseReason.OppositeSignal, mediator.LastPlaceSpotOrderCommand.CloseReason);
    }

    [Theory]
    [InlineData(PositionExitReason.StopLoss, CloseReason.StopLoss)]
    [InlineData(PositionExitReason.TakeProfit, CloseReason.TakeProfit)]
    [InlineData(PositionExitReason.Time, CloseReason.MaxDuration)]
    public void TradeMonitor_MapsExitReason_ToCloseReason(PositionExitReason exitReason, CloseReason expected)
    {
        var method = typeof(TradeMonitorWorker).GetMethod("MapCloseReason", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var mapped = (CloseReason)method!.Invoke(null, new object[] { exitReason })!;
        Assert.Equal(expected, mapped);
    }

    [Fact]
    public void ManualOrApiPath_CommandCarriesProvidedSource()
    {
        var command = new PlaceSpotOrderCommand(
            TradingSymbol.BNBUSDT,
            OrderSide.SELL,
            0.01m,
            Price: null,
            IsLimitOrder: false,
            OrderSource.Api,
            CloseReason.ManualClose,
            ParentPositionId: null,
            CorrelationId: "api-corr");

        Assert.Equal(OrderSource.Api, command.OrderSource);
        Assert.Equal(CloseReason.ManualClose, command.CloseReason);
        Assert.Equal("api-corr", command.CorrelationId);
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExecutionSettings:Retry:Count"] = "0",
                ["ExecutionSettings:Retry:BaseDelayMs"] = "100"
            })
            .Build();
    }

    private sealed class CapturingMediator : IMediator
    {
        public PlaceSpotOrderCommand? LastPlaceSpotOrderCommand { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is PlaceSpotOrderCommand cmd)
            {
                LastPlaceSpotOrderCommand = cmd;
                object result = new PlaceSpotOrderResult
                {
                    Success = true,
                    Order = new Order
                    {
                        Id = 1,
                        ExchangeOrderId = 2,
                        Symbol = cmd.Symbol,
                        Side = cmd.Side,
                        Status = OrderStatuses.FILLED,
                        ProcessingStatus = ProcessingStatus.OrderPlaced,
                        Quantity = cmd.Quantity,
                        Price = cmd.Price ?? 631m
                    }
                };
                return Task.FromResult((TResponse)result);
            }

            throw new NotSupportedException("Unexpected request type in test mediator.");
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<TResponse> Send<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest<TResponse>
            => Send((IRequest<TResponse>)request, cancellationToken);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest
            => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
            => EmptyAsync<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => EmptyAsync<object?>();

        public Task Publish(object notification, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification
            => Task.CompletedTask;

        private static async IAsyncEnumerable<T> EmptyAsync<T>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class FakePositionRepository : IPositionRepository
    {
        public Position? OpenPosition { get; set; }

        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default)
            => Task.FromResult(position.Id == 0 ? 1L : position.Id);

        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult<Position?>(null);

        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(OpenPosition?.Symbol == symbol ? OpenPosition : null);

        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Position>>(Array.Empty<Position>());

        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Position>>(Array.Empty<Position>());

        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
