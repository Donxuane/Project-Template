using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.Trading;
using Xunit;

namespace TradingBot.Application.Tests;

public class DecisionServiceTradingModeTests
{
    [Fact]
    public async Task SpotSellWithoutOpenLong_IsBlocked()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Sell,
            openPosition: null);

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Hold, result.Action);
        Assert.Equal(TradeExecutionIntent.CloseLong, result.ExecutionIntent);
        Assert.Contains("Spot SELL skipped because no open long position exists.", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotSellWithOpenLong_MapsToCloseLong()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Sell,
            openPosition: new Position
            {
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.05m,
                IsOpen = true
            });

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Sell, result.Action);
        Assert.Equal(TradeExecutionIntent.CloseLong, result.ExecutionIntent);
        Assert.NotNull(result.Candidate);
        Assert.Equal(OrderSide.SELL, result.Candidate!.Side);
        Assert.Equal(0.05m, result.Candidate.Quantity);
        Assert.False(result.Candidate.RequiresReducedPositionSize);
    }

    [Fact]
    public async Task SpotSellWithOpenLong_IgnoresHighVolatilityReduction_AndUsesOpenQuantity()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Sell,
            openPosition: new Position
            {
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.01m,
                IsOpen = true
            },
            requiresReducedPositionSize: true);

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.005m, CancellationToken.None);

        Assert.Equal(TradeSignal.Sell, result.Action);
        Assert.Equal(TradeExecutionIntent.CloseLong, result.ExecutionIntent);
        Assert.NotNull(result.Candidate);
        Assert.Equal(OrderSide.SELL, result.Candidate!.Side);
        Assert.Equal(0.01m, result.Candidate.Quantity);
        Assert.False(result.Candidate.RequiresReducedPositionSize);
    }

    [Fact]
    public async Task SpotBuy_MapsToOpenLong()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Buy,
            openPosition: null);

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Buy, result.Action);
        Assert.Equal(TradeExecutionIntent.OpenLong, result.ExecutionIntent);
        Assert.NotNull(result.Candidate);
        Assert.Equal(OrderSide.BUY, result.Candidate!.Side);
    }

    [Fact]
    public async Task SpotBuy_WithFreshMarketDataAge_IsAllowed()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Buy,
            openPosition: null,
            marketDataAgeSeconds: 2m);

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Buy, result.Action);
        Assert.Equal(TradeExecutionIntent.OpenLong, result.ExecutionIntent);
    }

    [Fact]
    public async Task SpotBuy_WithStaleMarketData_IsBlocked()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Buy,
            openPosition: null,
            marketDataAgeSeconds: 31m,
            maxMarketDataAgeSeconds: 15,
            currentPriceSource: "KlineFallback");

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Hold, result.Action);
        Assert.Equal(TradeExecutionIntent.OpenLong, result.ExecutionIntent);
        Assert.Contains("market data stale", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotCloseLong_IsNotBlockedByStaleMarketData()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Sell,
            openPosition: new Position
            {
                Symbol = TradingSymbol.BNBUSDT,
                Side = OrderSide.BUY,
                Quantity = 0.05m,
                IsOpen = true
            },
            marketDataAgeSeconds: 31m,
            maxMarketDataAgeSeconds: 15,
            currentPriceSource: "KlineFallback");

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Sell, result.Action);
        Assert.Equal(TradeExecutionIntent.CloseLong, result.ExecutionIntent);
    }

    [Fact]
    public async Task SpotBuy_WithHighVolatility_UsesReducedConfiguredQuantity()
    {
        var service = CreateService(
            tradingMode: TradingMode.Spot,
            signal: TradeSignal.Buy,
            openPosition: null,
            requiresReducedPositionSize: true);

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Buy, result.Action);
        Assert.Equal(TradeExecutionIntent.OpenLong, result.ExecutionIntent);
        Assert.NotNull(result.Candidate);
        Assert.Equal(0.005m, result.Candidate!.Quantity);
        Assert.True(result.Candidate.RequiresReducedPositionSize);
    }

    [Fact]
    public async Task FuturesSell_MapsToOpenShort()
    {
        var service = CreateService(
            tradingMode: TradingMode.Futures,
            signal: TradeSignal.Sell,
            openPosition: null);

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Sell, result.Action);
        Assert.Equal(TradingMode.Futures, result.TradingMode);
        Assert.Equal(TradeExecutionIntent.OpenShort, result.ExecutionIntent);
        Assert.NotNull(result.Candidate);
        Assert.Equal(TradeExecutionIntent.OpenShort, result.Candidate!.ExecutionIntent);
    }

    [Fact]
    public async Task FuturesSell_WithHighVolatility_StillUsesReducedConfiguredQuantity()
    {
        var service = CreateService(
            tradingMode: TradingMode.Futures,
            signal: TradeSignal.Sell,
            openPosition: null,
            requiresReducedPositionSize: true);

        var result = await service.DecideAsync(TradingSymbol.BNBUSDT, 0.01m, CancellationToken.None);

        Assert.Equal(TradeSignal.Sell, result.Action);
        Assert.NotNull(result.Candidate);
        Assert.Equal(TradeExecutionIntent.OpenShort, result.Candidate!.ExecutionIntent);
        Assert.Equal(0.005m, result.Candidate.Quantity);
        Assert.True(result.Candidate.RequiresReducedPositionSize);
    }

    private static DecisionService CreateService(
        TradingMode tradingMode,
        TradeSignal signal,
        Position? openPosition,
        bool requiresReducedPositionSize = false,
        decimal marketDataAgeSeconds = 0m,
        int maxMarketDataAgeSeconds = 15,
        string currentPriceSource = "RedisTicker")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:Mode"] = tradingMode.ToString(),
                ["DecisionEngine:MinimumSignalConfidence"] = "0.1",
                ["DecisionEngine:UseAIValidator"] = "false",
                ["DecisionEngine:MaxMarketDataAgeSeconds"] = maxMarketDataAgeSeconds.ToString()
            })
            .Build();

        return new DecisionService(
            config,
            new FakeMarketDataProvider(marketDataAgeSeconds, currentPriceSource),
            new FakeMarketConditionService(requiresReducedPositionSize),
            new FakeStrategy(signal),
            new FakeRiskEvaluator(),
            new FakeAIValidator(),
            new FakePositionRepository(openPosition),
            new PositionManager(),
            NullLogger<DecisionService>.Instance);
    }

    private sealed class FakeMarketDataProvider(decimal marketDataAgeSeconds, string currentPriceSource) : IMarketDataProvider
    {
        public Task<MarketSnapshot?> GetLatestAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<MarketSnapshot?>(new MarketSnapshot
            {
                Symbol = symbol,
                CurrentPrice = 631m,
                CurrentPriceSource = currentPriceSource,
                CurrentPriceAsOfUtc = DateTime.UtcNow.AddSeconds(-(double)marketDataAgeSeconds),
                MarketDataAgeSeconds = marketDataAgeSeconds,
                ClosePrices = [628m, 629m, 630m, 631m],
                HighPrices = [629m, 630m, 631m, 632m],
                LowPrices = [627m, 628m, 629m, 630m],
                Volumes = [10m, 12m, 11m, 13m]
            });
    }

    private sealed class FakeMarketConditionService(bool requiresReducedPositionSize) : IMarketConditionService
    {
        public int RequiredPeriods => 1;

        public MarketConditionResult Evaluate(MarketSnapshot snapshot)
            => new()
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                RequiresReducedPositionSize = requiresReducedPositionSize,
                Reason = "ok"
            };
    }

    private sealed class FakeStrategy(TradeSignal signal) : IStrategy
    {
        public int RequiredPeriods => 1;

        public Task<StrategySignalResult> GenerateSignalAsync(
            MarketSnapshot marketData,
            CancellationToken cancellationToken = default,
            bool allowStateMutation = true)
            => Task.FromResult(new StrategySignalResult
            {
                Signal = signal,
                Confidence = 0.9m,
                Reason = "test-signal"
            });
    }

    private sealed class FakeRiskEvaluator : IRiskEvaluator
    {
        public Task<RiskEvaluationResult> EvaluateAsync(TradeCandidate candidate, CancellationToken cancellationToken = default)
            => Task.FromResult(new RiskEvaluationResult
            {
                IsAllowed = true,
                Reason = "ok"
            });
    }

    private sealed class FakeAIValidator : IAIValidator
    {
        public Task<bool> ValidateAsync(TradeCandidate candidate, MarketSnapshot marketData, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FakePositionRepository(Position? openPosition) : IPositionRepository
    {
        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default) => Task.FromResult(openPosition);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>(openPosition is null ? [] : [openPosition]);
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
