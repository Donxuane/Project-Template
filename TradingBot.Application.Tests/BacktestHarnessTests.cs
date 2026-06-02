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
            new ProfileRuntimeSnapshot(true, 10, 0.0002m, 1, 0.0002m, true));

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
            new ProfileRuntimeSnapshot(false, 24, 0.0020m, 2, 0.0008m, true));

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
            new ProfileRuntimeSnapshot(true, 10, 0.0002m, 1, 0.0002m, true));

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

        Assert.Equal(16, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "current-guarded-baseline-ETH" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "current-guarded-baseline-BNB" && p.Symbols.SequenceEqual([TradingSymbol.BNBUSDT]));
        Assert.Contains(profiles, p => p.ProfileName == "lowvol-disabled-ETH+BNB+SOL" && p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT]));
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
    public void DefaultProfiles_IncludeIndependentEthAndBnbProfiles()
    {
        var profiles = BacktestApplication.BuildDefaultProfiles();

        var ethOnly = profiles.Where(p => p.Symbols.SequenceEqual([TradingSymbol.ETHUSDT])).ToArray();
        var bnbOnly = profiles.Where(p => p.Symbols.SequenceEqual([TradingSymbol.BNBUSDT])).ToArray();

        Assert.Equal(4, ethOnly.Length);
        Assert.Equal(4, bnbOnly.Length);
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
