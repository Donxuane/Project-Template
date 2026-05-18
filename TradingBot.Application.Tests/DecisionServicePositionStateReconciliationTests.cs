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

public class DecisionServicePositionStateReconciliationTests
{
    [Fact]
    public async Task StaleInMemoryPosition_WithNoDbOpenPosition_ReconcilesToFlatBeforeStrategyEvaluation()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.SOLUSDT, PositionType.Long, 100m, TrendState.Bullish, DateTime.UtcNow.AddMinutes(-5));

        var strategy = new StateAwareStrategy(positionManager, confidence: 0.9m);
        var service = CreateService(
            strategy,
            new FakePositionRepository(),
            positionManager,
            minSignalConfidence: 0.7m);

        var result = await service.DecideAsync(TradingSymbol.SOLUSDT, 0.1m, CancellationToken.None);

        Assert.Equal(TradeSignal.Buy, result.Action);
        Assert.False(strategy.LastObservedInPosition);
    }

    [Fact]
    public async Task FlatInMemory_WithDbOpenPosition_ReconcilesToLongBeforeStrategyEvaluation()
    {
        var positionManager = new PositionManager();
        var strategy = new StateAwareStrategy(positionManager, confidence: 0.9m);
        var service = CreateService(
            strategy,
            new FakePositionRepository(new Dictionary<TradingSymbol, Position>
            {
                [TradingSymbol.ETHUSDT] = CreateOpenLongPosition(TradingSymbol.ETHUSDT)
            }),
            positionManager,
            minSignalConfidence: 0.7m);

        var result = await service.DecideAsync(TradingSymbol.ETHUSDT, 0.1m, CancellationToken.None);
        var state = positionManager.GetState(TradingSymbol.ETHUSDT);

        Assert.Equal(TradeSignal.Hold, result.Action);
        Assert.True(strategy.LastObservedInPosition);
        Assert.True(state.IsInPosition);
        Assert.Equal(PositionType.Long, state.PositionType);
    }

    [Fact]
    public async Task BlockedRawEntry_DoesNotLeaveStrategyStuckInStaleHoldOnNextCycle()
    {
        var positionManager = new PositionManager();
        var strategy = new EnteringStatefulStrategy(positionManager, [0.5m, 0.9m]);
        var service = CreateService(
            strategy,
            new FakePositionRepository(),
            positionManager,
            minSignalConfidence: 0.7m);

        var first = await service.DecideAsync(TradingSymbol.SOLUSDT, 0.1m, CancellationToken.None);
        var second = await service.DecideAsync(TradingSymbol.SOLUSDT, 0.1m, CancellationToken.None);

        Assert.Equal(TradeSignal.Hold, first.Action);
        Assert.Equal(TradeSignal.Buy, second.Action);
        Assert.False(strategy.ObservedStatesBeforeSignal[1]);
    }

    [Fact]
    public async Task ExternalClose_ResultsInFlatStateOnNextEvaluation()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.ETHUSDT, PositionType.Long, 2200m, TrendState.Bullish, DateTime.UtcNow.AddMinutes(-10));

        var strategy = new StateAwareStrategy(positionManager, confidence: 0.9m);
        var service = CreateService(
            strategy,
            new FakePositionRepository(),
            positionManager,
            minSignalConfidence: 0.7m);

        var result = await service.DecideAsync(TradingSymbol.ETHUSDT, 0.1m, CancellationToken.None);

        Assert.Equal(TradeSignal.Buy, result.Action);
        Assert.False(strategy.LastObservedInPosition);
    }

    [Fact]
    public async Task SpotStaleShortState_IsReconciledToLongWhenDbOpenPositionExists()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.ETHUSDT, PositionType.Short, 2200m, TrendState.Bearish, DateTime.UtcNow.AddMinutes(-10));

        var strategy = new StateAwareStrategy(positionManager, confidence: 0.9m);
        var service = CreateService(
            strategy,
            new FakePositionRepository(new Dictionary<TradingSymbol, Position>
            {
                [TradingSymbol.ETHUSDT] = CreateOpenLongPosition(TradingSymbol.ETHUSDT)
            }),
            positionManager,
            minSignalConfidence: 0.7m);

        await service.DecideAsync(TradingSymbol.ETHUSDT, 0.1m, CancellationToken.None);
        var state = positionManager.GetState(TradingSymbol.ETHUSDT);

        Assert.True(state.IsInPosition);
        Assert.Equal(PositionType.Long, state.PositionType);
    }

    [Fact]
    public async Task DbOpenPositionForEth_DoesNotContaminateSolState()
    {
        var positionManager = new PositionManager();
        var strategy = new StateAwareStrategy(positionManager, confidence: 0.9m);
        var service = CreateService(
            strategy,
            new FakePositionRepository(new Dictionary<TradingSymbol, Position>
            {
                [TradingSymbol.ETHUSDT] = CreateOpenLongPosition(TradingSymbol.ETHUSDT)
            }),
            positionManager,
            minSignalConfidence: 0.7m);

        await service.DecideAsync(TradingSymbol.SOLUSDT, 0.1m, CancellationToken.None);

        Assert.False(strategy.LastObservedInPosition);
        Assert.False(positionManager.GetState(TradingSymbol.SOLUSDT).IsInPosition);
    }

    private static DecisionService CreateService(
        IStrategy strategy,
        IPositionRepository positionRepository,
        IPositionManager positionManager,
        decimal minSignalConfidence,
        TradingMode tradingMode = TradingMode.Spot)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trading:Mode"] = tradingMode.ToString(),
                ["DecisionEngine:MinimumSignalConfidence"] = minSignalConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["DecisionEngine:UseAIValidator"] = "false"
            })
            .Build();

        return new DecisionService(
            config,
            new FakeMarketDataProvider(),
            new FakeMarketConditionService(),
            strategy,
            new FakeRiskEvaluator(),
            new FakeAIValidator(),
            positionRepository,
            positionManager,
            NullLogger<DecisionService>.Instance);
    }

    private static Position CreateOpenLongPosition(TradingSymbol symbol)
    {
        return new Position
        {
            Id = 1,
            Symbol = symbol,
            Side = OrderSide.BUY,
            Quantity = 0.2m,
            AveragePrice = 2200m,
            OpenedAt = DateTime.UtcNow.AddMinutes(-5),
            IsOpen = true
        };
    }

    private sealed class FakeMarketDataProvider : IMarketDataProvider
    {
        public Task<MarketSnapshot?> GetLatestAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult<MarketSnapshot?>(new MarketSnapshot
            {
                Symbol = symbol,
                CurrentPrice = 100m,
                ClosePrices = [98m, 99m, 100m, 101m, 102m],
                HighPrices = [99m, 100m, 101m, 102m, 103m],
                LowPrices = [97m, 98m, 99m, 100m, 101m],
                Volumes = [10m, 12m, 11m, 13m, 14m]
            });
    }

    private sealed class FakeMarketConditionService : IMarketConditionService
    {
        public int RequiredPeriods => 1;

        public MarketConditionResult Evaluate(MarketSnapshot snapshot) =>
            new()
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok"
            };
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

    private sealed class FakePositionRepository : IPositionRepository
    {
        private readonly IReadOnlyDictionary<TradingSymbol, Position> _openPositions;

        public FakePositionRepository(IReadOnlyDictionary<TradingSymbol, Position>? openPositions = null)
        {
            _openPositions = openPositions ?? new Dictionary<TradingSymbol, Position>();
        }

        public Task<long> UpsertAsync(Position position, CancellationToken cancellationToken = default) => Task.FromResult(position.Id);
        public Task<Position?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
            => Task.FromResult(_openPositions.Values.FirstOrDefault(x => x.Id == id));
        public Task<Position?> GetOpenPositionAsync(TradingSymbol symbol, CancellationToken cancellationToken = default)
            => Task.FromResult(_openPositions.TryGetValue(symbol, out var position) ? position : null);
        public Task<IReadOnlyList<Position>> GetOpenPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Position>>(_openPositions.Values.ToList());
        public Task<IReadOnlyList<Position>> GetClosedPositionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Position>>([]);
        public Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task ClearPositionClosingAsync(long positionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StateAwareStrategy(IPositionManager positionManager, decimal confidence) : IStrategy
    {
        public int RequiredPeriods => 1;
        public bool LastObservedInPosition { get; private set; }

        public Task<StrategySignalResult> GenerateSignalAsync(MarketSnapshot marketData, CancellationToken cancellationToken = default)
        {
            var state = positionManager.GetState(marketData.Symbol);
            LastObservedInPosition = state.IsInPosition;

            return Task.FromResult(state.IsInPosition
                ? new StrategySignalResult { Signal = TradeSignal.Hold, Confidence = confidence, Reason = "Holding long position - trend still bullish." }
                : new StrategySignalResult { Signal = TradeSignal.Buy, Confidence = confidence, Reason = "Entry signal - bullish trend confirmed." });
        }
    }

    private sealed class EnteringStatefulStrategy(IPositionManager positionManager, IReadOnlyList<decimal> confidences) : IStrategy
    {
        private readonly Queue<decimal> _confidences = new(confidences);

        public int RequiredPeriods => 1;
        public List<bool> ObservedStatesBeforeSignal { get; } = [];

        public Task<StrategySignalResult> GenerateSignalAsync(MarketSnapshot marketData, CancellationToken cancellationToken = default)
        {
            var state = positionManager.GetState(marketData.Symbol);
            ObservedStatesBeforeSignal.Add(state.IsInPosition);

            if (state.IsInPosition)
            {
                return Task.FromResult(new StrategySignalResult
                {
                    Signal = TradeSignal.Hold,
                    Confidence = 0.9m,
                    Reason = "Holding long position - trend still bullish."
                });
            }

            var confidence = _confidences.Count > 0 ? _confidences.Dequeue() : 0.9m;
            positionManager.Enter(marketData.Symbol, PositionType.Long, marketData.CurrentPrice, TrendState.Bullish, DateTime.UtcNow);
            return Task.FromResult(new StrategySignalResult
            {
                Signal = TradeSignal.Buy,
                Confidence = confidence,
                Reason = "Entry signal - bullish trend confirmed."
            });
        }
    }
}
