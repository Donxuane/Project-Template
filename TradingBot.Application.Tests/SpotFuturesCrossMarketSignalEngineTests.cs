using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.SpotFuturesCrossMarket;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using Xunit;

namespace TradingBot.Application.Tests;

public class SpotFuturesCrossMarketSignalEngineTests
{
    [Fact]
    public void EntryQualityDisabled_PreservesPreviousLongEntryBehavior()
    {
        var engine = CreateEngine(BullishTrend(), BullishTrend());
        var settings = BaseSettings() with
        {
            EnableEntryQualityFilters = false
        };
        var snapshot = Snapshot(
            spotCloses: [100m, 101m, 102m, 103m, 102.90m],
            futuresCloses: [100m, 101m, 102m, 103m, 102.90m]);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.OpenLong, decision.Action);
        Assert.DoesNotContain("EntryQuality", decision.Reason);
    }

    [Fact]
    public void EntryQualityEnabled_RejectsLongWhenLatestClosedCandlesDoNotConfirmDirection()
    {
        var engine = CreateEngine(BullishTrend(), BullishTrend());
        var settings = QualitySettings();
        var snapshot = Snapshot(
            spotCloses: [100m, 101m, 102m, 103m, 102.90m],
            futuresCloses: [100m, 101m, 102m, 103m, 102.90m]);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.NoTrade, decision.Action);
        Assert.Contains("EntryDirectionNotConfirmed", decision.Reason);
    }

    [Fact]
    public void EntryQualityEnabled_RejectsShortWhenSpotMomentumIsTooWeak()
    {
        var engine = CreateEngine(BearishTrend(), BearishTrend());
        var settings = QualitySettings();
        var snapshot = Snapshot(
            spotCloses: [100m, 100.05m, 100.02m, 99.98m, 99.95m],
            futuresCloses: [100m, 100.05m, 100.02m, 99.98m, 99.95m]);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.NoTrade, decision.Action);
        Assert.Contains("EntryMomentumTooWeak", decision.Reason);
    }

    [Fact]
    public void EntryQualityEnabled_RejectsShortAfterOversizedClosedCandleIntoRecentLow()
    {
        var engine = CreateEngine(BearishTrend(), BearishTrend());
        var settings = QualitySettings();
        var snapshot = Snapshot(
            spotCloses: [102m, 101.80m, 101.50m, 101.20m, 100.60m],
            futuresCloses: [102m, 101.80m, 101.50m, 101.20m, 100.50m, 99.70m],
            futuresLows: [101.80m, 101.50m, 101.10m, 100.40m, 100.20m, 99.60m],
            futuresHighs: [102.20m, 102.00m, 101.80m, 101.40m, 100.80m, 99.90m]);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.NoTrade, decision.Action);
        Assert.Contains("EntryExhaustedNearRecentLow", decision.Reason);
    }

    [Fact]
    public void EntryQualityEnabled_AllowsShortWithDirectionalConfirmationAndRoomToContinue()
    {
        var engine = CreateEngine(BearishTrend(), BearishTrend());
        var settings = QualitySettings();
        var snapshot = Snapshot(
            spotCloses: [101m, 100.80m, 100.60m, 100.40m, 100.10m],
            futuresCloses: [101m, 100.80m, 100.60m, 100.40m, 100.10m]);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.OpenShort, decision.Action);
        Assert.Contains("EntryQualityConfirmed", decision.Reason);
    }

    private static SpotFuturesCrossMarketSignalEngine CreateEngine(params TrendAnalysisResult[] trendResults)
        => new(
            new FakeTrendStateService(trendResults),
            new FakeAtrService(),
            NullLogger<SpotFuturesCrossMarketSignalEngine>.Instance);

    private static SpotFuturesCrossMarketSettings BaseSettings()
        => new()
        {
            Symbol = TradingSymbol.BNBUSDT,
            Symbols = [TradingSymbol.BNBUSDT],
            Interval = "15m",
            ShortMaPeriod = 7,
            LongMaPeriod = 25,
            MomentumLookbackCandles = 4,
            MinEntryTrendConfidenceScore = 45,
            MaxAbsFundingRateForEntry = 0.0008m,
            MaxAbsBasisPercentForEntry = 1.0m,
            AtrStopMultiplier = 1.6m,
            AtrTargetMultiplier = 2.4m,
            MinStopPercent = 0.35m,
            MaxStopPercent = 2.0m,
            FeeAndSpreadPercent = 0.15m,
            MinNetExpectedMovePercent = 0.10m
        };

    private static SpotFuturesCrossMarketSettings QualitySettings()
        => BaseSettings() with
        {
            EnableEntryQualityFilters = true,
            RequireEntryClosedCandleDirectionConfirmation = true,
            MinEntrySpotMomentumAbsPercent = 0.10m,
            EntryExhaustionLookbackCandles = 8,
            EntryExhaustionExtremeZonePercent = 15m,
            EntryExhaustionMinMovePercent = 0.50m
        };

    private static CrossMarketSnapshot Snapshot(
        IReadOnlyList<decimal> spotCloses,
        IReadOnlyList<decimal> futuresCloses,
        IReadOnlyList<decimal>? futuresLows = null,
        IReadOnlyList<decimal>? futuresHighs = null)
    {
        var spot = Market(TradingSymbol.BNBUSDT, spotCloses);
        var futures = Market(
            TradingSymbol.BNBUSDT,
            futuresCloses,
            lows: futuresLows,
            highs: futuresHighs);

        return new CrossMarketSnapshot
        {
            Symbol = TradingSymbol.BNBUSDT,
            Interval = "15m",
            CandleOpenTimeUtc = new DateTime(2026, 7, 13, 13, 30, 0, DateTimeKind.Utc),
            CandleCloseTimeUtc = new DateTime(2026, 7, 13, 13, 44, 59, 999, DateTimeKind.Utc),
            MarketsInSync = true,
            Spot = spot,
            Futures = futures,
            SpotClose = spotCloses[^1],
            FuturesClose = futuresCloses[^1],
            BasisPercent = 0m,
            FundingRate = 0m,
            MarkPrice = futuresCloses[^1]
        };
    }

    private static MarketSnapshot Market(
        TradingSymbol symbol,
        IReadOnlyList<decimal> closes,
        IReadOnlyList<decimal>? lows = null,
        IReadOnlyList<decimal>? highs = null)
    {
        highs ??= closes.Select(c => c + 0.20m).ToArray();
        lows ??= closes.Select(c => c - 0.20m).ToArray();

        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = closes[^1],
            CurrentPriceSource = "ClosedCandle",
            CurrentPriceAsOfUtc = DateTime.UtcNow,
            ClosePrices = closes,
            HighPrices = highs,
            LowPrices = lows,
            Volumes = closes.Select(_ => 100m).ToArray()
        };
    }

    private static TrendAnalysisResult BullishTrend() => new()
    {
        IsValid = true,
        CurrentTrendState = TrendState.Bullish,
        IsBullishTrendConfirmed = true,
        ConfidenceScore = 80,
        ShortMaSlopePercent = 0.001m,
        TrendStrengthPercent = 0.002m
    };

    private static TrendAnalysisResult BearishTrend() => new()
    {
        IsValid = true,
        CurrentTrendState = TrendState.Bearish,
        IsBearishTrendConfirmed = true,
        ConfidenceScore = 80,
        ShortMaSlopePercent = -0.001m,
        TrendStrengthPercent = 0.002m
    };

    private sealed class FakeTrendStateService(IEnumerable<TrendAnalysisResult> results) : ITrendStateService
    {
        private readonly Queue<TrendAnalysisResult> _results = new(results);

        public int GetRequiredPeriods(int shortPeriod, int longPeriod) => 2;

        public TrendAnalysisResult Analyze(MarketSnapshot marketData, int shortPeriod, int longPeriod)
            => _results.Dequeue();
    }

    private sealed class FakeAtrService : IAtrService
    {
        public int RequiredPeriods => 2;

        public decimal Calculate(
            IReadOnlyList<decimal> highs,
            IReadOnlyList<decimal> lows,
            IReadOnlyList<decimal> closes,
            bool normalize,
            decimal currentPrice) => 0.003m;
    }
}
