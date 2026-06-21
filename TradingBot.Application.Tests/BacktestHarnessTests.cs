using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using TradingBot.Backtest;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;
using Xunit;

namespace TradingBot.Application.Tests;

public class BacktestHarnessTests
{
    [Fact]
    public void ExecutionSimulator_OpensAndClosesSingleLongPosition()
    {
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(0m, 0m, 0m));
        var trades = new List<SimulatedTrade>();
        var buySignal = new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "buy" };
        var sellSignal = new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "sell" };

        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, buySignal, Snapshot(TradingSymbol.BNBUSDT, 100m, DateTime.UtcNow), "p", "ETH+BNB", trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, sellSignal, Snapshot(TradingSymbol.BNBUSDT, 105m, DateTime.UtcNow.AddMinutes(1)), "p", "ETH+BNB", trades);

        Assert.Single(trades);
        Assert.Equal(5m, trades[0].GrossPnlQuote);
    }

    [Fact]
    public void ExecutionSimulator_NetPnl_AppliesFeeAndSpreadAssumptions()
    {
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(1m, 2m, 0m));
        var trades = new List<SimulatedTrade>();

        simulator.OnSignal("1m", TradingSymbol.ETHUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "buy" }, Snapshot(TradingSymbol.ETHUSDT, 100m, DateTime.UtcNow), "p", "ETH+BNB", trades);
        simulator.OnSignal("1m", TradingSymbol.ETHUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "sell" }, Snapshot(TradingSymbol.ETHUSDT, 110m, DateTime.UtcNow.AddMinutes(1)), "p", "ETH+BNB", trades);

        Assert.Single(trades);
        Assert.Equal(10m, trades[0].GrossPnlQuote);
        Assert.Equal(4.2m, decimal.Round(trades[0].FeeAndSpreadEstimateQuote, 4));
        Assert.Equal(5.8m, decimal.Round(trades[0].NetPnlQuote, 4));
    }

    [Fact]
    public void SummaryAggregator_ComputesMaxConsecutiveLosses_AndSummaryFields()
    {
        var now = DateTime.UtcNow;
        var trades = new[]
        {
            Trade(now, grossPnl: -1m, netPnl: -1m),
            Trade(now.AddMinutes(1), grossPnl: -2m, netPnl: -2m),
            Trade(now.AddMinutes(2), grossPnl: 1m, netPnl: 1m),
            Trade(now.AddMinutes(3), grossPnl: -1m, netPnl: -1m),
            Trade(now.AddMinutes(4), grossPnl: -1m, netPnl: -1m),
            Trade(now.AddMinutes(5), grossPnl: -1m, netPnl: -1m)
        };

        var maxLosses = ReplaySummaryAggregator.CalculateMaxConsecutiveLosses(trades);
        var summary = ReplaySummaryAggregator.BuildSummary(
            "1m",
            "profile",
            "ETH+BNB",
            trades,
            new ProfileSignalStats(),
            new ProfileRuntimeSnapshot(
                true,
                true,
                1.00m,
                true,
                0.30m,
                false,
                1.20m,
                null,
                false,
                false,
                "PreviousCandleHigh",
                false,
                false,
                0.35m,
                0.12m,
                1.25m,
                true,
                55m,
                "OppositeSignalOnly",
                null,
                10,
                0.0002m,
                1,
                0.0002m,
                true,
                false,
                "Combined"));

        Assert.Equal(3, maxLosses);
        Assert.Equal(6, summary.TradesCount);
        Assert.Equal(1, summary.Wins);
        Assert.Equal(5, summary.Losses);
        Assert.Equal(3, summary.MaxConsecutiveLosses);
    }

    [Fact]
    public void EntryGuard_Blocks_WhenExpectedMoveBelowMinimum()
    {
        var guard = CreateGuard(minExpectedMovePercent: 0.20m, minNetProfitPercent: 0.05m);
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.9m,
            ExpectedMovePercent = 0.10m,
            ExpectedTargetPrice = 102m,
            ExpectedTargetSource = "src"
        };

        var result = guard.Evaluate(TradingSymbol.ETHUSDT, signal, Snapshot(TradingSymbol.ETHUSDT, 100m, DateTime.UtcNow), hasOpenPositionForSymbol: false);

        Assert.False(result.IsAllowed);
        Assert.Equal(BacktestEntryGuard.ExpectedMoveBelowMinimum, result.Reason);
    }

    [Fact]
    public void EntryGuard_Blocks_WhenExpectedNetMoveBelowMinimum()
    {
        var guard = CreateGuard(
            minExpectedMovePercent: 0.01m,
            minNetProfitPercent: 0.10m,
            feeRatePercent: 0.1m,
            spreadPercent: 0.05m,
            slippagePercent: 0m);
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.9m,
            ExpectedMovePercent = 0.20m,
            ExpectedTargetPrice = 102m,
            ExpectedTargetSource = "src"
        };

        var result = guard.Evaluate(TradingSymbol.ETHUSDT, signal, Snapshot(TradingSymbol.ETHUSDT, 100m, DateTime.UtcNow), hasOpenPositionForSymbol: false);

        Assert.False(result.IsAllowed);
        Assert.Equal(BacktestEntryGuard.ExpectedNetMoveBelowMinimum, result.Reason);
    }

    [Fact]
    public void EntryGuard_Blocks_WhenConfidenceBelowThreshold()
    {
        var guard = CreateGuard(minExecutionConfidence: 0.70m);
        var signal = new StrategySignalResult
        {
            StrategyName = "MovingAverageTrendStrategy",
            Signal = TradeSignal.Buy,
            Confidence = 0.60m,
            ExpectedMovePercent = 1.0m,
            ExpectedTargetPrice = 102m,
            ExpectedTargetSource = "src"
        };

        var result = guard.Evaluate(TradingSymbol.ETHUSDT, signal, Snapshot(TradingSymbol.ETHUSDT, 100m, DateTime.UtcNow), hasOpenPositionForSymbol: false);

        Assert.False(result.IsAllowed);
        Assert.Equal(BacktestEntryGuard.ConfidenceBelowThreshold, result.Reason);
    }

    [Fact]
    public void EntryGuard_Blocks_WhenExpectedTargetMissing_AndRequired()
    {
        var guard = CreateGuard(requireExpectedTarget: true);
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.9m,
            ExpectedMovePercent = null,
            ExpectedTargetPrice = null,
            ExpectedTargetSource = null
        };

        var result = guard.Evaluate(TradingSymbol.ETHUSDT, signal, Snapshot(TradingSymbol.ETHUSDT, 100m, DateTime.UtcNow), hasOpenPositionForSymbol: false);

        Assert.False(result.IsAllowed);
        Assert.Equal(BacktestEntryGuard.MissingStrategyExpectedTarget, result.Reason);
    }

    [Fact]
    public void EntryGuard_Allows_ValidBuy_AndSimulatorOpensPosition()
    {
        var guard = CreateGuard(minExpectedMovePercent: 0.10m, minNetProfitPercent: 0.05m);
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.9m,
            ExpectedMovePercent = 1.20m,
            ExpectedTargetPrice = 102m,
            ExpectedTargetSource = "src"
        };
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(0.1m, 0.05m, 0m));
        var trades = new List<SimulatedTrade>();
        var snapshot = Snapshot(TradingSymbol.ETHUSDT, 100m, DateTime.UtcNow);

        var decision = guard.Evaluate(TradingSymbol.ETHUSDT, signal, snapshot, hasOpenPositionForSymbol: false);
        Assert.True(decision.IsAllowed);

        simulator.OnSignal(
            "1m",
            TradingSymbol.ETHUSDT,
            1m,
            signal,
            snapshot,
            "profile",
            "ETH",
            trades,
            wasGuarded: true,
            estimatedRoundTripCostPercent: decision.EstimatedRoundTripCostPercent,
            estimatedNetMovePercent: decision.EstimatedNetMovePercent);

        Assert.True(simulator.HasOpenPosition(TradingSymbol.ETHUSDT));
        Assert.Empty(trades);
    }

    [Fact]
    public void PullbackFollowThroughV2Filter_Rejected_WhenReclaimNotConfirmed()
    {
        var filter = CreatePullbackV2Filter(enabled: true);
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Reason = "Entry signal - pullback continuation override confirmed.",
            EntryNearRecentHigh = true,
            ExpectedTargetPrice = 101m
        };

        var decision = filter.Evaluate(
            TradingSymbol.ETHUSDT,
            signal,
            SnapshotWithSeries(
                TradingSymbol.ETHUSDT,
                DateTime.UtcNow,
                currentPrice: 100m,
                closes: [99.8m, 99.9m, 99.7m],
                highs: [100.2m, 100.1m, 99.95m]),
            hasOpenPosition: false);

        Assert.True(decision.IsRejected);
        Assert.Equal("Strategy:PullbackReclaimNotConfirmed", decision.RejectionReason);
    }

    [Fact]
    public void PullbackFollowThroughV2Filter_Executes_WhenFollowThroughConfirmedOnNextCandle()
    {
        var filter = CreatePullbackV2Filter(enabled: true);
        var t0 = DateTime.UtcNow;
        var setupSignal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Reason = "Entry signal - pullback continuation override confirmed.",
            EntryNearRecentHigh = true,
            ExpectedTargetPrice = 101m,
            ExpectedMovePercent = 0.8m
        };

        var reclaimSnapshot = SnapshotWithSeries(TradingSymbol.ETHUSDT, t0, currentPrice: 100m, closes: [99.7m, 99.8m, 99.9m, 100.2m, 100.2m], highs: [99.9m, 100.0m, 100.1m, 100.0m, 100.2m]);
        var pending = filter.Evaluate(TradingSymbol.ETHUSDT, setupSignal, reclaimSnapshot, hasOpenPosition: false);
        Assert.True(pending.IsPending);

        var nextSnapshot = SnapshotWithSeries(TradingSymbol.ETHUSDT, t0.AddMinutes(1), currentPrice: 100.1m, closes: [99.7m, 99.8m, 99.9m, 100.2m, 100.25m], highs: [99.9m, 100.0m, 100.1m, 100.0m, 100.3m]);
        var decision = filter.Evaluate(TradingSymbol.ETHUSDT, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "hold" }, nextSnapshot, hasOpenPosition: false);

        Assert.True(decision.IsExecute);
        Assert.NotNull(decision.SignalToExecute);
        Assert.True(decision.Diagnostics?.PullbackFollowThroughConfirmed);
        Assert.Equal(1, decision.Diagnostics?.CandlesWaitedAfterReclaim);
    }

    [Fact]
    public void PullbackFollowThroughV3Filter_Rejects_WhenResidualRewardRiskTooLow()
    {
        var filter = CreatePullbackV2Filter(
            enabled: true,
            enableV3: true,
            minResidualExpectedMove: 0.01m,
            minResidualNetMove: 0.01m,
            minResidualRewardRisk: 3.0m);
        var t0 = DateTime.UtcNow;
        var setupSignal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Reason = "Entry signal - pullback continuation override confirmed.",
            EntryNearRecentHigh = true,
            DistanceToInvalidationPercent = 0.2m,
            ExpectedTargetPrice = 100.8m,
            ExpectedMovePercent = 0.6m
        };

        _ = filter.Evaluate(TradingSymbol.ETHUSDT, setupSignal, SnapshotWithSeries(TradingSymbol.ETHUSDT, t0, 100m, [99.7m, 99.8m, 99.9m, 100.2m, 100.2m], [99.9m, 100.0m, 100.1m, 100.0m, 100.2m]), hasOpenPosition: false);
        var decision = filter.Evaluate(TradingSymbol.ETHUSDT, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "hold" }, SnapshotWithSeries(TradingSymbol.ETHUSDT, t0.AddMinutes(1), 100.30m, [99.7m, 99.8m, 99.9m, 100.2m, 100.30m], [99.9m, 100.0m, 100.1m, 100.0m, 100.35m]), hasOpenPosition: false);

        Assert.True(decision.IsRejected);
        Assert.Equal("Strategy:V3ResidualRewardRiskBelowMinimum", decision.RejectionReason);
    }

    [Fact]
    public void PullbackFollowThroughV3Filter_Allows_WhenResidualNetAndRewardRiskPass()
    {
        var filter = CreatePullbackV2Filter(enabled: true, enableV3: true);
        var t0 = DateTime.UtcNow;
        var setupSignal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Reason = "Entry signal - pullback continuation override confirmed.",
            EntryNearRecentHigh = true,
            DistanceToInvalidationPercent = 0.2m,
            ExpectedTargetPrice = 101.0m,
            ExpectedMovePercent = 1.0m
        };

        _ = filter.Evaluate(TradingSymbol.ETHUSDT, setupSignal, SnapshotWithSeries(TradingSymbol.ETHUSDT, t0, 100m, [99.7m, 99.8m, 99.9m, 100.2m, 100.2m], [99.9m, 100.0m, 100.1m, 100.0m, 100.2m]), hasOpenPosition: false);
        var decision = filter.Evaluate(TradingSymbol.ETHUSDT, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "hold" }, SnapshotWithSeries(TradingSymbol.ETHUSDT, t0.AddMinutes(1), 100.2m, [99.7m, 99.8m, 99.9m, 100.2m, 100.3m], [99.9m, 100.0m, 100.1m, 100.0m, 100.35m]), hasOpenPosition: false);

        Assert.True(decision.IsExecute);
        Assert.NotNull(decision.Diagnostics?.ResidualEstimatedNetMovePercent);
        Assert.True(decision.Diagnostics!.ResidualEstimatedNetMovePercent > 0m);
        Assert.NotNull(decision.Diagnostics.ResidualRewardRisk);
    }

    [Fact]
    public void V2DelayedExecution_PopulatesEstimatedCostFields()
    {
        var guard = CreateGuard(minExpectedMovePercent: 0.1m, minNetProfitPercent: 0.05m);
        var filter = CreatePullbackV2Filter(enabled: true);
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(0.1m, 0.05m, 0m));
        var trades = new List<SimulatedTrade>();
        var t0 = DateTime.UtcNow;
        var setupSignal = new StrategySignalResult
        {
            StrategyName = "MovingAverageTrendStrategy",
            Signal = TradeSignal.Buy,
            Reason = "Entry signal - pullback continuation override confirmed.",
            Confidence = 0.9m,
            EntryNearRecentHigh = true,
            ExpectedTargetPrice = 101m,
            ExpectedMovePercent = 0.8m,
            ExpectedTargetSource = "test",
            DistanceToInvalidationPercent = 0.2m
        };

        _ = filter.Evaluate(TradingSymbol.ETHUSDT, setupSignal, SnapshotWithSeries(TradingSymbol.ETHUSDT, t0, 100m, [99.7m, 99.8m, 99.9m, 100.2m, 100.2m], [99.9m, 100.0m, 100.1m, 100.0m, 100.2m]), hasOpenPosition: false);
        var execute = filter.Evaluate(TradingSymbol.ETHUSDT, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "hold" }, SnapshotWithSeries(TradingSymbol.ETHUSDT, t0.AddMinutes(1), 100.1m, [99.7m, 99.8m, 99.9m, 100.2m, 100.25m], [99.9m, 100.0m, 100.1m, 100.0m, 100.3m]), hasOpenPosition: false);
        Assert.True(execute.IsExecute);

        var guardDecision = guard.Evaluate(TradingSymbol.ETHUSDT, execute.SignalToExecute!, Snapshot(TradingSymbol.ETHUSDT, 100.1m, t0.AddMinutes(1)), hasOpenPositionForSymbol: false);
        Assert.True(guardDecision.IsAllowed);

        simulator.OnSignal("1m", TradingSymbol.ETHUSDT, 1m, execute.SignalToExecute!, Snapshot(TradingSymbol.ETHUSDT, 100.1m, t0.AddMinutes(1)), "p", "ETH", trades, true, guardDecision.EstimatedRoundTripCostPercent, guardDecision.EstimatedNetMovePercent, execute.Diagnostics);
        simulator.OnSignal("1m", TradingSymbol.ETHUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "sell" }, SnapshotWithRange(TradingSymbol.ETHUSDT, 100.0m, 100.2m, 99.8m, t0.AddMinutes(2)), "p", "ETH", trades);

        var trade = Assert.Single(trades);
        Assert.NotNull(trade.EstimatedRoundTripCostPercent);
        Assert.NotNull(trade.EstimatedNetMovePercent);
    }

    [Fact]
    public void SummaryAggregator_IncludesBlockedReasonCounts_AndGrossNetWinSplit()
    {
        var stats = new ProfileSignalStats
        {
            RawBuySignals = 5,
            ExecutedBuySignals = 2
        };
        stats.IncrementBlocked(BacktestEntryGuard.ExpectedMoveBelowMinimum);
        stats.IncrementBlocked(BacktestEntryGuard.ExpectedMoveBelowMinimum);
        stats.IncrementBlocked(BacktestEntryGuard.ConfidenceBelowThreshold);

        var now = DateTime.UtcNow;
        var trades = new[]
        {
            Trade(now, grossPnl: 1m, netPnl: -0.1m),
            Trade(now.AddMinutes(1), grossPnl: 2m, netPnl: 1m),
            Trade(now.AddMinutes(2), grossPnl: -1m, netPnl: -1.1m)
        };

        var summary = ReplaySummaryAggregator.BuildSummary(
            "1m",
            "p",
            "ETH",
            trades,
            stats,
            new ProfileRuntimeSnapshot(
                false,
                true,
                1.20m,
                true,
                0.30m,
                true,
                1.50m,
                0.0012m,
                true,
                true,
                "PreviousCandleHigh",
                true,
                false,
                0.35m,
                0.12m,
                1.25m,
                true,
                55m,
                "OppositeSignalOnly",
                null,
                24,
                0.0020m,
                2,
                0.0008m,
                true,
                false,
                "Combined"));

        Assert.Equal(5, summary.RawBuySignals);
        Assert.Equal(2, summary.ExecutedBuySignals);
        Assert.Equal(3, summary.BlockedBuySignals);
        Assert.Equal(2, summary.GrossWinningTrades);
        Assert.Equal(66.666666666666666666666666667m, summary.GrossWinRatePercent);
        Assert.Equal(1, summary.NetWinningTrades);
        Assert.Equal(33.333333333333333333333333333m, summary.NetWinRatePercent);
        Assert.Equal(2, summary.BlockedByReason[BacktestEntryGuard.ExpectedMoveBelowMinimum]);
        Assert.Equal(1, summary.BlockedByReason[BacktestEntryGuard.ConfidenceBelowThreshold]);
        Assert.False(summary.EnableLowVolatilityBreakoutEntry);
        Assert.Equal(24, summary.BreakoutLookbackCandles);
        Assert.Equal(0.0020m, summary.BreakoutBufferPercent);
        Assert.Equal(2, summary.BreakoutConfirmationCandles);
        Assert.Equal(0.0008m, summary.MinBreakoutSlopePercent);
        Assert.True(summary.UseConfirmedClosedCandlesForLowVolBreakout);
        Assert.True(summary.EnableNormalTrendPullbackContinuationOverride);
        Assert.Equal(1.20m, summary.NormalTrendPullbackMinExpectedRewardRisk);
        Assert.True(summary.EnableNormalTrendMinDistanceToInvalidationFilter);
        Assert.Equal(0.30m, summary.NormalTrendMinDistanceToInvalidationPercent);
        Assert.True(summary.EnableNormalTrendNearRecentHighRejection);
        Assert.Equal(1.50m, summary.NormalTrendNearRecentHighRequiresRewardRisk);
        Assert.Equal(0.0012m, summary.NormalTrendNearRecentHighRequiresTrendStrengthPercent);
        Assert.True(summary.EnablePullbackOverrideHighVolatilityBlock);
        Assert.Equal(0, summary.StrategyRejectedBuySignals);
        Assert.Empty(summary.StrategyRejectedByReason);
        Assert.True(summary.EnableNormalTrendPullbackReclaimConfirmationFilter);
        Assert.Equal("PreviousCandleHigh", summary.NormalTrendPullbackReclaimMode);
        Assert.True(summary.EnablePullbackFollowThroughV2);
        Assert.False(summary.EnablePullbackFollowThroughV3);
        Assert.Equal(0.35m, summary.PullbackV3MinResidualExpectedMovePercent);
        Assert.Equal(0.12m, summary.PullbackV3MinResidualNetMovePercent);
        Assert.Equal(1.25m, summary.PullbackV3MinResidualRewardRisk);
        Assert.True(summary.PullbackV3RejectIfTargetAlreadyMostlyConsumed);
        Assert.Equal(55m, summary.PullbackV3MaxTargetConsumedPercent);
    }

    [Fact]
    public void ExecutionSimulator_TracksMfeMae_FromPostEntryCandlesOnly()
    {
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(0m, 0m, 0m));
        var trades = new List<SimulatedTrade>();
        var t0 = DateTime.UtcNow;

        simulator.OnSignal(
            "1m",
            TradingSymbol.ETHUSDT,
            1m,
            new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "buy", ExpectedTargetPrice = 120m, VolatilityRegime = "High" },
            SnapshotWithRange(TradingSymbol.ETHUSDT, 100m, 130m, 80m, t0),
            "p",
            "ETH",
            trades);

        simulator.OnSignal("1m", TradingSymbol.ETHUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "h1" }, SnapshotWithRange(TradingSymbol.ETHUSDT, 101m, 108m, 97m, t0.AddMinutes(1)), "p", "ETH", trades);
        simulator.OnSignal("1m", TradingSymbol.ETHUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "h2" }, SnapshotWithRange(TradingSymbol.ETHUSDT, 102m, 110m, 95m, t0.AddMinutes(2)), "p", "ETH", trades);
        simulator.OnSignal("1m", TradingSymbol.ETHUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "sell" }, SnapshotWithRange(TradingSymbol.ETHUSDT, 102m, 103m, 99m, t0.AddMinutes(3)), "p", "ETH", trades);

        var trade = Assert.Single(trades);
        Assert.Equal("OppositeSignal", trade.ExitReason);
        Assert.Equal(110m, trade.MaxFavorablePrice);
        Assert.Equal(95m, trade.MaxAdversePrice);
        Assert.Equal(10m, trade.MfePercent);
        Assert.Equal(-5m, trade.MaePercent);
        Assert.Equal("High", trade.VolatilityRegime);
    }

    [Fact]
    public void ExecutionSimulator_ExpectedTargetTouch_True_SetsFirstTouchAndCounterfactual()
    {
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(0m, 0m, 0m));
        var trades = new List<SimulatedTrade>();
        var t0 = DateTime.UtcNow;
        var touchTime = t0.AddMinutes(2);

        simulator.OnSignal(
            "1m",
            TradingSymbol.BNBUSDT,
            1m,
            new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "buy", ExpectedTargetPrice = 109m },
            SnapshotWithRange(TradingSymbol.BNBUSDT, 100m, 101m, 99m, t0),
            "p",
            "BNB",
            trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "h1" }, SnapshotWithRange(TradingSymbol.BNBUSDT, 101m, 108m, 100m, t0.AddMinutes(1)), "p", "BNB", trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "h2" }, SnapshotWithRange(TradingSymbol.BNBUSDT, 102m, 109.5m, 101m, touchTime), "p", "BNB", trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "sell" }, SnapshotWithRange(TradingSymbol.BNBUSDT, 101m, 102m, 100m, t0.AddMinutes(3)), "p", "BNB", trades);

        var trade = Assert.Single(trades);
        Assert.True(trade.TouchedExpectedTarget);
        Assert.Equal(touchTime, trade.FirstExpectedTargetTouchTimeUtc);
        Assert.NotNull(trade.CounterfactualExitAtExpectedTargetNetPnlQuote);
        Assert.NotNull(trade.CounterfactualDeltaVsActualNetPnlQuote);
        Assert.Equal(trade.CounterfactualExitAtExpectedTargetNetPnlQuote - trade.NetPnlQuote, trade.CounterfactualDeltaVsActualNetPnlQuote);
        Assert.Equal("OppositeSignal", trade.ExitReason);
    }

    [Fact]
    public void ExecutionSimulator_ProfitCaptureCounterfactual_DetectsNearTargetBeforeOppositeExit()
    {
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(0m, 0m, 0m));
        var trades = new List<SimulatedTrade>();
        var t0 = DateTime.UtcNow;

        simulator.OnSignal(
            "1m",
            TradingSymbol.BNBUSDT,
            1m,
            new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "buy", ExpectedTargetPrice = 110m },
            SnapshotWithRange(TradingSymbol.BNBUSDT, 100m, 100m, 99m, t0),
            "p",
            "BNB",
            trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "h1" }, SnapshotWithRange(TradingSymbol.BNBUSDT, 101m, 109.8m, 100m, t0.AddMinutes(1)), "p", "BNB", trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "sell" }, SnapshotWithRange(TradingSymbol.BNBUSDT, 100.5m, 101m, 100m, t0.AddMinutes(2)), "p", "BNB", trades);

        var trade = Assert.Single(trades);
        Assert.True(trade.ProfitCapture90Touched);
        Assert.True(trade.ProfitCapture95Touched);
        Assert.True(trade.ProfitCapture98Touched);
        Assert.NotNull(trade.ProfitCapture98CounterfactualNetPnlQuote);
        Assert.NotNull(trade.ProfitCaptureDeltaVsOppositeSignalExitQuote);
    }

    [Fact]
    public void ExecutionSimulator_ExpectedTargetTouch_False_KeepsCounterfactualNull()
    {
        var simulator = new ExecutionSimulator(new ExecutionCostSettings(0m, 0m, 0m));
        var trades = new List<SimulatedTrade>();
        var t0 = DateTime.UtcNow;

        simulator.OnSignal(
            "1m",
            TradingSymbol.BNBUSDT,
            1m,
            new StrategySignalResult { Signal = TradeSignal.Buy, Reason = "buy", ExpectedTargetPrice = 120m },
            SnapshotWithRange(TradingSymbol.BNBUSDT, 100m, 101m, 99m, t0),
            "p",
            "BNB",
            trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Hold, Reason = "h1" }, SnapshotWithRange(TradingSymbol.BNBUSDT, 101m, 108m, 100m, t0.AddMinutes(1)), "p", "BNB", trades);
        simulator.OnSignal("1m", TradingSymbol.BNBUSDT, 1m, new StrategySignalResult { Signal = TradeSignal.Sell, Reason = "sell" }, SnapshotWithRange(TradingSymbol.BNBUSDT, 102m, 110m, 101m, t0.AddMinutes(2)), "p", "BNB", trades);

        var trade = Assert.Single(trades);
        Assert.False(trade.TouchedExpectedTarget);
        Assert.Null(trade.FirstExpectedTargetTouchTimeUtc);
        Assert.Null(trade.CounterfactualExitAtExpectedTargetNetPnlQuote);
        Assert.Null(trade.CounterfactualDeltaVsActualNetPnlQuote);
    }

    [Fact]
    public void SummaryAggregator_IncludesExpectedTargetTouchAndExcursionMetrics()
    {
        var now = DateTime.UtcNow;
        var trades = new[]
        {
            Trade(now, grossPnl: 1m, netPnl: 0.8m) with
            {
                MfePercent = 4m,
                MaePercent = -1m,
                TouchedExpectedTarget = true,
                CounterfactualExitAtExpectedTargetNetPnlQuote = 1.2m,
                CounterfactualDeltaVsActualNetPnlQuote = 0.4m
            },
            Trade(now.AddMinutes(1), grossPnl: -1m, netPnl: -1.2m) with
            {
                MfePercent = 1m,
                MaePercent = -3m,
                TouchedExpectedTarget = false
            }
        };

        var summary = ReplaySummaryAggregator.BuildSummary(
            "1m",
            "p",
            "ETH",
            trades,
            new ProfileSignalStats(),
            new ProfileRuntimeSnapshot(
                true,
                true,
                1.00m,
                false,
                0.15m,
                false,
                1.20m,
                null,
                false,
                false,
                "PreviousCandleHigh",
                false,
                false,
                0.35m,
                0.12m,
                1.25m,
                true,
                55m,
                "OppositeSignalOnly",
                null,
                10,
                0.0002m,
                1,
                0.0002m,
                true,
                false,
                "Combined"));

        Assert.Equal(1, summary.ExpectedTargetTouchTrades);
        Assert.Equal(50m, summary.ExpectedTargetTouchRatePercent);
        Assert.Equal(2.5m, summary.AverageMfePercent);
        Assert.Equal(-2m, summary.AverageMaePercent);
        Assert.Equal(1.2m, summary.ExpectedTargetCounterfactualNetPnlQuote);
        Assert.Equal(0.4m, summary.ExpectedTargetCounterfactualDeltaQuote);
    }

    [Fact]
    public void DefaultProfiles_IncludeExpandedProfileMatrix_AndSymbolIsolatedSets()
    {
        var profiles = BacktestApplication.BuildDefaultProfiles();

        Assert.Equal(72, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "current-guarded-baseline-ETH" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "current-guarded-baseline-BNB" && p.Symbols.SequenceEqual([TradingSymbol.BNBUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "lowvol-disabled-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "lowvol-disabled-highvol-pullback-block-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-disabled-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-strict-rr-1.40-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-strict-rr-1.60-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-nearhigh-trendstrength-strict-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-reclaim-prevhigh-rr-1.20-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-reclaim-followthrough-v2-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-reclaim-followthrough-v3-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-v2-profitlock-90-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "pullback-prevhigh-profitlock-98-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
    }

    [Fact]
    public void DefaultProfiles_LowVolDisabled_SetsBreakoutFeatureOff()
    {
        var profile = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "lowvol-disabled-ETH+BNB");

        Assert.True(profile.ConfigOverrides.TryGetValue("DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry", out var enabled));
        Assert.Equal("false", enabled);
        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"]);
        Assert.Equal("1.00", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"]);
    }

    [Fact]
    public void DefaultProfiles_LowVolDisabledHighVolPullbackBlock_EnablesBlockFlag()
    {
        var profile = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "lowvol-disabled-highvol-pullback-block-ETH+BNB");

        Assert.Equal("false", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"]);
        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"]);
        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnablePullbackOverrideHighVolatilityBlock"]);
    }

    [Fact]
    public void DefaultProfiles_PullbackDisabled_DisablesOverride()
    {
        var profile = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "pullback-disabled-ETH+BNB");

        Assert.Equal("false", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"]);
        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"]);
    }

    [Fact]
    public void DefaultProfiles_PullbackStrictRr_AppliesExpectedThresholds()
    {
        var rr140 = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "pullback-strict-rr-1.40-ETH+BNB");
        var rr160 = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "pullback-strict-rr-1.60-ETH+BNB");

        Assert.Equal("1.40", rr140.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"]);
        Assert.Equal("1.60", rr160.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"]);
        Assert.Equal("true", rr140.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"]);
        Assert.Equal("0.30", rr140.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"]);
    }

    [Fact]
    public void DefaultProfiles_PullbackNearHighTrendStrengthStrict_AppliesExpectedConfig()
    {
        var profile = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "pullback-nearhigh-trendstrength-strict-ETH+BNB");

        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendNearRecentHighRejection"]);
        Assert.Equal("1.50", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresRewardRisk"]);
        Assert.Equal("0.0012", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresTrendStrengthPercent"]);
        Assert.Equal("1.20", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"]);
        Assert.Equal("0.30", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"]);
    }

    [Fact]
    public void DefaultProfiles_PullbackReclaimPrevHigh_AppliesExpectedConfig()
    {
        var profile = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "pullback-reclaim-prevhigh-rr-1.20-ETH+BNB");

        Assert.Equal("false", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"]);
        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"]);
        Assert.Equal("1.20", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"]);
        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"]);
        Assert.Equal("PreviousCandleHigh", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"]);
        Assert.Equal("0.30", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"]);
    }

    [Fact]
    public void DefaultProfiles_PullbackReclaimFollowThroughV2_AppliesExpectedConfig()
    {
        var profile = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "pullback-reclaim-followthrough-v2-ETH+BNB");

        Assert.Equal("false", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"]);
        Assert.Equal("true", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"]);
        Assert.Equal("true", profile.ConfigOverrides["Backtest:PullbackFollowThroughV2:Enabled"]);
        Assert.Equal("PreviousCandleHigh", profile.ConfigOverrides["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"]);
    }

    [Fact]
    public void DefaultProfiles_PullbackReclaimFollowThroughV3_AppliesExpectedConfig()
    {
        var profile = BacktestApplication.BuildDefaultProfiles()
            .First(p => p.ProfileName == "pullback-reclaim-followthrough-v3-ETH+BNB");

        Assert.Equal("true", profile.ConfigOverrides["Backtest:PullbackFollowThroughV2:Enabled"]);
        Assert.Equal("true", profile.ConfigOverrides["Backtest:PullbackFollowThroughV3:Enabled"]);
        Assert.Equal("0.35", profile.ConfigOverrides["PullbackV3MinResidualExpectedMovePercent"]);
        Assert.Equal("0.12", profile.ConfigOverrides["PullbackV3MinResidualNetMovePercent"]);
        Assert.Equal("1.25", profile.ConfigOverrides["PullbackV3MinResidualRewardRisk"]);
        Assert.Equal("true", profile.ConfigOverrides["PullbackV3RejectIfTargetAlreadyMostlyConsumed"]);
        Assert.Equal("55", profile.ConfigOverrides["PullbackV3MaxTargetConsumedPercent"]);
    }

    [Fact]
    public void DefaultProfiles_IncludeIndependentEthAndBnbProfiles()
    {
        var profiles = BacktestApplication.BuildDefaultProfiles();

        var ethOnly = profiles.Where(p => p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT])).ToArray();
        var bnbOnly = profiles.Where(p => p.Symbols.SequenceEqual([TradingSymbol.BNBUSDT])).ToArray();

        Assert.Equal(18, ethOnly.Length);
        Assert.Equal(18, bnbOnly.Length);
    }

    [Fact]
    public void Cli_DefaultInterval_Is1m()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);
        var dataDir = Path.Combine(tempRoot, "data");
        var outputDir = Path.Combine(tempRoot, "output");
        var settings = BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", dataDir,
            "--output-dir", outputDir
        ]);

        Assert.Single(settings.Intervals);
        Assert.Equal("1m", settings.Intervals[0]);
    }

    [Fact]
    public void Cli_ParsesSingleInterval()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);
        var settings = BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", Path.Combine(tempRoot, "data"),
            "--output-dir", Path.Combine(tempRoot, "output"),
            "--interval", "3m"
        ]);

        Assert.Single(settings.Intervals);
        Assert.Equal("3m", settings.Intervals[0]);
    }

    [Fact]
    public void Cli_ParsesMultipleIntervals()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);
        var settings = BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", Path.Combine(tempRoot, "data"),
            "--output-dir", Path.Combine(tempRoot, "output"),
            "--intervals", "1m,3m,5m"
        ]);

        Assert.Equal(3, settings.Intervals.Count);
        Assert.Equal(["1m", "3m", "5m"], settings.Intervals);
    }

    [Fact]
    public void Cli_ParsesBootstrapDays()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);
        var settings = BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", Path.Combine(tempRoot, "data"),
            "--output-dir", Path.Combine(tempRoot, "output"),
            "--bootstrap", "true",
            "--bootstrap-days", "14"
        ]);

        Assert.Equal(14, settings.BootstrapDays);
        Assert.Null(settings.BootstrapStartUtc);
        Assert.Null(settings.BootstrapEndUtc);
    }

    [Fact]
    public void Cli_ParsesBootstrapStartEndUtc()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);
        var settings = BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", Path.Combine(tempRoot, "data"),
            "--output-dir", Path.Combine(tempRoot, "output"),
            "--bootstrap", "true",
            "--bootstrap-start", "2026-05-01T00:00:00Z",
            "--bootstrap-end", "2026-05-31T00:00:00Z"
        ]);

        Assert.Equal(DateTime.Parse("2026-05-01T00:00:00Z").ToUniversalTime(), settings.BootstrapStartUtc);
        Assert.Equal(DateTime.Parse("2026-05-31T00:00:00Z").ToUniversalTime(), settings.BootstrapEndUtc);
        Assert.Null(settings.BootstrapDays);
    }

    [Fact]
    public void Cli_RejectsBootstrapDaysAndStartEndTogether()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);

        Assert.Throws<ArgumentException>(() => BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", Path.Combine(tempRoot, "data"),
            "--output-dir", Path.Combine(tempRoot, "output"),
            "--bootstrap-days", "7",
            "--bootstrap-start", "2026-05-01T00:00:00Z",
            "--bootstrap-end", "2026-05-31T00:00:00Z"
        ]));
    }

    [Fact]
    public void CandleAggregator_1mTo3m_AggregatesOhlcvCorrectly()
    {
        var candles = new[]
        {
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:00:00Z", 100, 101, 99, 100.5m, 10),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:01:00Z", 100.5m, 102, 100, 101.5m, 11),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:02:00Z", 101.5m, 103, 101, 102.5m, 12)
        };
        var result = CandleAggregator.Aggregate(TradingSymbol.ETHUSDT, candles, "1m", "3m");

        Assert.Single(result.Candles);
        var c = result.Candles[0];
        Assert.Equal(DateTime.Parse("2026-01-01T00:00:00Z").ToUniversalTime(), c.OpenTimeUtc);
        Assert.Equal(100m, c.Open);
        Assert.Equal(103m, c.High);
        Assert.Equal(99m, c.Low);
        Assert.Equal(102.5m, c.Close);
        Assert.Equal(33m, c.Volume);
    }

    [Fact]
    public void CandleAggregator_1mTo5m_AggregatesOhlcvCorrectly()
    {
        var candles = new[]
        {
            Candle(TradingSymbol.BNBUSDT, "2026-01-01T00:00:00Z", 10, 11, 9, 10.1m, 1),
            Candle(TradingSymbol.BNBUSDT, "2026-01-01T00:01:00Z", 10.1m, 12, 10, 11m, 2),
            Candle(TradingSymbol.BNBUSDT, "2026-01-01T00:02:00Z", 11m, 12.5m, 10.8m, 12m, 3),
            Candle(TradingSymbol.BNBUSDT, "2026-01-01T00:03:00Z", 12m, 13, 11.5m, 12.8m, 4),
            Candle(TradingSymbol.BNBUSDT, "2026-01-01T00:04:00Z", 12.8m, 14, 12.2m, 13.5m, 5)
        };
        var result = CandleAggregator.Aggregate(TradingSymbol.BNBUSDT, candles, "1m", "5m");

        Assert.Single(result.Candles);
        var c = result.Candles[0];
        Assert.Equal(10m, c.Open);
        Assert.Equal(14m, c.High);
        Assert.Equal(9m, c.Low);
        Assert.Equal(13.5m, c.Close);
        Assert.Equal(15m, c.Volume);
    }

    [Fact]
    public void CandleAggregator_BoundaryAlignment_Works()
    {
        var candles = new[]
        {
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:01:00Z", 1, 2, 1, 1.5m, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:02:00Z", 1.5m, 2, 1.2m, 1.7m, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:03:00Z", 1.7m, 2.1m, 1.6m, 2.0m, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:04:00Z", 2.0m, 2.2m, 1.9m, 2.1m, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:05:00Z", 2.1m, 2.3m, 2.0m, 2.2m, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:06:00Z", 2.2m, 2.4m, 2.1m, 2.3m, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:07:00Z", 2.3m, 2.5m, 2.2m, 2.4m, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:08:00Z", 2.4m, 2.6m, 2.3m, 2.5m, 1),
        };
        var result = CandleAggregator.Aggregate(TradingSymbol.ETHUSDT, candles, "1m", "3m");

        Assert.Equal(2, result.Candles.Count);
        Assert.Equal(DateTime.Parse("2026-01-01T00:03:00Z").ToUniversalTime(), result.Candles[0].OpenTimeUtc);
        Assert.Equal(DateTime.Parse("2026-01-01T00:06:00Z").ToUniversalTime(), result.Candles[1].OpenTimeUtc);
    }

    [Fact]
    public void CandleAggregator_DropsIncompleteFinalBucket()
    {
        var candles = new[]
        {
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:00:00Z", 1, 1, 1, 1, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:01:00Z", 1, 1, 1, 1, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:02:00Z", 1, 1, 1, 1, 1),
            Candle(TradingSymbol.ETHUSDT, "2026-01-01T00:03:00Z", 1, 1, 1, 1, 1)
        };
        var result = CandleAggregator.Aggregate(TradingSymbol.ETHUSDT, candles, "1m", "3m");

        Assert.Single(result.Candles);
        Assert.Equal(1, result.DroppedIncompleteFinalBucketCount);
    }

    [Fact]
    public void BootstrapDownloader_MergeByOpenTime_DeduplicatesAndSorts()
    {
        var existing = new[]
        {
            new KlineWireRow(1000, "1", "2", "0.5", "1.5", "10"),
            new KlineWireRow(1060, "1.5", "2.5", "1.4", "2.0", "11"),
        };
        var downloaded = new[]
        {
            new KlineWireRow(1060, "9", "9", "9", "9", "9"),
            new KlineWireRow(1120, "2.0", "3.0", "1.8", "2.8", "12")
        };

        var merged = BinanceKlineBootstrapDownloader.MergeByOpenTime(existing, downloaded);

        Assert.Equal(3, merged.Count);
        Assert.True(merged.Select(x => x.OpenTimeMs).SequenceEqual(new[] { 1000L, 1060L, 1120L }));
        Assert.Equal("9", merged[1].Open);
    }

    [Fact]
    public void ResolveIntervalOutputDirectory_MultiInterval_UsesSubfolder()
    {
        var path = BacktestApplication.ResolveIntervalOutputDirectory("root/out", "3m", multiIntervalRun: true);
        Assert.EndsWith("root/out/3m", path.Replace("\\", "/"));

        var singlePath = BacktestApplication.ResolveIntervalOutputDirectory("root/out", "3m", multiIntervalRun: false);
        Assert.Equal("root/out", singlePath.Replace("\\", "/"));
    }

    [Fact]
    public void BuildRobustnessCandidateProfiles_IncludesGuardedBnbProfiles()
    {
        var profiles = BacktestApplication.BuildRobustnessCandidateProfiles();

        Assert.Equal(18, profiles.Count);
        Assert.All(profiles, p => Assert.Equal([TradingSymbol.BNBUSDT], p.Symbols));
        Assert.Contains(profiles, p => p.ProfileName == "bnb-guard-prevhigh-profitlock-98");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-guard-v2-profitlock-90-fields");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-guard-prevhigh-profitlock-95-lockreach");
    }

    [Fact]
    public void BuildBnbGuardProfiles_IncludesFieldsLockreachAndCombinedModes()
    {
        var profiles = BacktestApplication.BuildBnbGuardProfiles();

        Assert.Equal(18, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "bnb-guard-prevhigh-profitlock-98" && p.ConfigOverrides["Backtest:BnbPullbackGuard:Mode"] == "Combined");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-guard-prevhigh-profitlock-98-fields" && p.ConfigOverrides["Backtest:BnbPullbackGuard:Mode"] == "FieldsOnly");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-guard-v2-profitlock-90-lockreach" && p.ConfigOverrides["Backtest:BnbPullbackGuard:Mode"] == "LockReachabilityOnly");
        Assert.All(profiles, p => Assert.Equal("true", p.ConfigOverrides["Backtest:BnbPullbackGuard:Enabled"]));
    }

    [Fact]
    public void BnbPullbackGuard_AllowsKnownWinnerMetrics()
    {
        var guard = CreateBnbGuard(BnbPullbackGuardMode.FieldsOnly);
        var signal = WinnerStyleSignal();
        var decision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, null, "5m", 98m, isV2Path: false);

        Assert.True(decision.IsAllowed);
        Assert.False(decision.Diagnostics.ExpectedMoveCapRejected);
        Assert.False(decision.Diagnostics.TrendStrengthCapRejected);
    }

    [Fact]
    public void BnbPullbackGuard_CombinedModeBlocksWinnerWhenLockDistanceExceedsCap()
    {
        var guard = CreateBnbGuard(BnbPullbackGuardMode.Combined);
        var signal = WinnerStyleSignal();
        var decision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, null, "5m", 98m, isV2Path: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(BnbPullbackEntryGuard.LockReachabilityExceeded, decision.Reason);
        Assert.True(decision.Diagnostics.LockReachabilityRejected);
    }

    [Fact]
    public void BnbPullbackGuard_BlocksHighExpectedMove()
    {
        var guard = CreateBnbGuard(BnbPullbackGuardMode.FieldsOnly);
        var signal = WinnerStyleSignal(expectedMovePercent: 0.91m);
        var decision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, null, "5m", 90m, isV2Path: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(BnbPullbackEntryGuard.ExpectedMoveCapExceeded, decision.Reason);
        Assert.True(decision.Diagnostics.ExpectedMoveCapRejected);
    }

    [Fact]
    public void BnbPullbackGuard_BlocksHighDistanceToInvalidation()
    {
        var guard = CreateBnbGuard(BnbPullbackGuardMode.FieldsOnly);
        var signal = WinnerStyleSignal(distanceToInvalidationPercent: 0.57m);
        var decision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, null, "5m", 90m, isV2Path: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(BnbPullbackEntryGuard.DistanceToInvalidationCapExceeded, decision.Reason);
        Assert.True(decision.Diagnostics.DistanceToInvalidationCapRejected);
    }

    [Fact]
    public void BnbPullbackGuard_BlocksHighTrendStrength()
    {
        var guard = CreateBnbGuard(BnbPullbackGuardMode.FieldsOnly);
        var signal = WinnerStyleSignal(trendStrengthPercent: 0.0012m);
        var decision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, null, "5m", 90m, isV2Path: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(BnbPullbackEntryGuard.TrendStrengthCapExceeded, decision.Reason);
        Assert.True(decision.Diagnostics.TrendStrengthCapRejected);
    }

    [Fact]
    public void BnbPullbackGuard_V2BlocksHighResidualExpectedMoveAndRewardRisk()
    {
        var guard = CreateBnbGuard(BnbPullbackGuardMode.FieldsOnly);
        var signal = WinnerStyleSignal();
        var diagnostics = new PullbackV2Diagnostics(
            PullbackSetupDetected: true,
            PullbackReclaimConfirmed: true,
            PullbackFollowThroughConfirmed: true,
            PullbackRejectedReason: null,
            ReclaimReferencePrice: 100m,
            FollowThroughReferencePrice: 101m,
            CandlesWaitedAfterReclaim: 1,
            ResidualExpectedMovePercent: 0.82m,
            ResidualEstimatedNetMovePercent: 0.57m,
            ResidualRewardRisk: 1.44m,
            DistanceFromEntryToExpectedTargetPercent: 0.82m);

        var moveDecision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, diagnostics, "5m", 90m, isV2Path: true);
        Assert.False(moveDecision.IsAllowed);
        Assert.Equal(BnbPullbackEntryGuard.ResidualExpectedMoveCapExceeded, moveDecision.Reason);

        var rrDiagnostics = diagnostics with { ResidualExpectedMovePercent = 0.37m, ResidualRewardRisk = 1.44m };
        var rrDecision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, rrDiagnostics, "5m", 90m, isV2Path: true);
        Assert.False(rrDecision.IsAllowed);
        Assert.Equal(BnbPullbackEntryGuard.ResidualRewardRiskCapExceeded, rrDecision.Reason);
    }

    [Fact]
    public void BnbPullbackGuard_LockReachabilityBlocksInflatedTargetDistance()
    {
        var guard = CreateBnbGuard(BnbPullbackGuardMode.LockReachabilityOnly);
        var signal = WinnerStyleSignal(expectedMovePercent: 0.907m);
        var decision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, null, "5m", 90m, isV2Path: false);

        Assert.False(decision.IsAllowed);
        Assert.Equal(BnbPullbackEntryGuard.LockReachabilityExceeded, decision.Reason);
        Assert.True(decision.Diagnostics.LockReachabilityRejected);
        Assert.Equal(0.8163m, decimal.Round(decision.Diagnostics.LockDistancePercent ?? 0m, 4));
    }

    [Fact]
    public void RobustnessSummary_IncludesBnbGuardBlockedSignals()
    {
        var windows = new[]
        {
            new RobustnessWindowDetailRow
            {
                ProfileName = "bnb-guard-prevhigh-profitlock-98",
                Interval = "5m",
                WindowLabel = "30d",
                WindowStartUtc = DateTime.UtcNow.AddDays(-30),
                WindowEndUtc = DateTime.UtcNow,
                TradesCount = 1,
                EstimatedNetPnlQuote = 0.04m,
                BnbPullbackGuardEnabled = true,
                BnbPullbackGuardBlockedSignals = 4,
                BnbPullbackGuardBlockedByReason = new Dictionary<string, int>
                {
                    [BnbPullbackEntryGuard.ExpectedMoveCapExceeded] = 2,
                    [BnbPullbackEntryGuard.TrendStrengthCapExceeded] = 2
                }
            }
        };

        var summary = RobustnessSummaryAggregator.BuildSummaries(windows).Single();

        Assert.True(summary.BnbPullbackGuardEnabled);
        Assert.Equal(4, summary.BnbPullbackGuardBlockedSignals);
        Assert.Equal(2, summary.BnbPullbackGuardBlockedByReason[BnbPullbackEntryGuard.ExpectedMoveCapExceeded]);
    }

    [Fact]
    public void CapturedMfeCalculator_SeparatesNegativeCaptureTrades()
    {
        var trades = new[]
        {
            TradeWithCapture(mfePercent: 2m, capturedMfePercent: 80m, netPnl: 1m),
            TradeWithCapture(mfePercent: 2m, capturedMfePercent: -1840m, netPnl: -1m)
        };

        var metrics = CapturedMfeCalculator.Compute(trades);

        Assert.Equal(CapturedMfeCalculator.CalculationMode, metrics.CalculationMode);
        Assert.Equal(80m, metrics.AvgCapturedMfePercentPositiveOnly);
        Assert.Equal(1, metrics.NegativeCaptureTradeCount);
        Assert.Equal(-880m, metrics.AvgCapturedMfeIncludingNegativeRatio);
    }

    [Fact]
    public void SummaryAggregator_ReportsCapturedMfeClarityFields()
    {
        var trades = new[]
        {
            TradeWithCapture(mfePercent: 2m, capturedMfePercent: 100m, netPnl: 1m),
            TradeWithCapture(mfePercent: 2m, capturedMfePercent: -500m, netPnl: -1m)
        };

        var summary = ReplaySummaryAggregator.BuildSummary(
            "5m",
            "pullback-v2-profitlock-98-BNB",
            "BNBUSDT",
            trades,
            new ProfileSignalStats(),
            CreateRuntimeSnapshot());

        Assert.Equal(CapturedMfeCalculator.CalculationMode, summary.CapturedMfeCalculationMode);
        Assert.Equal(100m, summary.AvgCapturedMfePercent);
        Assert.Equal(-200m, summary.AvgCapturedMfeIncludingNegativeRatio);
        Assert.Equal(1, summary.NegativeCaptureTradeCount);
    }

    [Fact]
    public void RobustnessSummaryAggregator_OneTradeProfileWarning_WhenSparseEvidence()
    {
        var windows = new[]
        {
            new RobustnessWindowDetailRow
            {
                ProfileName = "pullback-v2-profitlock-98-BNB",
                Interval = "5m",
                WindowLabel = "30d",
                WindowStartUtc = DateTime.UtcNow.AddDays(-30),
                WindowEndUtc = DateTime.UtcNow,
                TradesCount = 1,
                EstimatedNetPnlQuote = 0.02m
            },
            new RobustnessWindowDetailRow
            {
                ProfileName = "pullback-v2-profitlock-98-BNB",
                Interval = "5m",
                WindowLabel = "60d",
                WindowStartUtc = DateTime.UtcNow.AddDays(-60),
                WindowEndUtc = DateTime.UtcNow,
                TradesCount = 0,
                EstimatedNetPnlQuote = 0m
            }
        };

        var summary = RobustnessSummaryAggregator.BuildSummaries(windows).Single();

        Assert.True(summary.OneTradeProfileWarning);
        Assert.Equal(1, summary.TradesCount);
        Assert.Equal(1, summary.PositiveWindowsCount);
        Assert.Equal(0, summary.NegativeWindowsCount);
    }

    [Fact]
    public void RobustnessSummaryAggregator_AggregatesMultipleWindows()
    {
        var windows = new[]
        {
            WindowDetail("pullback-prevhigh-profitlock-98-BNB", "5m", "30d", trades: 2, net: 0.04m, profitLock: 2),
            WindowDetail("pullback-prevhigh-profitlock-98-BNB", "5m", "60d", trades: 1, net: -0.01m, profitLock: 0)
        };

        var summary = RobustnessSummaryAggregator.BuildSummaries(windows).Single();

        Assert.Equal(2, summary.WindowCount);
        Assert.Equal(3, summary.TradesCount);
        Assert.Equal(0.03m, summary.EstimatedNetPnlQuote);
        Assert.Equal(2, summary.ProfitLockExitTrades);
        Assert.Equal(1, summary.PositiveWindowsCount);
        Assert.Equal(1, summary.NegativeWindowsCount);
        Assert.Equal(-0.01m, summary.MinWindowNetPnl);
    }

    [Fact]
    public void RobustnessWindowResolver_SkipsWindowsWithoutEnoughData()
    {
        var dataStart = DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime();
        var dataEnd = DateTime.Parse("2026-05-01T00:00:00Z").ToUniversalTime();

        var windows = RobustnessWindowResolver.Resolve(dataStart, dataEnd, [30, 60, 90], null, null);

        Assert.Equal(3, windows.Count);
        Assert.False(windows.Single(w => w.Label == "30d").SkippedInsufficientData);
        Assert.True(windows.Single(w => w.Label == "60d").SkippedInsufficientData);
        Assert.True(windows.Single(w => w.Label == "90d").SkippedInsufficientData);
    }

    [Fact]
    public void CandleWindowSlicer_FiltersToInclusiveStartExclusiveEnd()
    {
        var candles = new[]
        {
            Candle(TradingSymbol.BNBUSDT, "2026-05-01T00:00:00Z", 1, 1, 1, 1, 1),
            Candle(TradingSymbol.BNBUSDT, "2026-05-01T00:01:00Z", 1, 1, 1, 1, 1),
            Candle(TradingSymbol.BNBUSDT, "2026-05-01T00:02:00Z", 1, 1, 1, 1, 1)
        };

        var sliced = CandleWindowSlicer.Slice(
            candles,
            DateTime.Parse("2026-05-01T00:01:00Z").ToUniversalTime(),
            DateTime.Parse("2026-05-01T00:02:00Z").ToUniversalTime());

        Assert.Single(sliced);
        Assert.Equal(DateTime.Parse("2026-05-01T00:01:00Z").ToUniversalTime(), sliced[0].OpenTimeUtc);
    }

    [Fact]
    public void ResolveRobustnessOutputDirectory_MultiInterval_UsesWindowAndIntervalFolders()
    {
        var path = RobustnessApplication.ResolveRobustnessOutputDirectory("root/out", "30d", "3m", multiIntervalRun: true);
        Assert.EndsWith("root/out/30d/3m", path.Replace("\\", "/"));
    }

    [Fact]
    public void Cli_ParsesRobustnessModeAndWindows()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);
        var settings = BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", Path.Combine(tempRoot, "data"),
            "--output-dir", Path.Combine(tempRoot, "output"),
            "--robustness", "true",
            "--robustness-windows", "30,60"
        ]);

        Assert.True(settings.RunRobustness);
        Assert.Equal(["1m", "3m", "5m"], settings.Intervals);
        Assert.Equal([30, 60], settings.RobustnessWindows);
    }

    [Fact]
    public void Cli_ParsesRobustnessFixedWindow()
    {
        var tempRoot = CreateTempDirectory();
        var appSettingsPath = CreateTempAppsettings(tempRoot);
        var settings = BacktestCli.Parse([
            "--appsettings", appSettingsPath,
            "--data-dir", Path.Combine(tempRoot, "data"),
            "--output-dir", Path.Combine(tempRoot, "output"),
            "--robustness", "true",
            "--robustness-window-start", "2026-04-01T00:00:00Z",
            "--robustness-window-end", "2026-05-01T00:00:00Z"
        ]);

        Assert.Equal(DateTime.Parse("2026-04-01T00:00:00Z").ToUniversalTime(), settings.RobustnessWindowStartUtc);
        Assert.Equal(DateTime.Parse("2026-05-01T00:00:00Z").ToUniversalTime(), settings.RobustnessWindowEndUtc);
    }

    [Fact]
    public void StrategyStaticStateResetter_ClearsMovingAverageStrategyStaticState()
    {
        var field = typeof(MovingAverageTrendStrategy)
            .GetField("LastEntrySignalTimesUtc", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(field);

        var map = field!.GetValue(null) as ConcurrentDictionary<TradingSymbol, DateTime>;
        Assert.NotNull(map);
        map!.Clear();
        map[TradingSymbol.ETHUSDT] = DateTime.UtcNow;
        Assert.True(StrategyStaticStateResetter.GetTrackedSymbolCount() > 0);

        StrategyStaticStateResetter.ResetMovingAverageTrendStrategyState();
        Assert.Equal(0, StrategyStaticStateResetter.GetTrackedSymbolCount());
    }

    private static MarketSnapshot Snapshot(TradingSymbol symbol, decimal price, DateTime timestampUtc)
    {
        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = price,
            TimestampUtc = timestampUtc,
            LatestClosedCandleOpenTimeUtc = timestampUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = timestampUtc,
            LatestClosedCandleClosePrice = price,
            HighPrices = [price],
            LowPrices = [price],
            ClosePrices = [price],
            Volumes = [1m]
        };
    }

    private static MarketSnapshot SnapshotWithRange(TradingSymbol symbol, decimal closePrice, decimal highPrice, decimal lowPrice, DateTime timestampUtc)
    {
        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = closePrice,
            TimestampUtc = timestampUtc,
            LatestClosedCandleOpenTimeUtc = timestampUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = timestampUtc,
            LatestClosedCandleClosePrice = closePrice,
            HighPrices = [highPrice],
            LowPrices = [lowPrice],
            ClosePrices = [closePrice],
            Volumes = [1m]
        };
    }

    private static MarketSnapshot SnapshotWithSeries(
        TradingSymbol symbol,
        DateTime timestampUtc,
        decimal currentPrice,
        IReadOnlyList<decimal> closes,
        IReadOnlyList<decimal> highs)
    {
        var lows = highs.Select(x => x - 0.1m).ToArray();
        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = currentPrice,
            TimestampUtc = timestampUtc,
            LatestClosedCandleOpenTimeUtc = timestampUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = timestampUtc,
            LatestClosedCandleClosePrice = closes[^1],
            HighPrices = highs.ToArray(),
            LowPrices = lows,
            ClosePrices = closes.ToArray(),
            Volumes = closes.Select(_ => 1m).ToArray()
        };
    }

    private static SimulatedTrade Trade(DateTime entryTimeUtc, decimal grossPnl, decimal netPnl)
    {
        return new SimulatedTrade
        {
            ProfileName = "p",
            Symbols = "ETH+BNB",
            Symbol = TradingSymbol.ETHUSDT,
            EntryTimeUtc = entryTimeUtc,
            ExitTimeUtc = entryTimeUtc.AddMinutes(1),
            EntryPrice = 100m,
            ExitPrice = 100m,
            Quantity = 1m,
            GrossPnlQuote = grossPnl,
            NetPnlQuote = netPnl,
            ExitReason = "OppositeSignal",
            DurationMinutes = 1m
        };
    }

    private static SimulatedTrade TradeWithCapture(decimal mfePercent, decimal capturedMfePercent, decimal netPnl)
    {
        return new SimulatedTrade
        {
            ProfileName = "pullback-v2-profitlock-98-BNB",
            Symbols = "BNBUSDT",
            Symbol = TradingSymbol.BNBUSDT,
            EntryTimeUtc = DateTime.UtcNow,
            ExitTimeUtc = DateTime.UtcNow.AddMinutes(5),
            EntryPrice = 100m,
            ExitPrice = 101m,
            Quantity = 1m,
            GrossPnlQuote = netPnl,
            NetPnlQuote = netPnl,
            MfePercent = mfePercent,
            CapturedMfePercent = capturedMfePercent,
            ExitReason = netPnl >= 0 ? "ProfitLock" : "OppositeSignal",
            DurationMinutes = 5m
        };
    }

    private static RobustnessWindowDetailRow WindowDetail(
        string profileName,
        string interval,
        string windowLabel,
        int trades,
        decimal net,
        int profitLock)
    {
        return new RobustnessWindowDetailRow
        {
            ProfileName = profileName,
            Interval = interval,
            WindowLabel = windowLabel,
            WindowStartUtc = DateTime.UtcNow.AddDays(-30),
            WindowEndUtc = DateTime.UtcNow,
            TradesCount = trades,
            EstimatedNetPnlQuote = net,
            ProfitLockExitTrades = profitLock,
            NetPnlBySymbol = new Dictionary<string, decimal> { ["BNBUSDT"] = net }
        };
    }

    [Fact]
    public void BuildBnbRetestContinuationV1Profiles_IncludesPrimaryProfitLockThresholds()
    {
        var profiles = BacktestApplication.BuildBnbRetestContinuationV1Profiles();

        Assert.Equal(3, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "bnb-retest-continuation-v1-profitlock-98");
        Assert.All(profiles, p =>
        {
            Assert.Equal("true", p.ConfigOverrides["Backtest:BnbRetestContinuationV1:Enabled"]);
            Assert.Equal("false", p.ConfigOverrides["Backtest:BnbPullbackGuard:Enabled"]);
            Assert.Equal("CappedExpectedMoveTarget", p.ConfigOverrides["Backtest:BnbRetestContinuationV1:TargetModel"]);
        });
    }

    [Fact]
    public void BuildBnbRetestRobustnessProfiles_IncludesTargetVariantsAndComparisons()
    {
        var profiles = BacktestApplication.BuildBnbRetestRobustnessProfiles();

        Assert.Contains(profiles, p => p.ProfileName == "bnb-retest-continuation-v1-raw-profitlock-98");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-retest-continuation-v1-atr-profitlock-98");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-guard-prevhigh-profitlock-98-fields");
        Assert.Contains(profiles, p => p.ProfileName == "pullback-prevhigh-profitlock-98-BNB");
    }

    [Fact]
    public void BnbRetestContinuation_AllowsWinnerStyleMetricsWithCappedTarget()
    {
        var model = CreateRetestModel(BnbRetestTargetModelName.CappedExpectedMoveTarget);
        var signal = WinnerStyleSignal();
        var snapshot = CreateRetestSnapshot(entryPrice: 679.83m);
        var decision = model.Evaluate(TradingSymbol.BNBUSDT, signal, snapshot, "5m", 98m);

        Assert.True(decision.IsAllowed);
        Assert.Equal(0.466m, decision.Diagnostics.RawExpectedMovePercent);
        Assert.Equal(0.466m, decision.Diagnostics.CappedExpectedMovePercent);
        Assert.False(decision.Diagnostics.TargetWasCapped);
        Assert.Equal("CappedExpectedMoveTarget", decision.Diagnostics.TargetModelName);
        Assert.Equal(0.4567m, decimal.Round(decision.Diagnostics.LockDistancePercent ?? 0m, 4));
    }

    [Fact]
    public void BnbRetestContinuation_CapsInflatedExpectedMove()
    {
        var model = CreateRetestModel(BnbRetestTargetModelName.CappedExpectedMoveTarget);
        var signal = WinnerStyleSignal(expectedMovePercent: 0.907m);
        var snapshot = CreateRetestSnapshot(entryPrice: 604m);
        var decision = model.Evaluate(TradingSymbol.BNBUSDT, signal, snapshot, "5m", 90m);

        Assert.True(decision.IsAllowed);
        Assert.Equal(0.907m, decision.Diagnostics.RawExpectedMovePercent);
        Assert.Equal(0.50m, decision.Diagnostics.CappedExpectedMovePercent);
        Assert.True(decision.Diagnostics.TargetWasCapped);
        Assert.Equal("MaxCappedExpectedMovePercent", decision.Diagnostics.CapReason);
    }

    [Fact]
    public void BnbRetestContinuation_BlocksHighTrendStrengthAndBearishPreviousCandle()
    {
        var model = CreateRetestModel(BnbRetestTargetModelName.CappedExpectedMoveTarget);
        var snapshot = CreateRetestSnapshot(entryPrice: 679.83m);

        var trendDecision = model.Evaluate(
            TradingSymbol.BNBUSDT,
            WinnerStyleSignal(trendStrengthPercent: 0.00129m),
            snapshot,
            "5m",
            98m);
        Assert.False(trendDecision.IsAllowed);
        Assert.Equal(BnbRetestContinuationV1Model.TrendStrengthCapExceeded, trendDecision.Reason);

        var bearishSignal = WinnerStyleSignal();
        bearishSignal = new StrategySignalResult
        {
            Signal = bearishSignal.Signal,
            Confidence = bearishSignal.Confidence,
            ExpectedMovePercent = bearishSignal.ExpectedMovePercent,
            ExpectedTargetPrice = bearishSignal.ExpectedTargetPrice,
            ExpectedTargetSource = bearishSignal.ExpectedTargetSource,
            DistanceToInvalidationPercent = bearishSignal.DistanceToInvalidationPercent,
            TrendStrengthPercent = bearishSignal.TrendStrengthPercent,
            ConsecutiveBullishTrendCandles = bearishSignal.ConsecutiveBullishTrendCandles,
            EntryNearRecentHigh = bearishSignal.EntryNearRecentHigh,
            PreviousCandleBearish = true,
            Reason = bearishSignal.Reason
        };
        var bearishDecision = model.Evaluate(TradingSymbol.BNBUSDT, bearishSignal, snapshot, "5m", 98m);
        Assert.False(bearishDecision.IsAllowed);
        Assert.Equal(BnbRetestContinuationV1Model.PreviousCandleBearishRejected, bearishDecision.Reason);
    }

    [Fact]
    public void BnbRetestContinuation_AtrLimitedTargetUsesReachableMove()
    {
        var model = CreateRetestModel(BnbRetestTargetModelName.AtrLimitedTarget);
        var signal = WinnerStyleSignal(expectedMovePercent: 0.907m);
        var snapshot = CreateRetestSnapshot(entryPrice: 604m, atr: 2.5m);
        var decision = model.Evaluate(TradingSymbol.BNBUSDT, signal, snapshot, "5m", 90m);

        Assert.True(decision.IsAllowed);
        Assert.Equal(0.907m, decision.Diagnostics.RawExpectedMovePercent);
        Assert.True(decision.Diagnostics.CappedExpectedMovePercent is > 0m and <= 0.50m);
        Assert.Equal("AtrLimitedTarget", decision.Diagnostics.TargetModelName);
    }

    private static BnbRetestContinuationV1Model CreateRetestModel(BnbRetestTargetModelName targetModel)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backtest:BnbRetestContinuationV1:Enabled"] = "true",
                ["Backtest:BnbRetestContinuationV1:TargetModel"] = targetModel.ToString(),
                ["Backtest:BnbRetestContinuationV1:MaxCappedExpectedMovePercent"] = "0.50",
                ["Backtest:BnbRetestContinuationV1:MaxConsecutiveBullishTrendCandles"] = "2",
                ["Backtest:BnbRetestContinuationV1:MaxTrendStrengthPercent"] = "0.00090",
                ["Backtest:BnbRetestContinuationV1:MaxDistanceToInvalidationPercent"] = "0.40",
                ["Backtest:BnbRetestContinuationV1:RejectPreviousCandleBearish"] = "true",
                ["Backtest:BnbRetestContinuationV1:AtrPeriod"] = "14",
                ["Backtest:BnbRetestContinuationV1:AtrMultiplier"] = "1.0"
            })
            .Build();
        return new BnbRetestContinuationV1Model(configuration);
    }

    private static MarketSnapshot CreateRetestSnapshot(decimal entryPrice, decimal atr = 1.2m)
    {
        var closes = new List<decimal>();
        var highs = new List<decimal>();
        var lows = new List<decimal>();
        var price = entryPrice - 20m;
        for (var i = 0; i < 30; i++)
        {
            var close = price + i * 0.5m;
            closes.Add(close);
            highs.Add(close + atr / 2m);
            lows.Add(close - atr / 2m);
        }

        closes[^1] = entryPrice;
        highs[^1] = entryPrice + atr / 2m;
        lows[^1] = entryPrice - atr / 2m;

        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = entryPrice,
            HighPrices = highs,
            LowPrices = lows,
            ClosePrices = closes,
            Volumes = Enumerable.Repeat(100m, closes.Count).ToArray(),
            TimestampUtc = DateTime.UtcNow
        };
    }

    private static BnbPullbackEntryGuard CreateBnbGuard(BnbPullbackGuardMode mode)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backtest:BnbPullbackGuard:Enabled"] = "true",
                ["Backtest:BnbPullbackGuard:Mode"] = mode.ToString(),
                ["Backtest:BnbPullbackGuard:MaxExpectedMovePercent"] = "0.50",
                ["Backtest:BnbPullbackGuard:MaxDistanceToInvalidationPercent"] = "0.40",
                ["Backtest:BnbPullbackGuard:MaxTrendStrengthPercent"] = "0.00090",
                ["Backtest:BnbPullbackGuard:MaxResidualExpectedMovePercent"] = "0.45",
                ["Backtest:BnbPullbackGuard:MaxResidualRewardRisk"] = "1.10",
                ["Backtest:BnbPullbackGuard:MaxLockDistancePercent"] = "0.40",
                ["Backtest:BnbPullbackGuard:MaxLockDistancePercentByInterval:5m"] = "0.40"
            })
            .Build();
        return new BnbPullbackEntryGuard(configuration);
    }

    private static StrategySignalResult WinnerStyleSignal(
        decimal? expectedMovePercent = null,
        decimal? distanceToInvalidationPercent = null,
        decimal? trendStrengthPercent = null)
        => new()
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.9m,
            ExpectedMovePercent = expectedMovePercent ?? 0.466m,
            ExpectedTargetPrice = 683m,
            ExpectedTargetSource = "src",
            DistanceToInvalidationPercent = distanceToInvalidationPercent ?? 0.353m,
            TrendStrengthPercent = trendStrengthPercent ?? 0.000778m,
            ShortMaSlopePercent = 0.000474m,
            ConsecutiveBullishTrendCandles = 1,
            EntryNearRecentHigh = true,
            Reason = "winner"
        };

    private static ProfileRuntimeSnapshot CreateRuntimeSnapshot()
        => new(
            true,
            true,
            1.20m,
            true,
            0.30m,
            false,
            1.20m,
            null,
            false,
            true,
            "PreviousCandleHigh",
            true,
            false,
            0.35m,
            0.12m,
            1.25m,
            true,
            55m,
            "ProfitLock98",
            98m,
            10,
            0.0002m,
            1,
            0.0002m,
            true,
            false,
            "Combined");

    private static BacktestEntryGuard CreateGuard(
        decimal minExecutionConfidence = 0.5m,
        bool requireExpectedTarget = false,
        decimal minExpectedMovePercent = 0.1m,
        decimal minNetProfitPercent = 0.05m,
        decimal feeRatePercent = 0.1m,
        decimal spreadPercent = 0.05m,
        decimal slippagePercent = 0m)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecisionEngine:MinExecutionConfidence"] = minExecutionConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:RequireStrategyExpectedTargetForSpotOpenLong"] = requireExpectedTarget.ToString(),
                ["Trading:MinExpectedMovePercent"] = minExpectedMovePercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:MinNetProfitPercent"] = minNetProfitPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Trading:MaxOpenPositionsPerSymbol"] = "1",
                ["DecisionEngine:Strategies:MovingAverageCrossover:MinConfidence"] = minExecutionConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
            .Build();

        return new BacktestEntryGuard(configuration, new ExecutionCostSettings(feeRatePercent, spreadPercent, slippagePercent));
    }

    private static PullbackFollowThroughV2Filter CreatePullbackV2Filter(
        bool enabled,
        bool enableV3 = false,
        decimal minResidualExpectedMove = 0.35m,
        decimal minResidualNetMove = 0.12m,
        decimal minResidualRewardRisk = 1.25m,
        bool rejectIfTargetMostlyConsumed = true,
        decimal maxTargetConsumedPercent = 55m)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backtest:PullbackFollowThroughV2:Enabled"] = enabled.ToString(),
                ["Backtest:PullbackFollowThroughV3:Enabled"] = enableV3.ToString(),
                ["Trading:MinExpectedMovePercent"] = "0.1",
                ["Trading:MinNetProfitPercent"] = "0.05",
                ["PullbackV3MinResidualExpectedMovePercent"] = minResidualExpectedMove.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["PullbackV3MinResidualNetMovePercent"] = minResidualNetMove.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["PullbackV3MinResidualRewardRisk"] = minResidualRewardRisk.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["PullbackV3RejectIfTargetAlreadyMostlyConsumed"] = rejectIfTargetMostlyConsumed.ToString(),
                ["PullbackV3MaxTargetConsumedPercent"] = maxTargetConsumedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
            .Build();
        return new PullbackFollowThroughV2Filter(configuration, new ExecutionCostSettings(0.1m, 0.05m, 0m));
    }

    [Fact]
    public void BuildReachabilityResearchProfiles_IncludesCoreAndExperimentalProfiles()
    {
        var profiles = BacktestApplication.BuildReachabilityResearchProfiles(includeExperimental: true);
        Assert.Contains(profiles, p => p.ProfileName == "bnb-reachability-research-1m");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-reachability-research-3m");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-reachability-research-5m");
        Assert.Contains(profiles, p => p.ProfileName == "bnb-reachability-confidence-relaxed-1m");
        Assert.All(profiles.Where(p => p.ProfileName.StartsWith("bnb-reachability-research-", StringComparison.Ordinal)), p =>
            Assert.Equal("true", p.ConfigOverrides["Backtest:ReachabilityResearch:Enabled"]));
    }

    [Fact]
    public void CandidateForwardOutcomeAnalyzer_ComputesForwardMfeMaeAndLockReachability()
    {
        var symbol = TradingSymbol.BNBUSDT;
        var start = new DateTime(2026, 3, 9, 8, 0, 0, DateTimeKind.Utc);
        var candles = Enumerable.Range(0, 90)
            .Select(i => new KlineCandle(
                symbol,
                start.AddMinutes(i),
                100m,
                i <= 30 ? 100.50m : 100.20m,
                i <= 45 ? 99.80m : 99.95m,
                100m,
                1m))
            .ToArray();

        var analytics = CandidateForwardOutcomeAnalyzer.Analyze(candles, start.AddMinutes(5), 100m, 0.40m, 60);
        Assert.Equal(0.36m, analytics.Lock90DistancePercent);
        Assert.True(analytics.ForwardMfe15Percent >= 0.49m);
        Assert.True(analytics.ForwardMae15Percent <= -0.20m);
        Assert.True(analytics.Lock90ReachableWithin60m);
        Assert.NotNull(analytics.TimeToLock90Minutes);
    }

    [Fact]
    public void CandidateReachabilityCollector_DetectsInflatedExpectedMoveAndConfidenceFalseNegative()
    {
        var settings = new CandidateReachabilitySettings
        {
            Enabled = true,
            ExpectedMoveInflatedMultiplier = 1.0m,
            ForwardHorizonMinutes = 60,
            MinFavorableMfePercent = 0.05m,
            MaxAcceptableForwardMae60Percent = 0.30m
        };
        var collector = new CandidateReachabilityCollector(settings);
        var symbol = TradingSymbol.BNBUSDT;
        var entryTime = new DateTime(2026, 3, 9, 8, 6, 0, DateTimeKind.Utc);
        var candles = Enumerable.Range(0, 90)
            .Select(i => new KlineCandle(symbol, entryTime.AddMinutes(i - 5), 100m, 100.55m, 99.95m, 100m, 1m))
            .ToArray();
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.67m,
            ExpectedMovePercent = 0.392m,
            ExpectedTargetPrice = 100.39m,
            ExpectedTargetSource = "src",
            DistanceToInvalidationPercent = 0.20m,
            Reason = "test"
        };
        var snapshot = new MarketSnapshot
        {
            CurrentPrice = 100m,
            TimestampUtc = entryTime
        };

        collector.Capture("1m", "bnb-reachability-research-1m", "BNBUSDT", symbol, signal, snapshot,
            "Guard", BacktestEntryGuard.ConfidenceBelowThreshold, 0.70m, 0.22m, false, candles);

        var record = Assert.Single(collector.Records);
        Assert.True(record.Lock90Reachable);
        Assert.True(record.ConfidenceFalseNegativeCandidate);
        Assert.False(record.ExpectedMoveInflated);
    }

    [Fact]
    public void CandidateReachabilityCollector_DetectsInflatedExpectedMove()
    {
        var settings = new CandidateReachabilitySettings
        {
            Enabled = true,
            ExpectedMoveInflatedMultiplier = 1.0m,
            ForwardHorizonMinutes = 60
        };
        var collector = new CandidateReachabilityCollector(settings);
        var symbol = TradingSymbol.BNBUSDT;
        var entryTime = new DateTime(2026, 3, 15, 14, 6, 0, DateTimeKind.Utc);
        var candles = Enumerable.Range(0, 90)
            .Select(i => new KlineCandle(symbol, entryTime.AddMinutes(i - 5), 100m, 100.08m, 99.95m, 100m, 1m))
            .ToArray();
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.74m,
            ExpectedMovePercent = 0.738m,
            ExpectedTargetPrice = 100.74m,
            ExpectedTargetSource = "src",
            DistanceToInvalidationPercent = 0.35m,
            Reason = "test"
        };
        var snapshot = new MarketSnapshot
        {
            CurrentPrice = 100m,
            TimestampUtc = entryTime
        };

        collector.Capture("5m", "bnb-reachability-research-5m", "BNBUSDT", symbol, signal, snapshot,
            "BnbPullbackGuard", "BnbPullbackGuard:ExpectedMoveCapExceeded", 0.70m, null, false, candles);

        var record = Assert.Single(collector.Records);
        Assert.True(record.ExpectedMoveInflated);
        Assert.False(record.Lock90Reachable);
    }

    [Fact]
    public async Task CandidateReachabilityReportWriter_WritesExpectedFiles()
    {
        var dir = CreateTempDirectory();
        var writer = new CandidateReachabilityReportWriter(dir);
        var candidates = new List<CandidateReachabilityRecord>
        {
            new()
            {
                Interval = "1m",
                ProfileName = "bnb-reachability-research-1m",
                Symbols = "BNBUSDT",
                Symbol = TradingSymbol.BNBUSDT,
                TimeUtc = new DateTime(2026, 3, 9, 8, 6, 0, DateTimeKind.Utc),
                RejectionLayer = "Guard",
                RejectionReason = BacktestEntryGuard.ConfidenceBelowThreshold,
                ConfidenceFalseNegativeCandidate = true,
                Lock90Reachable = true,
                ExpectedMoveInflated = false
            }
        };

        await writer.WriteAsync(candidates, [], CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(dir, "candidate-reachability-details.json")));
        Assert.True(File.Exists(Path.Combine(dir, "candidate-reachability-summary.json")));
        Assert.True(File.Exists(Path.Combine(dir, "confidence-false-negatives.json")));
        Assert.True(File.Exists(Path.Combine(dir, "inflated-target-candidates.json")));
        Assert.True(File.Exists(Path.Combine(dir, "candidate-reachability-details.csv")));
    }

    [Fact]
    public void BacktestEntryGuard_ReachabilityRelaxation_AllowsWhenCapsPass()
    {
        var guard = CreateReachabilityRelaxationGuard(relaxedMinConfidence: 0.65m);
        var signal = new StrategySignalResult
        {
            Signal = TradeSignal.Buy,
            Confidence = 0.67m,
            ExpectedMovePercent = 0.38m,
            ExpectedTargetPrice = 100.38m,
            ExpectedTargetSource = "src",
            DistanceToInvalidationPercent = 0.25m,
            Reason = "test"
        };
        var snapshot = new MarketSnapshot { CurrentPrice = 100m, TimestampUtc = DateTime.UtcNow };

        var decision = guard.Evaluate(TradingSymbol.BNBUSDT, signal, snapshot, hasOpenPositionForSymbol: false);

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void BuildBroadReachabilityScannerProfile_UsesReachabilityScanNaming()
    {
        var profile = BacktestApplication.BuildBroadReachabilityScannerProfile(TradingSymbol.ETHUSDT, "3m");
        Assert.Equal("reachability-scan-ETHUSDT-3m", profile.ProfileName);
        Assert.Equal("true", profile.ConfigOverrides["Backtest:ReachabilityResearch:Enabled"]);
        Assert.Equal("false", profile.ConfigOverrides["Backtest:BnbPullbackGuard:Enabled"]);
    }

    [Fact]
    public void BroadReachabilityRankingAggregator_RanksSymbolIntervalPairs()
    {
        var candidates = new List<CandidateReachabilityRecord>
        {
            new() { Symbol = TradingSymbol.BNBUSDT, Interval = "1m", Lock90Reachable = true, ExpectedMoveInflated = false },
            new() { Symbol = TradingSymbol.BNBUSDT, Interval = "1m", Lock90Reachable = false, ExpectedMoveInflated = true },
            new() { Symbol = TradingSymbol.ETHUSDT, Interval = "1m", Lock90Reachable = false, ExpectedMoveInflated = true },
            new() { Symbol = TradingSymbol.ETHUSDT, Interval = "1m", Lock90Reachable = false, ExpectedMoveInflated = true },
            new() { Symbol = TradingSymbol.ETHUSDT, Interval = "1m", Lock90Reachable = false, ExpectedMoveInflated = true }
        };

        var rankings = BroadReachabilityRankingAggregator.BuildRankings(candidates);
        Assert.Equal(2, rankings.Count);
        Assert.Equal(TradingSymbol.BNBUSDT, rankings[0].Symbol);
        Assert.Equal(1, rankings[0].ReachableLock90Count);
        Assert.Equal(0.5m, rankings[0].ReachableLock90Rate);
        Assert.Equal("Isolated", rankings[0].RepeatabilityVerdict);
    }

    [Fact]
    public void BroadReachabilityRankingAggregator_BuildDiscoveryAnswers_ReplaceStrategyWhenNoRepeatablePairs()
    {
        var candidates = new List<CandidateReachabilityRecord>
        {
            new() { Symbol = TradingSymbol.BNBUSDT, Interval = "5m", Lock90Reachable = false, ExpectedMoveInflated = true, ConfidenceFalseNegativeCandidate = false },
            new() { Symbol = TradingSymbol.SOLUSDT, Interval = "1m", Lock90Reachable = true, ExpectedMoveInflated = false, ConfidenceFalseNegativeCandidate = true, TimeUtc = DateTime.UtcNow }
        };
        var rankings = BroadReachabilityRankingAggregator.BuildRankings(candidates);
        var answers = BroadReachabilityRankingAggregator.BuildDiscoveryAnswers(candidates, rankings);

        Assert.Contains(answers, a => a.Verdict == "ReplaceStrategyFamily");
        Assert.Contains(answers, a => a.Question.Contains("new strategy model entirely", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BroadReachabilityReportWriter_WritesRankingAndDiscoveryOutputs()
    {
        var dir = CreateTempDirectory();
        var writer = new BroadReachabilityReportWriter(dir);
        var rankings = new List<SymbolIntervalReachabilityRankingRow>
        {
            new()
            {
                Symbol = TradingSymbol.ETHUSDT,
                Interval = "1m",
                CandidateCount = 3,
                ReachableLock90Count = 1,
                ReachableLock90Rate = 0.333333m,
                RepeatabilityVerdict = "Sparse",
                NetReachabilityScore = 12m
            }
        };

        await writer.WriteAsync([], rankings, [], CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(dir, "symbol-interval-reachability-ranking.json")));
        Assert.True(File.Exists(Path.Combine(dir, "symbol-interval-reachability-ranking.csv")));
        Assert.True(File.Exists(Path.Combine(dir, "broad-reachability-discovery-answers.json")));
    }

    private static BacktestEntryGuard CreateReachabilityRelaxationGuard(decimal relaxedMinConfidence = 0.65m)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecisionEngine:Strategies:MovingAverageCrossover:MinConfidence"] = "0.70",
                ["Trading:MinExpectedMovePercent"] = "0.1",
                ["Trading:MinNetProfitPercent"] = "0.05",
                ["Backtest:ReachabilityConfidenceRelaxation:Enabled"] = "true",
                ["Backtest:ReachabilityConfidenceRelaxation:MaxLock90DistancePercent"] = "0.40",
                ["Backtest:ReachabilityConfidenceRelaxation:MaxDistanceToInvalidationPercent"] = "0.40",
                ["Backtest:ReachabilityConfidenceRelaxation:RelaxedMinConfidence"] = relaxedMinConfidence.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })
            .Build();
        return new BacktestEntryGuard(configuration, new ExecutionCostSettings(0.1m, 0.05m, 0m));
    }

    private static KlineCandle Candle(TradingSymbol symbol, string openTimeUtc, decimal open, decimal high, decimal low, decimal close, decimal volume)
        => new(symbol, DateTime.Parse(openTimeUtc).ToUniversalTime(), open, high, low, close, volume);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tb-backtest-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateTempAppsettings(string dir)
    {
        var path = Path.Combine(dir, "appsettings.json");
        File.WriteAllText(path, """
{
  "DecisionEngine": {
    "MinExecutionConfidence": 0.70
  },
  "Trading": {
    "FeeRatePercent": 0.1,
    "EstimatedSpreadPercent": 0.05
  }
}
""");
        return path;
    }
}
