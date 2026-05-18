using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using Xunit;

namespace TradingBot.Application.Tests;

public class MovingAverageTrendStrategySpotSemanticsTests
{
    [Fact]
    public async Task EntryRejected_TrendConfidenceTooWeak_LogsThresholdAndMetrics()
    {
        var positionManager = new PositionManager();
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 620.4m,
                CurrentLongMa = 620.1m,
                PreviousShortMa = 620.35m,
                PreviousLongMa = 620.05m,
                ShortMaSlopePercent = 0.00008m,
                LongMaSlopePercent = 0.00004m,
                TrendStrengthPercent = 0.00015m,
                ConfidenceScore = 20,
                IsBullishTrendConfirmed = true,
                CurrentTrendState = TrendState.Bullish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            logger: logger);

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("trend confidence too weak", result.Reason, StringComparison.OrdinalIgnoreCase);
        var rejectionLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rejectionLog);
        Assert.Equal(20, rejectionLog!.Get<int>("TrendConfidenceScore"));
        Assert.Equal(35, rejectionLog.Get<int>("MinimumTrendConfidenceScore"));
        Assert.Equal(0.00008m, rejectionLog.Get<decimal>("ShortMaSlopePercent"));
        Assert.Equal(0.00015m, rejectionLog.Get<decimal>("TrendStrengthPercent"));
    }

    [Fact]
    public async Task EntryRejected_WaitingConfirmedDirection_LogsSlopeAndStrengthMetrics()
    {
        var positionManager = new PositionManager();
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 620.2m,
                CurrentLongMa = 620.1m,
                PreviousShortMa = 620.2m,
                PreviousLongMa = 620.1m,
                ShortMaSlopePercent = 0.00001m,
                LongMaSlopePercent = 0.00001m,
                TrendStrengthPercent = 0.0002m,
                ConfidenceScore = 60,
                CurrentTrendState = TrendState.Bullish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            logger: logger);

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("waiting for confirmed trend direction", result.Reason, StringComparison.OrdinalIgnoreCase);
        var rejectionLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rejectionLog);
        Assert.Equal("No entry signal - waiting for confirmed trend direction.", rejectionLog!.Get<string>("Reason"));
        Assert.Equal(0.00001m, rejectionLog.Get<decimal>("ShortMaSlopePercent"));
        Assert.Equal(0.0002m, rejectionLog.Get<decimal>("TrendStrengthPercent"));
        Assert.Equal(false, rejectionLog.Get<bool>("RequireCrossoverForEntry"));
    }

    [Fact]
    public async Task LowVolatilityWithoutBreakout_ReturnsHold()
    {
        var positionManager = new PositionManager();
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 620.10m,
                CurrentLongMa = 620.05m,
                PreviousShortMa = 620.09m,
                PreviousLongMa = 620.04m,
                ShortMaSlopePercent = 0.00005m,
                LongMaSlopePercent = 0.00003m,
                TrendStrengthPercent = 0.00020m,
                ConfidenceScore = 40,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireBreakoutConfirmation"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowVolatilitySnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Equal("No entry signal - low-volatility market without breakout.", result.Reason);
        var breakoutLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("low-volatility breakout diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(breakoutLog);
        Assert.Equal(false, breakoutLog!.Get<bool>("Passed"));
    }

    [Fact]
    public async Task LowVolatilityWithBreakoutAndPositiveSlope_DoesNotAdvanceOnSameClosedCandle_AdvancesOnNewClosedCandle()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.5m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.4m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = 0.002m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.003m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            });

        var baselineClosedCandle = DateTime.UtcNow.AddMinutes(-1);
        var first = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.SOLUSDT, baselineClosedCandle));
        var secondSameCandle = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.SOLUSDT, baselineClosedCandle));
        var thirdNewCandle = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.SOLUSDT, baselineClosedCandle.AddMinutes(1)));

        Assert.Equal(TradeSignal.Hold, first.Signal);
        Assert.Equal("No entry signal - breakout detected, waiting for new closed candle confirmation.", first.Reason);
        Assert.Equal(TradeSignal.Hold, secondSameCandle.Signal);
        Assert.Equal("No entry signal - breakout detected, waiting for new closed candle confirmation.", secondSameCandle.Reason);
        Assert.Equal(TradeSignal.Buy, thirdNewCandle.Signal);
        Assert.Equal("Entry signal - low-volatility breakout confirmed after follow-through.", thirdNewCandle.Reason);
    }

    [Fact]
    public async Task LowVolatilityWithBreakout_WhenFeatureDisabled_ReturnsHold()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.6m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.4m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = 0.002m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.003m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Equal("No entry signal - low-volatility market without breakout.", result.Reason);
    }

    [Fact]
    public async Task LowVolatilityWithBreakout_WhenConfirmationDisabled_ReturnsImmediateBuy()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.5m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.4m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = 0.002m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.003m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireBreakoutConfirmation"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.DOGEUSDT));

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.Equal("Entry signal - low-volatility breakout confirmed.", result.Reason);
    }

    [Fact]
    public async Task PendingBreakoutFails_WhenPriceFallsBelowThreshold()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.5m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.4m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = 0.002m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.003m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutHoldBufferPercent"] = "0.01"
            });

        var baselineClosedCandle = DateTime.UtcNow.AddMinutes(-1);
        var first = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.BTCUSDT, baselineClosedCandle));
        var second = await strategy.GenerateSignalAsync(CreateLowVolatilitySnapshot(TradingSymbol.BTCUSDT, baselineClosedCandle.AddMinutes(1)));

        Assert.Equal(TradeSignal.Hold, first.Signal);
        Assert.Equal("No entry signal - breakout detected, waiting for new closed candle confirmation.", first.Reason);
        Assert.Equal(TradeSignal.Hold, second.Signal);
        Assert.Equal("No entry signal - pending breakout failed confirmation.", second.Reason);
    }

    [Fact]
    public async Task PendingBreakoutFails_WhenShortSlopeTurnsNegative()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.5m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.4m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = 0.002m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.003m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            });

        var baselineClosedCandle = DateTime.UtcNow.AddMinutes(-1);
        _ = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.BNBUSDT, baselineClosedCandle));

        var failingStrategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.4m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.5m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = -0.0002m,
                LongMaSlopePercent = 0.0001m,
                TrendStrengthPercent = 0.0025m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            });

        var result = await failingStrategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.BNBUSDT, baselineClosedCandle.AddMinutes(1)));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Equal("No entry signal - pending breakout failed confirmation.", result.Reason);
    }

    [Fact]
    public async Task LowVolatilityBreakoutDiagnostics_IncludeBreakoutMetrics()
    {
        var positionManager = new PositionManager();
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.6m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.4m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = 0.002m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.003m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            });

        _ = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.ETHUSDT, DateTime.UtcNow.AddMinutes(-1)));

        var diagnosticsLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("low-volatility breakout diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(diagnosticsLog);
        Assert.True(diagnosticsLog!.Values.ContainsKey("RecentRangeHigh"));
        Assert.True(diagnosticsLog.Values.ContainsKey("BreakoutThresholdPrice"));
        Assert.True(diagnosticsLog.Values.ContainsKey("ShortMaSlopePercent"));
        Assert.True(diagnosticsLog.Values.ContainsKey("TrendStrengthCurrent"));
        Assert.True(diagnosticsLog.Values.ContainsKey("Passed"));
        Assert.True(diagnosticsLog.Values.ContainsKey("PendingBreakoutExists"));
        Assert.True(diagnosticsLog.Values.ContainsKey("ConfirmationPassed"));
        Assert.True(diagnosticsLog.Values.ContainsKey("LatestClosedCandleOpenTimeUtc"));
        Assert.True(diagnosticsLog.Values.ContainsKey("LatestClosedCandleCloseTimeUtc"));
        Assert.True(diagnosticsLog.Values.ContainsKey("LatestClosedCandleClosePrice"));
        Assert.True(diagnosticsLog.Values.ContainsKey("PendingDetectedCandleTimeUtc"));
        Assert.True(diagnosticsLog.Values.ContainsKey("PendingLastConfirmationCandleTimeUtc"));
        Assert.True(diagnosticsLog.Values.ContainsKey("PendingConfirmedClosedCandleCount"));
        Assert.True(diagnosticsLog.Values.ContainsKey("ConfirmationHoldBufferThresholdPrice"));
    }

    [Fact]
    public async Task BreakoutConfirmation_PassesOnlyAfterRequiredDistinctClosedCandles()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.5m,
                CurrentLongMa = 100.3m,
                PreviousShortMa = 100.4m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = 0.002m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.003m,
                ConfidenceScore = 45,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.LowVolatility
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles"] = "2"
            });

        var candle0 = DateTime.UtcNow.AddMinutes(-2);
        var first = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.ETHUSDT, candle0));
        var second = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.ETHUSDT, candle0.AddMinutes(1)));
        var third = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.ETHUSDT, candle0.AddMinutes(2)));

        Assert.Equal(TradeSignal.Hold, first.Signal);
        Assert.Equal(TradeSignal.Hold, second.Signal);
        Assert.Equal("No entry signal - breakout detected, waiting for new closed candle confirmation.", second.Reason);
        Assert.Equal(TradeSignal.Buy, third.Signal);
    }

    [Fact]
    public async Task TrendingMarketBehavior_RemainsBullishEntry()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 621m,
                CurrentLongMa = 620m,
                PreviousShortMa = 620.5m,
                PreviousLongMa = 619.9m,
                ShortMaSlopePercent = 0.001m,
                LongMaSlopePercent = 0.0004m,
                TrendStrengthPercent = 0.0012m,
                ConfidenceScore = 70,
                IsBullishTrendConfirmed = true,
                IsBullishCrossover = true,
                CurrentTrendState = TrendState.Bullish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Regime = VolatilityRegime.Normal,
                Reason = "normal"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.Contains("bullish trend confirmed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotFlatBearishConfirmed_ReturnsHold_NotSell()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: true,
            trendResult: CreateTrend(isBearishConfirmed: true),
            positionManager: positionManager);

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());
        var state = positionManager.GetState(TradingSymbol.BNBUSDT);

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("Spot bearish signal ignored", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(state.IsInPosition);
        Assert.NotEqual(PositionType.Short, state.PositionType);
    }

    [Fact]
    public async Task SpotExistingShortState_IsIgnoredAndReset()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.BNBUSDT, PositionType.Short, 620m, TrendState.Bearish, DateTime.UtcNow.AddMinutes(-1));

        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: true,
            trendResult: CreateTrend(isBearishConfirmed: true),
            positionManager: positionManager);

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());
        var state = positionManager.GetState(TradingSymbol.BNBUSDT);

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("Spot short state ignored", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(state.IsInPosition);
        Assert.Equal(PositionType.None, state.PositionType);
        Assert.DoesNotContain("Holding short position", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotExistingLongAndBearishReversal_StillReturnsSellExit()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.BNBUSDT, PositionType.Long, 620m, TrendState.Bullish, DateTime.UtcNow.AddMinutes(-1));

        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: true,
            trendResult: CreateTrend(isBearishConfirmed: true),
            positionManager: positionManager);

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Sell, result.Signal);
        Assert.Contains("Exit signal - trend reversal detected for long position.", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FuturesBearishConfirmed_AllowsExistingShortEntryBehavior()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Futures,
            allowShortSelling: true,
            trendResult: CreateTrend(isBearishConfirmed: true),
            positionManager: positionManager);

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());
        var state = positionManager.GetState(TradingSymbol.BNBUSDT);

        Assert.Equal(TradeSignal.Sell, result.Signal);
        Assert.Contains("Entry signal - bearish trend confirmed", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(state.IsInPosition);
        Assert.Equal(PositionType.Short, state.PositionType);
    }

    private static MovingAverageTrendStrategy CreateStrategy(
        TradingMode tradingMode,
        bool allowShortSelling,
        TrendAnalysisResult trendResult,
        IPositionManager positionManager,
        ILogger<MovingAverageTrendStrategy>? logger = null,
        MarketConditionResult? marketConditionResult = null,
        IReadOnlyDictionary<string, string?>? configOverrides = null)
    {
        var values = new Dictionary<string, string?>
            {
                ["Trading:Mode"] = tradingMode.ToString(),
                ["DecisionEngine:MovingAverageCrossoverStrategy:ShortPeriod"] = "2",
                ["DecisionEngine:MovingAverageCrossoverStrategy:LongPeriod"] = "3",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MinimumTrendConfidenceScore"] = "35",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MinimumMarketConditionScore"] = "55",
                ["DecisionEngine:MovingAverageCrossoverStrategy:AllowShortSelling"] = allowShortSelling.ToString(),
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireCrossoverForEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:CooldownSeconds"] = "0",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "10",
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutBufferPercent"] = "0.0005",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MinBreakoutSlopePercent"] = "0.0002",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequirePositiveShortSlopeForBreakout"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireTrendStrengthExpansion"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireBreakoutConfirmation"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles"] = "1",
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutHoldBufferPercent"] = "0.0",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireCloseAboveBreakoutThreshold"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireShortSlopeStillPositiveOnConfirmation"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireNoImmediateBearishCandleAfterBreakout"] = "false"
            };
        if (configOverrides is not null)
        {
            foreach (var kv in configOverrides)
                values[kv.Key] = kv.Value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        return new MovingAverageTrendStrategy(
            logger ?? NullLogger<MovingAverageTrendStrategy>.Instance,
            new FakeTrendStateService(trendResult),
            positionManager,
            new FakeMarketStateTracker(),
            new FakeMarketConditionService(marketConditionResult),
            configuration);
    }

    private static MarketSnapshot CreateSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 620m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 619m,
            ClosePrices = [616m, 617m, 618m, 619m, 620m],
            HighPrices = [617m, 618m, 619m, 620m, 621m],
            LowPrices = [615m, 616m, 617m, 618m, 619m],
            Volumes = [10m, 11m, 12m, 11m, 13m]
        };
    }

    private static MarketSnapshot CreateLowVolatilitySnapshot(TradingSymbol symbol, DateTime? latestClosedCandleTimeUtc = null)
    {
        var closedCandleTimeUtc = latestClosedCandleTimeUtc ?? DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = 100.10m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 100.01m,
            ClosePrices = [100.00m, 100.01m, 100.00m, 100.02m, 100.01m, 100.00m, 100.02m, 100.01m, 100.00m, 100.01m, 100.10m],
            HighPrices = [100.02m, 100.03m, 100.02m, 100.03m, 100.02m, 100.02m, 100.03m, 100.02m, 100.02m, 100.02m, 100.11m],
            LowPrices = [99.98m, 99.99m, 99.98m, 99.99m, 99.98m, 99.98m, 99.99m, 99.98m, 99.98m, 99.98m, 100.00m],
            Volumes = [10m, 10m, 11m, 10m, 10m, 10m, 11m, 10m, 10m, 10m, 12m]
        };
    }

    private static MarketSnapshot CreateBreakoutSnapshot(TradingSymbol symbol, DateTime? latestClosedCandleTimeUtc = null)
    {
        var closedCandleTimeUtc = latestClosedCandleTimeUtc ?? DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = 100.60m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 100.60m,
            ClosePrices = [100.00m, 100.01m, 100.02m, 100.01m, 100.03m, 100.02m, 100.01m, 100.03m, 100.02m, 100.01m, 100.60m],
            HighPrices = [100.02m, 100.03m, 100.03m, 100.02m, 100.04m, 100.03m, 100.02m, 100.04m, 100.03m, 100.02m, 100.61m],
            LowPrices = [99.98m, 99.99m, 99.99m, 99.98m, 100.00m, 99.99m, 99.98m, 100.00m, 99.99m, 99.98m, 100.20m],
            Volumes = [10m, 10m, 11m, 10m, 12m, 10m, 10m, 11m, 10m, 10m, 20m]
        };
    }

    private static TrendAnalysisResult CreateTrend(bool isBearishConfirmed)
    {
        return new TrendAnalysisResult
        {
            IsValid = true,
            MarketRegime = MarketRegime.Trending,
            ConfidenceScore = 60,
            CurrentTrendState = isBearishConfirmed ? TrendState.Bearish : TrendState.Bullish,
            IsBearishTrendConfirmed = isBearishConfirmed,
            IsBullishTrendConfirmed = !isBearishConfirmed,
            IsBearishCrossover = isBearishConfirmed
        };
    }

    private sealed class FakeTrendStateService(TrendAnalysisResult trendResult) : ITrendStateService
    {
        public int GetRequiredPeriods(int shortPeriod, int longPeriod) => Math.Max(shortPeriod, longPeriod) + 1;

        public TrendAnalysisResult Analyze(MarketSnapshot marketData, int shortPeriod, int longPeriod) => trendResult;
    }

    private sealed class FakeMarketConditionService(MarketConditionResult? result = null) : IMarketConditionService
    {
        public int RequiredPeriods => 1;

        public MarketConditionResult Evaluate(MarketSnapshot snapshot)
            => result ?? new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Normal
            };
    }

    private sealed class FakeMarketStateTracker : IMarketStateTracker
    {
        public SymbolMarketState GetState(TradingSymbol symbol) => new();
        public void Update(TradingSymbol symbol, TrendState trendState)
        {
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (state is IEnumerable<KeyValuePair<string, object?>> structured)
            {
                foreach (var kv in structured)
                    values[kv.Key] = kv.Value;
            }

            Entries.Add(new LogEntry(message, values));
        }
    }

    private sealed record LogEntry(string Message, IReadOnlyDictionary<string, object?> Values)
    {
        public T Get<T>(string key)
        {
            var value = Values[key];
            if (value is T typed)
                return typed;

            return (T)Convert.ChangeType(value!, typeof(T));
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose()
        {
        }
    }
}
