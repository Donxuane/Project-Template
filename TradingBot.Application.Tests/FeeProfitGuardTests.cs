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

public class FeeProfitGuardTests
{
    [Fact]
    public async Task SpotOpenLong_Allowed_WhenNetProfitAboveThreshold()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 101m
        });

        Assert.True(result.IsAllowed);
        Assert.True(result.NetExpectedProfitPercent > 0.15m);
    }

    [Fact]
    public async Task SpotOpenLong_Blocked_WhenGrossMoveBelowMinimum()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 100.2m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("gross move", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotOpenLong_Blocked_WhenNetProfitBelowMinimumAfterFeesAndSpread()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 100.35m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("net profit", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisabledFeeGuard_AllowsExecution()
    {
        var guard = CreateGuard(useFeeGuard: false);
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Buy,
            ExecutionIntent = TradeExecutionIntent.OpenLong,
            Side = OrderSide.BUY,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 100.05m
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task SpotCloseLong_UsesLongProfitCalculation_NotShortStyle()
    {
        var guard = CreateGuard(openPosition: new Position
        {
            Symbol = TradingSymbol.BNBUSDT,
            Side = OrderSide.BUY,
            Quantity = 0.02m,
            AveragePrice = 100m,
            IsOpen = true
        });
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Side = OrderSide.SELL,
            Quantity = 0.01m,
            EntryPrice = 101m
        });

        Assert.True(result.IsAllowed);
        Assert.True(result.GrossExpectedProfitPercent > 0m);
    }

    [Fact]
    public async Task ProtectiveClose_BypassesFeeGuard()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Spot,
            RawSignal = TradeSignal.Hold,
            ExecutionIntent = TradeExecutionIntent.CloseLong,
            Side = OrderSide.SELL,
            Quantity = 0.01m,
            EntryPrice = 99m,
            TargetPrice = 99m,
            IsProtectiveExit = true
        });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task FuturesIntent_RemainsUnsupported()
    {
        var guard = CreateGuard();
        var result = await guard.EvaluateAsync(new FeeProfitGuardRequest
        {
            Symbol = TradingSymbol.BNBUSDT,
            TradingMode = TradingMode.Futures,
            RawSignal = TradeSignal.Sell,
            ExecutionIntent = TradeExecutionIntent.OpenShort,
            Side = OrderSide.SELL,
            Quantity = 0.01m,
            EntryPrice = 100m,
            TargetPrice = 99m
        });

        Assert.False(result.IsAllowed);
        Assert.Contains("not supported", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    private static FeeProfitGuard CreateGuard(bool useFeeGuard = true, Position? openPosition = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:UseFeeGuard"] = useFeeGuard ? "true" : "false",
                ["Trading:FeeRatePercent"] = "0.1",
                ["Trading:EstimatedSpreadPercent"] = "0.05",
                ["Trading:MinExpectedMovePercent"] = "0.3",
                ["Trading:MinNetProfitPercent"] = "0.15"
            })
            .Build();

        return new FeeProfitGuard(
            config,
            new FakePositionRepository(openPosition),
            new FakePriceCacheService(100m),
            NullLogger<FeeProfitGuard>.Instance);
    }

    private sealed class FakePositionRepository(Position? openPosition) : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>(openPosition is null ? [] : [openPosition]);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
    }

    private sealed class FakePriceCacheService(decimal price) : IPriceCacheService
    {
        public Task<decimal?> GetCachedPriceAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult<decimal?>(price);
        public Task SetCachedPriceAsync(TradingSymbol symbol, decimal price, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
