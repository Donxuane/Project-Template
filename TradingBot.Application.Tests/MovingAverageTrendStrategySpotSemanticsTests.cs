using System;
using System.Collections;
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
        Assert.Contains("No entry signal - low-volatility market without breakout.", result.Reason);
        Assert.Contains("BreakoutFailure=", result.Reason);
        var breakoutLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("low-volatility breakout diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(breakoutLog);
        Assert.Equal(false, breakoutLog!.Get<bool>("Passed"));
        var rejectionLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rejectionLog);
        Assert.Equal(true, rejectionLog!.Get<bool>("LowVolBreakoutPathEvaluated"));
        Assert.Equal(false, rejectionLog.Get<bool>("LowVolBreakoutPassed"));
        Assert.Equal(false, rejectionLog.Get<bool>("NormalTrendEntryPathEvaluated"));
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
        Assert.Contains("No entry signal - low-volatility market without breakout.", result.Reason);
    }

    [Fact]
    public async Task LowVolatilityFailedBreakout_WhenFallbackDisabled_PreservesOldHoldBehavior()
    {
        var positionManager = new PositionManager();
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.4m,
                CurrentLongMa = 100.2m,
                PreviousShortMa = 100.3m,
                PreviousLongMa = 100.1m,
                ShortMaSlopePercent = 0.001m,
                LongMaSlopePercent = 0.0005m,
                TrendStrengthPercent = 0.002m,
                ConfidenceScore = 60,
                IsBullishTrendConfirmed = true,
                CurrentTrendState = TrendState.Bullish,
                MarketRegime = MarketRegime.Trending
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
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowVolatilitySnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("low-volatility market without breakout", result.Reason, StringComparison.OrdinalIgnoreCase);
        var rejectionLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rejectionLog);
        Assert.Equal(false, rejectionLog!.Get<bool>("NormalTrendFallbackAfterFailedBreakoutEnabled"));
        Assert.Equal(false, rejectionLog.Get<bool>("NormalTrendFallbackUsed"));
    }

    [Fact]
    public async Task LowVolatilityFailedBreakout_WithFallbackEnabled_WeakTrendStillReturnsHold()
    {
        var positionManager = new PositionManager();
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.1m,
                CurrentLongMa = 100.0m,
                PreviousShortMa = 100.1m,
                PreviousLongMa = 100.0m,
                ShortMaSlopePercent = 0.0001m,
                LongMaSlopePercent = 0.0001m,
                TrendStrengthPercent = 0.0001m,
                ConfidenceScore = 20,
                CurrentTrendState = TrendState.Neutral,
                MarketRegime = MarketRegime.Trending
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
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowVolatilitySnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("trend confidence too weak", result.Reason, StringComparison.OrdinalIgnoreCase);
        var rejectionLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rejectionLog);
        Assert.Equal(true, rejectionLog!.Get<bool>("NormalTrendFallbackAfterFailedBreakoutEnabled"));
        Assert.Equal(true, rejectionLog.Get<bool>("NormalTrendFallbackUsed"));
    }

    [Fact]
    public async Task LowVolatilityFailedBreakout_WithFallbackEnabled_StrongBullishTrendReturnsBuy()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.4m,
                CurrentLongMa = 100.2m,
                PreviousShortMa = 100.3m,
                PreviousLongMa = 100.1m,
                ShortMaSlopePercent = 0.0015m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.0025m,
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
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowVolatilitySnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.Contains("bullish trend confirmed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LowVolatilityBreakoutPasses_WithFallbackEnabled_BreakoutPathStillWins()
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
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireBreakoutConfirmation"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.Equal("Entry signal - low-volatility breakout confirmed.", result.Reason);
    }

    [Fact]
    public async Task LowVolatilityFailedBreakout_FallbackEnabled_RequireCrossoverAppliesToNormalFallback()
    {
        var positionManager = new PositionManager();
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.4m,
                CurrentLongMa = 100.2m,
                PreviousShortMa = 100.3m,
                PreviousLongMa = 100.1m,
                ShortMaSlopePercent = 0.0015m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.0025m,
                ConfidenceScore = 70,
                IsBullishTrendConfirmed = true,
                IsBullishCrossover = false,
                CurrentTrendState = TrendState.Bullish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireCrossoverForEntry"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowVolatilitySnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("waiting for confirmed trend direction", result.Reason, StringComparison.OrdinalIgnoreCase);
        var rejectionLog = logger.Entries.FirstOrDefault(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(rejectionLog);
        Assert.Equal("Bullish crossover is required but not present.", rejectionLog!.Get<string>("CanEnterLongFailureReason"));
    }

    [Fact]
    public async Task LowVolatilityFailedBreakout_FallbackEnabled_SpotBearishStillIgnored()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.0m,
                CurrentLongMa = 100.2m,
                PreviousShortMa = 100.1m,
                PreviousLongMa = 100.25m,
                ShortMaSlopePercent = -0.001m,
                LongMaSlopePercent = -0.0004m,
                TrendStrengthPercent = 0.002m,
                ConfidenceScore = 70,
                IsBearishTrendConfirmed = true,
                CurrentTrendState = TrendState.Bearish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Regime = VolatilityRegime.Low,
                Reason = "low-volatility"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowVolatilitySnapshot(TradingSymbol.ETHUSDT));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("Spot bearish signal ignored", result.Reason, StringComparison.OrdinalIgnoreCase);
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
    public async Task LowVolatilityBreakoutBuy_IncludesExpectedTargetFields()
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

        var result = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(TradingSymbol.XRPUSDT));

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.True(result.ExpectedTargetPrice.HasValue);
        Assert.True(result.ExpectedMovePercent.HasValue);
        Assert.True(result.BreakoutRangeHigh.HasValue);
        Assert.True(result.BreakoutRangeLow.HasValue);
        Assert.True(result.BreakoutThresholdPrice.HasValue);
        Assert.Equal("MovingAverageTrendStrategy.LowVolBreakoutExpectedTarget", result.ExpectedTargetSource);
        Assert.True(result.ExpectedTargetPrice.Value >= 100.60m);
        Assert.True(result.ExpectedMovePercent.Value >= 0m);
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
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();

        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: true,
            trendResult: CreateTrend(isBearishConfirmed: true),
            positionManager: positionManager,
            logger: logger);

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Sell, result.Signal);
        Assert.Contains("Exit signal - trend reversal detected for long position.", result.Reason, StringComparison.OrdinalIgnoreCase);
        var reversalExitLog = logger.Entries.FirstOrDefault(x =>
            x.Message.Contains("exit candidate", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                x.Values.TryGetValue("ExitType", out var exitType) ? exitType?.ToString() : null,
                "TrendReversal",
                StringComparison.Ordinal));
        Assert.NotNull(reversalExitLog);
        Assert.True(reversalExitLog!.Values.ContainsKey("EstimatedNetAfterCostPercent"));
        Assert.True(reversalExitLog.Values.ContainsKey("ReversalStrength"));
        Assert.True(reversalExitLog.Values.ContainsKey("BearishCrossover"));
        Assert.True(reversalExitLog.Values.ContainsKey("HighVolatilityWarning"));
        Assert.True(reversalExitLog.Get<bool>("BearishReversalConfirmed"));
        Assert.Equal("Bearish trend reversal detected for long position.", reversalExitLog.Get<string>("ExitAllowedReason"));
    }

    [Fact]
    public async Task SpotExistingLong_MomentumWeakeningTinyRed_NoInvalidation_HoldsWhenNetAwareGuardEnabled()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.ADAUSDT, PositionType.Long, 100m, TrendState.Bullish, DateTime.UtcNow.AddMinutes(-1));
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();

        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: true,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.01m,
                CurrentLongMa = 100.00m,
                PreviousShortMa = 100.01m,
                PreviousLongMa = 100.00m,
                ShortMaSlopePercent = 0.00001m,
                LongMaSlopePercent = 0.00001m,
                TrendStrengthPercent = 0.0002m,
                ConfidenceScore = 70,
                IsBullishTrendConfirmed = true,
                CurrentTrendState = TrendState.Bullish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            logger: logger,
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNetAwareMomentumExit"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitMinTradeAgeMinutes"] = "5",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitAllowIfUnrealizedLossPercentBelow"] = "-0.20",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitRequireBearishConfirmationWhenFeeNegative"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitMinNetProfitPercent"] = "0.10"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshotForPrice(TradingSymbol.ADAUSDT, 99.95m));

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("Holding long position - trend still bullish.", result.Reason, StringComparison.OrdinalIgnoreCase);
        var exitCandidateLog = logger.Entries.FirstOrDefault(x =>
            x.Message.Contains("exit candidate", StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                x.Values.TryGetValue("ExitSignalType", out var exitType) ? exitType?.ToString() : null,
                "MomentumWeakening",
                StringComparison.Ordinal));
        Assert.NotNull(exitCandidateLog);
        Assert.Equal("MomentumWeakening", exitCandidateLog!.Get<string>("ExitSignalType"));
        Assert.Equal(false, exitCandidateLog.Get<bool>("ExitAllowed"));
        Assert.True(exitCandidateLog.Values.ContainsKey("UnrealizedPnLPercent"));
        Assert.True(exitCandidateLog.Values.ContainsKey("EstimatedRoundTripCostPercent"));
        Assert.True(exitCandidateLog.Values.ContainsKey("NetAfterEstimatedCostPercent"));
        Assert.True(exitCandidateLog.Values.ContainsKey("BreakoutThesisInvalidated"));
        Assert.Contains("below development window", exitCandidateLog.Get<string>("ExitBlockedReason"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotExistingLong_MomentumWeakening_BearishReversalConfirmed_Sells()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.DOTUSDT, PositionType.Long, 100m, TrendState.Bullish, DateTime.UtcNow.AddMinutes(-2));

        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: true,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 99.9m,
                CurrentLongMa = 100.0m,
                PreviousShortMa = 100.0m,
                PreviousLongMa = 100.0m,
                ShortMaSlopePercent = -0.00001m,
                LongMaSlopePercent = 0m,
                TrendStrengthPercent = 0.001m,
                ConfidenceScore = 70,
                IsBearishTrendConfirmed = true,
                IsBearishCrossover = true,
                CurrentTrendState = TrendState.Bearish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNetAwareMomentumExit"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshotForPrice(TradingSymbol.DOTUSDT, 99.98m));

        Assert.Equal(TradeSignal.Sell, result.Signal);
        Assert.Contains("trend reversal detected for long position", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SpotExistingLong_MomentumWeakening_NetProfitableAboveFloor_Sells()
    {
        var positionManager = new PositionManager();
        positionManager.Enter(TradingSymbol.SHIBUSDT, PositionType.Long, 100m, TrendState.Bullish, DateTime.UtcNow.AddMinutes(-10));

        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: true,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100.2m,
                CurrentLongMa = 100.1m,
                PreviousShortMa = 100.2m,
                PreviousLongMa = 100.1m,
                ShortMaSlopePercent = 0.00001m,
                LongMaSlopePercent = 0.00001m,
                TrendStrengthPercent = 0.001m,
                ConfidenceScore = 75,
                IsBullishTrendConfirmed = true,
                CurrentTrendState = TrendState.Bullish,
                MarketRegime = MarketRegime.Trending
            },
            positionManager: positionManager,
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNetAwareMomentumExit"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitMinTradeAgeMinutes"] = "5",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitAllowIfUnrealizedLossPercentBelow"] = "-0.20",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitRequireBearishConfirmationWhenFeeNegative"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:MomentumExitMinNetProfitPercent"] = "0.10"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshotForPrice(TradingSymbol.SHIBUSDT, 100.40m));

        Assert.Equal(TradeSignal.Sell, result.Signal);
        Assert.Contains("long momentum is weakening", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntry_PopulatesQualityObservabilityFields()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.NotNull(result.ConsecutiveBullishTrendCandles);
        Assert.True(result.ConsecutiveBullishTrendCandles >= 1);
        Assert.NotNull(result.DistanceToRecentHighPercent);
        Assert.NotNull(result.DistanceToInvalidationPercent);
        Assert.NotNull(result.EntryNearRecentHigh);
        Assert.NotNull(result.CurrentCloseAboveRecentHigh);
        Assert.NotNull(result.PreviousCandleBearish);
        Assert.Null(result.NormalTrendEntryRejectedReason);
    }

    [Fact]
    public async Task NormalTrendEntry_SolLikeChaseNearHigh_BlockedWhenBullishPersistenceFilterEnabled()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 86.38m,
                CurrentLongMa = 86.30m,
                PreviousShortMa = 86.32m,
                PreviousLongMa = 86.29m,
                ShortMaSlopePercent = 0.0007m,
                LongMaSlopePercent = 0.0003m,
                TrendStrengthPercent = 0.0009m,
                ConfidenceScore = 100,
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
                Reason = "trending"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendBullishPersistenceFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinBullishPersistenceCandles"] = "2"
            });

        var result = await strategy.GenerateSignalAsync(CreateSolLikeNearHighSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("consecutive bullish closes", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.ConsecutiveBullishTrendCandles);
        Assert.Equal(1, result.ConsecutiveBullishTrendCandles);
        Assert.True(result.EntryNearRecentHigh);
        Assert.NotNull(result.NormalTrendEntryRejectedReason);
    }

    [Fact]
    public async Task NormalTrendEntry_SolLikeChaseNearHigh_AllowedWhenQualityFiltersDisabled()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 86.38m,
                CurrentLongMa = 86.30m,
                PreviousShortMa = 86.32m,
                PreviousLongMa = 86.29m,
                ShortMaSlopePercent = 0.0007m,
                LongMaSlopePercent = 0.0003m,
                TrendStrengthPercent = 0.0009m,
                ConfidenceScore = 100,
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
                Reason = "trending"
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendBullishPersistenceFilter"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateSolLikeNearHighSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.Contains("bullish trend confirmed", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.EntryNearRecentHigh);
    }

    [Fact]
    public async Task NormalTrendEntry_BlocksWhenExpectedRewardRiskBelowThreshold()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 0.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinExpectedRewardRisk"] = "0.80"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("expected reward:risk", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntry_AllowsWhenExpectedRewardRiskMeetsThreshold()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 2.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinExpectedRewardRisk"] = "0.80",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendUseMinAtrStructureExtension"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateStrongRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.Contains("bullish trend confirmed", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntry_BlocksNearRecentHighWhenRRTooLow()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 0.1m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendNearRecentHighRejection"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresRewardRisk"] = "1.20"
            });

        var result = await strategy.GenerateSignalAsync(CreateNearHighLowRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("near-recent-high", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntry_AllowsNearRecentHighWhenRRStrongEnough()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.8m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendNearRecentHighRejection"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresRewardRisk"] = "1.20",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendUseMinAtrStructureExtension"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateNearHighStrongRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.True(result.EntryNearRecentHigh);
    }

    [Fact]
    public async Task NormalTrendEntry_ExistingFiltersStillWork_WhenRewardRiskFilterEnabled()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 2.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendBullishPersistenceFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinBullishPersistenceCandles"] = "2",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinExpectedRewardRisk"] = "0.10",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendUseMinAtrStructureExtension"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateSolLikeNearHighSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("consecutive bullish closes", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntry_DefaultsDisabled_PreserveCurrentBehavior()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 0.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
    }

    [Fact]
    public async Task NormalTrendEntryQuality_GateOff_UsesCurrentSeriesMode()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        var qualityLog = logger.Entries.First(x => x.Message.Contains("normal trend entry quality", StringComparison.OrdinalIgnoreCase));
        Assert.False(qualityLog.Get<bool>("UseConfirmedClosedCandlesForEntryQuality"));
        Assert.Equal("CurrentSeries", qualityLog.Get<string>("EntryQualityCandleMode"));
    }

    [Fact]
    public async Task NormalTrendEntryQuality_GateOn_IgnoresInProgressLatestClose()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true"
            });

        var snapshot = CreateSnapshot();
        var result = await strategy.GenerateSignalAsync(snapshot);

        Assert.Equal(TradeSignal.Buy, result.Signal);
        var qualityLog = logger.Entries.First(x => x.Message.Contains("normal trend entry quality", StringComparison.OrdinalIgnoreCase));
        Assert.True(qualityLog.Get<bool>("UseConfirmedClosedCandlesForEntryQuality"));
        Assert.Equal("ConfirmedClosed", qualityLog.Get<string>("EntryQualityCandleMode"));
        Assert.Equal(snapshot.LatestClosedCandleClosePrice!.Value, qualityLog.Get<decimal>("EntryQualityLatestClose"));
        Assert.Equal(snapshot.LatestClosedCandleCloseTimeUtc!.Value, qualityLog.Get<DateTime>("EntryQualityLatestCloseTimeUtc"));
    }

    [Fact]
    public async Task NormalTrendEntryQuality_GateOn_UsesConfirmedClosedForCloseAboveRecentHigh()
    {
        var snapshot = CreateCloseAboveRecentHighModeSnapshot();

        var gateOff = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "false"
            });
        var gateOn = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true"
            });

        var offResult = await gateOff.GenerateSignalAsync(snapshot);
        var onResult = await gateOn.GenerateSignalAsync(snapshot);

        Assert.Equal(TradeSignal.Buy, offResult.Signal);
        Assert.Equal(TradeSignal.Hold, onResult.Signal);
        Assert.Contains("close above recent range high", onResult.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntryQuality_GateOn_BullishPersistenceIgnoresInProgressCandle()
    {
        var snapshot = CreateBullishPersistenceModeSnapshot();
        var gateOff = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendBullishPersistenceFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinBullishPersistenceCandles"] = "1",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "false"
            });
        var gateOn = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendBullishPersistenceFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinBullishPersistenceCandles"] = "1",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true"
            });

        var offResult = await gateOff.GenerateSignalAsync(snapshot);
        var onResult = await gateOn.GenerateSignalAsync(snapshot);

        Assert.Equal(TradeSignal.Buy, offResult.Signal);
        Assert.Equal(TradeSignal.Hold, onResult.Signal);
        Assert.Contains("consecutive bullish closes", onResult.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntryQuality_GateOn_PreviousBearishIgnoresInProgressCandle()
    {
        var snapshot = CreatePreviousBearishModeSnapshot();
        var gateOff = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRejectPreviousBearishCandleFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "false"
            });
        var gateOn = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRejectPreviousBearishCandleFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true"
            });

        var offResult = await gateOff.GenerateSignalAsync(snapshot);
        var onResult = await gateOn.GenerateSignalAsync(snapshot);

        Assert.Equal(TradeSignal.Hold, offResult.Signal);
        Assert.Contains("bearish closed candle", offResult.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TradeSignal.Buy, onResult.Signal);
    }

    [Fact]
    public async Task NormalTrendEntry_ExistingFiltersStillWork_WithConfirmedClosedMode()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 0.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinExpectedRewardRisk"] = "0.80",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("expected reward:risk", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalTrendEntry_BlockedWhenRejectPreviousBearishCandleFilterEnabled()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRejectPreviousBearishCandleFilter"] = "true"
            });

        var snapshot = CreateSnapshot();
        var closesWithBearishPrevious = snapshot.ClosePrices.Take(snapshot.ClosePrices.Count - 2)
            .Append(621m)
            .Append(620m)
            .ToArray();
        var bearishPreviousSnapshot = new MarketSnapshot
        {
            Symbol = snapshot.Symbol,
            CurrentPrice = 620m,
            LatestClosedCandleOpenTimeUtc = snapshot.LatestClosedCandleOpenTimeUtc,
            LatestClosedCandleCloseTimeUtc = snapshot.LatestClosedCandleCloseTimeUtc,
            LatestClosedCandleClosePrice = 620m,
            ClosePrices = closesWithBearishPrevious,
            HighPrices = snapshot.HighPrices,
            LowPrices = snapshot.LowPrices,
            Volumes = snapshot.Volumes
        };

        var result = await strategy.GenerateSignalAsync(bearishPreviousSnapshot);

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("bearish closed candle", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(true, result.PreviousCandleBearish);
        Assert.NotNull(result.NormalTrendEntryRejectedReason);
    }

    [Fact]
    public async Task NormalTrendEntry_IncludesExpectedTargetFields()
    {
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.True(result.ExpectedTargetPrice.HasValue);
        Assert.True(result.ExpectedMovePercent.HasValue);
        Assert.Equal("MovingAverageTrendStrategy.NormalTrendExpectedTarget", result.ExpectedTargetSource);
        Assert.True(result.ExpectedTargetPrice.Value >= 620m);
        Assert.True(result.ExpectedMovePercent.Value >= 0m);
    }

    [Fact]
    public async Task NormalTrendExpectedTarget_LogsMaxAtrStructureProjectionDetails_WhenConfigured()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: positionManager,
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendAtrExtensionMultiplier"] = "0.75",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendStructureExtensionMultiplier"] = "0.50",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendExpectedTargetLookbackCandles"] = "12",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendUseMinAtrStructureExtension"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        var targetLog = logger.Entries.FirstOrDefault(x =>
            x.Message.Contains("normal trend expected target", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(targetLog);

        Assert.Equal("MaxAtrStructure", targetLog!.Get<string>("ProjectionMode"));
        var atrExtension = targetLog.Get<decimal>("AtrExtension");
        var structureExtension = targetLog.Get<decimal>("StructureExtension");
        var projectedExtension = targetLog.Get<decimal>("ProjectedExtension");
        Assert.Equal(Math.Max(atrExtension, structureExtension), projectedExtension);
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

    [Fact]
    public async Task ReadOnlyEvaluation_BuySignal_DoesNotMutatePositionState()
    {
        var symbol = TradingSymbol.BNBUSDT;
        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Normal
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false"
            });

        var result = await strategy.GenerateSignalAsync(CreateSnapshot(), CancellationToken.None, allowStateMutation: false);
        var state = positionManager.GetState(symbol);

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.False(state.IsInPosition);
        Assert.Equal(PositionType.None, state.PositionType);
    }

    [Fact]
    public async Task ReadOnlyEvaluation_WithOpenPosition_DoesNotClearPendingBreakoutState()
    {
        var symbol = TradingSymbol.XRPUSDT;
        RemovePendingBreakout(symbol);

        var positionManager = new PositionManager();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                MarketRegime = MarketRegime.LowVolatility,
                ConfidenceScore = 70,
                CurrentTrendState = TrendState.Bullish,
                IsBullishTrendConfirmed = true,
                ShortMaSlopePercent = 0.002m,
                TrendStrengthPercent = 0.01m,
                CurrentShortMa = 100.25m,
                CurrentLongMa = 100.15m,
                PreviousShortMa = 100.10m,
                PreviousLongMa = 100.05m
            },
            positionManager: positionManager,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Low
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireBreakoutConfirmation"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles"] = "2"
            });

        var baselineCandle = DateTime.UtcNow.AddMinutes(-2);
        _ = await strategy.GenerateSignalAsync(CreateBreakoutSnapshot(symbol, baselineCandle));
        Assert.True(HasPendingBreakout(symbol));

        positionManager.Enter(symbol, PositionType.Long, 100.50m, TrendState.Bullish, DateTime.UtcNow.AddMinutes(-1));
        _ = await strategy.GenerateSignalAsync(
            CreateLowVolatilitySnapshot(symbol, baselineCandle.AddMinutes(1)),
            CancellationToken.None,
            allowStateMutation: false);

        Assert.True(HasPendingBreakout(symbol));

        RemovePendingBreakout(symbol);
    }

    [Fact]
    public async Task PullbackContinuationOverride_DefaultsOff_PreservesCloseAboveRecentHighRejection()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateTrend(isBearishConfirmed: false),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateCloseAboveRecentHighModeSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("close above recent range high", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LowVolBreakout_ConfirmedClosedMode_UsesClosedCandleForLatestPreviousAndRange()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
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
            positionManager: new PositionManager(),
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
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "3",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireBreakoutConfirmation"] = "false"
            });

        _ = await strategy.GenerateSignalAsync(CreateBreakoutConfirmedClosedModeSnapshot());

        var breakoutLog = logger.Entries.First(x => x.Message.Contains("low-volatility breakout diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.True(breakoutLog.Get<bool>("UseConfirmedClosedCandlesForLowVolBreakout"));
        Assert.Equal("ConfirmedClosed", breakoutLog.Get<string>("BreakoutCandleMode"));
        Assert.Equal(104m, breakoutLog.Get<decimal>("BreakoutLatestClose"));
        Assert.Equal(103m, breakoutLog.Get<decimal>("BreakoutRangeHigh"));
        Assert.Equal(100m, breakoutLog.Get<decimal>("BreakoutRangeLow"));
        Assert.Equal(3, breakoutLog.Get<int>("BreakoutRangeWindowCandleCount"));
    }

    [Fact]
    public async Task PullbackContinuationOverride_DoesNotApply_WhenRangingRejected()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: new TrendAnalysisResult
            {
                IsValid = true,
                CurrentShortMa = 100m,
                CurrentLongMa = 99.8m,
                PreviousShortMa = 99.9m,
                PreviousLongMa = 99.7m,
                ShortMaSlopePercent = 0.001m,
                LongMaSlopePercent = 0.0008m,
                TrendStrengthPercent = 0.002m,
                ConfidenceScore = 70,
                CurrentTrendState = TrendState.Bullish,
                IsBullishTrendConfirmed = true,
                MarketRegime = MarketRegime.Ranging
            },
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 80,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateCloseAboveRecentHighModeSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("ranging market", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PullbackContinuationOverride_DoesNotApply_WhenRewardRiskBelowOverrideThreshold()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateBullishPullbackTrend(),
            positionManager: new PositionManager(),
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 0.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "0.90"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        var rejectionLog = logger.Entries.First(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.True(rejectionLog.Get<bool>("PullbackContinuationOverrideEvaluated"));
        Assert.False(rejectionLog.Get<bool>("PullbackContinuationOverrideAllowed"));
        Assert.Contains("expected reward:risk", rejectionLog.Get<string>("PullbackContinuationOverrideRejectedReason"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PullbackContinuationOverride_DoesNotApply_WhenDistanceToInvalidationTooSmall()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateBullishPullbackTrend(),
            positionManager: new PositionManager(),
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "0.20",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "2.00"
            });

        var result = await strategy.GenerateSignalAsync(CreateStrongRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        var rejectionLog = logger.Entries.First(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.False(rejectionLog.Get<bool>("PullbackContinuationOverrideAllowed"));
        Assert.Contains("distance to invalidation", rejectionLog.Get<string>("PullbackContinuationOverrideRejectedReason"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PullbackContinuationOverride_DoesNotApply_WhenPreviousConfirmedCandleBearish_AndRejectFlagEnabled()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateBullishPullbackTrend(),
            positionManager: new PositionManager(),
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "0.20",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackRejectPreviousBearishCandle"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreatePullbackPreviousBearishConfirmedSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        var rejectionLog = logger.Entries.First(x => x.Message.Contains("entry rejected", StringComparison.OrdinalIgnoreCase));
        Assert.False(rejectionLog.Get<bool>("PullbackContinuationOverrideAllowed"));
        Assert.Contains("rejects immediate bearish", rejectionLog.Get<string>("PullbackContinuationOverrideRejectedReason"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PullbackContinuationOverride_AllowsEntry_WhenCloseAboveRecentHighIsOnlyBlocker()
    {
        var logger = new CapturingLogger<MovingAverageTrendStrategy>();
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateBullishPullbackTrend(),
            positionManager: new PositionManager(),
            logger: logger,
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 1.0m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "0.40"
            });

        var result = await strategy.GenerateSignalAsync(CreateStrongRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Buy, result.Signal);
        Assert.Contains("pullback continuation override confirmed", result.Reason, StringComparison.OrdinalIgnoreCase);
        var qualityLog = logger.Entries.Last(x => x.Message.Contains("normal trend entry quality", StringComparison.OrdinalIgnoreCase));
        Assert.True(qualityLog.Get<bool>("PullbackContinuationOverrideAllowed"));
    }

    [Fact]
    public async Task PullbackContinuationOverride_DoesNotBypassExistingNormalTrendRewardRiskFilter()
    {
        var strategy = CreateStrategy(
            tradingMode: TradingMode.Spot,
            allowShortSelling: false,
            trendResult: CreateBullishPullbackTrend(),
            positionManager: new PositionManager(),
            marketConditionResult: new MarketConditionResult
            {
                IsValid = true,
                AllowTrade = true,
                MarketConditionScore = 85,
                Reason = "ok",
                Regime = VolatilityRegime.Normal,
                Atr = 0.2m
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendCloseAboveRecentHighFilter"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinExpectedRewardRisk"] = "1.20",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true"
            });

        var result = await strategy.GenerateSignalAsync(CreateLowRewardRiskSnapshot());

        Assert.Equal(TradeSignal.Hold, result.Signal);
        Assert.Contains("expected reward:risk", result.Reason, StringComparison.OrdinalIgnoreCase);
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
                ["DecisionEngine:MovingAverageCrossoverStrategy:RequireNoImmediateBearishCandleAfterBreakout"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendFallbackWhenLowVolBreakoutFails"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "0.80",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackRequireCloseAboveShortAndLongMa"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackRequirePositiveShortSlope"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackRejectPreviousBearishCandle"] = "true"
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

    private static MarketSnapshot CreateSnapshotForPrice(TradingSymbol symbol, decimal currentPrice)
    {
        var snapshot = CreateSnapshot();
        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = currentPrice,
            LatestClosedCandleOpenTimeUtc = snapshot.LatestClosedCandleOpenTimeUtc,
            LatestClosedCandleCloseTimeUtc = snapshot.LatestClosedCandleCloseTimeUtc,
            LatestClosedCandleClosePrice = snapshot.LatestClosedCandleClosePrice,
            ClosePrices = snapshot.ClosePrices,
            HighPrices = snapshot.HighPrices,
            LowPrices = snapshot.LowPrices,
            Volumes = snapshot.Volumes
        };
    }

    private static MarketSnapshot CreateCloseAboveRecentHighModeSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 110m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 104m,
            ClosePrices = [100m, 101m, 102m, 103m, 104m, 110m],
            HighPrices = [101m, 102m, 103m, 104m, 105m, 111m],
            LowPrices = [99m, 100m, 101m, 102m, 103m, 109m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 12m]
        };
    }

    private static MarketSnapshot CreateBullishPersistenceModeSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 97m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 96m,
            ClosePrices = [100m, 99m, 98m, 97m, 96m, 97m],
            HighPrices = [101m, 100m, 99m, 98m, 97m, 98m],
            LowPrices = [99m, 98m, 97m, 96m, 95m, 96m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 11m]
        };
    }

    private static MarketSnapshot CreatePreviousBearishModeSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 100m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 101m,
            ClosePrices = [97m, 98m, 99m, 100m, 101m, 100m],
            HighPrices = [98m, 99m, 100m, 101m, 102m, 101m],
            LowPrices = [96m, 97m, 98m, 99m, 100m, 99m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 11m]
        };
    }

    private static MarketSnapshot CreateSolLikeNearHighSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.SOLUSDT,
            CurrentPrice = 86.42m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 86.42m,
            ClosePrices =
            [
                86.10m, 86.12m, 86.11m, 86.14m, 86.13m, 86.16m, 86.15m, 86.18m,
                86.20m, 86.22m, 86.21m, 86.24m, 86.26m, 86.28m, 86.30m, 86.29m, 86.42m
            ],
            HighPrices =
            [
                86.14m, 86.16m, 86.15m, 86.18m, 86.17m, 86.20m, 86.19m, 86.22m,
                86.24m, 86.26m, 86.25m, 86.28m, 86.30m, 86.32m, 86.34m, 86.40m, 86.43m
            ],
            LowPrices =
            [
                86.06m, 86.08m, 86.07m, 86.10m, 86.09m, 86.12m, 86.11m, 86.14m,
                86.16m, 86.18m, 86.17m, 86.20m, 86.22m, 86.24m, 86.28m, 86.26m, 86.38m
            ],
            Volumes = Enumerable.Repeat(100m, 17).ToArray()
        };
    }

    private static MarketSnapshot CreateLowRewardRiskSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 100.00m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 100.00m,
            ClosePrices = [99.70m, 99.80m, 99.85m, 99.90m, 99.95m, 100.00m],
            HighPrices = [100.00m, 100.01m, 100.02m, 100.01m, 100.02m, 100.02m],
            LowPrices = [99.00m, 99.05m, 99.10m, 99.15m, 99.20m, 99.20m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 10m]
        };
    }

    private static MarketSnapshot CreateStrongRewardRiskSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 100.00m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 100.00m,
            ClosePrices = [99.80m, 99.85m, 99.90m, 99.95m, 100.00m, 100.00m],
            HighPrices = [100.20m, 100.40m, 100.60m, 101.20m, 102.00m, 103.00m],
            LowPrices = [99.60m, 99.62m, 99.64m, 99.65m, 99.66m, 99.67m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 10m]
        };
    }

    private static MarketSnapshot CreateBreakoutConfirmedClosedModeSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 150m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 104m,
            ClosePrices = [100m, 101m, 102m, 103m, 104m, 150m],
            HighPrices = [101m, 102m, 103m, 104m, 105m, 151m],
            LowPrices = [99m, 100m, 101m, 102m, 103m, 149m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 12m]
        };
    }

    private static MarketSnapshot CreatePullbackPreviousBearishConfirmedSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 100m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 100m,
            ClosePrices = [101m, 102m, 103m, 102m, 100m, 101m],
            HighPrices = [101.5m, 102.5m, 103.5m, 102.5m, 100.5m, 101.5m],
            LowPrices = [100.5m, 101.5m, 102.5m, 101.5m, 99.5m, 100.5m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 10m]
        };
    }

    private static MarketSnapshot CreateNearHighLowRewardRiskSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 100.00m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 100.00m,
            ClosePrices = [99.80m, 99.85m, 99.90m, 99.95m, 100.00m, 100.00m],
            HighPrices = [100.02m, 100.04m, 100.06m, 100.08m, 100.10m, 100.10m],
            LowPrices = [99.00m, 99.02m, 99.04m, 99.06m, 99.08m, 99.10m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 10m]
        };
    }

    private static MarketSnapshot CreateNearHighStrongRewardRiskSnapshot()
    {
        var closedCandleTimeUtc = DateTime.UtcNow.AddMinutes(-1);
        return new MarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            CurrentPrice = 100.00m,
            LatestClosedCandleOpenTimeUtc = closedCandleTimeUtc.AddMinutes(-1),
            LatestClosedCandleCloseTimeUtc = closedCandleTimeUtc,
            LatestClosedCandleClosePrice = 100.00m,
            ClosePrices = [99.90m, 99.92m, 99.94m, 99.96m, 99.98m, 100.00m],
            HighPrices = [100.02m, 100.04m, 100.06m, 100.08m, 100.10m, 100.10m],
            LowPrices = [99.70m, 99.72m, 99.74m, 99.75m, 99.76m, 99.78m],
            Volumes = [10m, 10m, 10m, 10m, 10m, 10m]
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

    private static TrendAnalysisResult CreateBullishPullbackTrend()
    {
        return new TrendAnalysisResult
        {
            IsValid = true,
            CurrentShortMa = 99.80m,
            CurrentLongMa = 99.60m,
            PreviousShortMa = 99.70m,
            PreviousLongMa = 99.55m,
            ShortMaSlopePercent = 0.0012m,
            LongMaSlopePercent = 0.0005m,
            TrendStrengthPercent = 0.0020m,
            ConfidenceScore = 70,
            CurrentTrendState = TrendState.Bullish,
            IsBullishTrendConfirmed = true,
            MarketRegime = MarketRegime.Trending
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

    private static bool HasPendingBreakout(TradingSymbol symbol)
    {
        var dictionary = GetPendingBreakoutDictionary();
        return dictionary.Contains(symbol);
    }

    private static void RemovePendingBreakout(TradingSymbol symbol)
    {
        var dictionary = GetPendingBreakoutDictionary();
        if (dictionary.Contains(symbol))
            dictionary.Remove(symbol);
    }

    private static IDictionary GetPendingBreakoutDictionary()
    {
        var field = typeof(MovingAverageTrendStrategy).GetField("PendingBreakouts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        return (IDictionary)field!.GetValue(null)!;
    }
}
