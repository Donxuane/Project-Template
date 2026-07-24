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
    public void NormalMode_MisalignedSpotAndFuturesRegimes_ReturnsNoTrade()
    {
        var engine = CreateEngine();
        var snapshot = Snapshot(
            spotCloses: Series(60, 94m, 0.10m),
            futuresCloses: Series(60, 106m, -0.10m));

        var decision = engine.Evaluate(BaseSettings(), snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.NoTrade, decision.Action);
        Assert.Contains("NoQualifiedTrendEntry", decision.Reason);
    }

    [Fact]
    public void EvidenceMode_NormalSetupRejected_ProducesLabelledTestnetEntry()
    {
        var engine = CreateEngine();
        var settings = BaseSettings() with { EnableTestnetEvidenceEntries = true };
        var snapshot = Snapshot(
            spotCloses: Series(60, 94m, 0.10m),
            futuresCloses: Series(60, 106m, -0.10m));

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Contains(decision.Action, new[] { CrossMarketAction.OpenLong, CrossMarketAction.OpenShort });
        Assert.Contains("TESTNET_EVIDENCE_FALLBACK", decision.Reason);
        Assert.NotNull(decision.StopLossPrice);
        Assert.NotNull(decision.TakeProfitPrice);
    }

    [Fact]
    public void EvidenceMode_QualifiedStrategySetup_IsNotRelabelledAsFallback()
    {
        var engine = CreateEngine();
        var settings = BaseSettings() with
        {
            EnableTestnetEvidenceEntries = true,
            LongRsiMax = 100m
        };
        var closes = Series(60, 88m, 0.20m);

        var decision = engine.Evaluate(settings, Snapshot(closes, closes), openPositionSide: null);

        Assert.Equal(CrossMarketAction.OpenLong, decision.Action);
        Assert.DoesNotContain("TESTNET_EVIDENCE_FALLBACK", decision.Reason);
        Assert.Contains("regime confirmed", decision.Reason);
    }

    [Fact]
    public void EvidenceMode_OutOfSyncData_RemainsBlocked()
    {
        var engine = CreateEngine();
        var settings = BaseSettings() with { EnableTestnetEvidenceEntries = true };
        var snapshot = new CrossMarketSnapshot
        {
            Symbol = TradingSymbol.BTCUSDT,
            MarketsInSync = false,
            SyncIssue = "test"
        };

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.NoTrade, decision.Action);
        Assert.Contains("MarketsOutOfSync", decision.Reason);
    }

    [Fact]
    public void EvidenceMode_DislocatedBasis_RemainsBlocked()
    {
        var engine = CreateEngine();
        var settings = BaseSettings() with { EnableTestnetEvidenceEntries = true };
        var snapshot = Snapshot(
            spotCloses: Series(60, 94m, 0.10m),
            futuresCloses: Series(60, 106m, -0.10m),
            basisPercent: 1.01m);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.NoTrade, decision.Action);
        Assert.Contains("BasisDislocation", decision.Reason);
    }

    [Fact]
    public void EvidenceMode_UsesSeparateBasisToleranceOnlyForFallbackProbe()
    {
        var engine = CreateEngine();
        var settings = BaseSettings() with
        {
            EnableTestnetEvidenceEntries = true,
            TestnetEvidenceMaxAbsBasisPercent = 5m
        };
        var snapshot = Snapshot(
            spotCloses: Series(60, 94m, 0.10m),
            futuresCloses: Series(60, 106m, -0.10m),
            basisPercent: 1.40m);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Contains(decision.Action, new[] { CrossMarketAction.OpenLong, CrossMarketAction.OpenShort });
        Assert.Contains("TESTNET_EVIDENCE_FALLBACK", decision.Reason);
    }

    [Fact]
    public void EvidenceMode_QualifiedStrategyEntry_KeepsNormalBasisLimit()
    {
        var engine = CreateEngine();
        var settings = BaseSettings() with
        {
            EnableTestnetEvidenceEntries = true,
            TestnetEvidenceMaxAbsBasisPercent = 5m,
            LongRsiMax = 100m
        };
        var closes = Series(60, 88m, 0.20m);

        var decision = engine.Evaluate(
            settings,
            Snapshot(closes, closes, basisPercent: 1.40m),
            openPositionSide: null);

        Assert.Equal(CrossMarketAction.NoTrade, decision.Action);
        Assert.Contains("mode=strategy", decision.Reason);
    }

    [Fact]
    public void EvidenceMode_ExtremePositiveFunding_SelectsShortProbe()
    {
        var engine = CreateEngine();
        var settings = BaseSettings() with { EnableTestnetEvidenceEntries = true };
        var closes = Series(60, 88m, 0.20m);
        var snapshot = Snapshot(closes, closes, fundingRate: settings.MaxAbsFundingRateForEntry + 0.0001m);

        var decision = engine.Evaluate(settings, snapshot, openPositionSide: null);

        Assert.Equal(CrossMarketAction.OpenShort, decision.Action);
        Assert.Contains("TESTNET_EVIDENCE_FALLBACK", decision.Reason);
    }

    private static SpotFuturesCrossMarketSignalEngine CreateEngine()
        => new(
            new FakeTrendStateService(),
            new FakeAtrService(),
            NullLogger<SpotFuturesCrossMarketSignalEngine>.Instance);

    private static SpotFuturesCrossMarketSettings BaseSettings()
        => new()
        {
            Symbol = TradingSymbol.BTCUSDT,
            Symbols = [TradingSymbol.BTCUSDT],
            Interval = "1m",
            RegimeInterval = "15m",
            ShortMaPeriod = 7,
            LongMaPeriod = 20,
            MomentumLookbackCandles = 3,
            RegimeShortMaPeriod = 12,
            RegimeLongMaPeriod = 36,
            MinRegimeAdx = 10m,
            RsiPeriod = 14,
            LongRsiMin = 42m,
            LongRsiMax = 82m,
            ShortRsiMin = 18m,
            ShortRsiMax = 58m,
            EntryBreakoutLookbackCandles = 8,
            EntryPullbackLookbackCandles = 4,
            MinEntryVolumeRatio = 0.20m,
            MinLongTakerBuyRatio = 0.48m,
            MaxShortTakerBuyRatio = 0.52m,
            MaxEntryExtensionAtr = 3m,
            MinRewardRiskRatio = 1.60m,
            RequireEntryClosedCandleDirectionConfirmation = false,
            RequireMicrostructureConfirmation = false,
            MaxAbsFundingRateForEntry = 0.0008m,
            MaxAbsBasisPercentForEntry = 1m,
            TestnetEvidenceMaxAbsBasisPercent = 1m,
            AtrStopMultiplier = 1.4m,
            MinStopPercent = 0.20m,
            MaxStopPercent = 1m,
            FeeAndSpreadPercent = 0.10m,
            MinNetExpectedMovePercent = 0.05m
        };

    private static CrossMarketSnapshot Snapshot(
        IReadOnlyList<decimal> spotCloses,
        IReadOnlyList<decimal> futuresCloses,
        decimal basisPercent = 0m,
        decimal? fundingRate = 0m)
    {
        var spot = Market(TradingSymbol.BTCUSDT, spotCloses);
        var futures = Market(TradingSymbol.BTCUSDT, futuresCloses);

        return new CrossMarketSnapshot
        {
            Symbol = TradingSymbol.BTCUSDT,
            Interval = "1m",
            CandleOpenTimeUtc = new DateTime(2026, 7, 20, 10, 0, 0, DateTimeKind.Utc),
            CandleCloseTimeUtc = new DateTime(2026, 7, 20, 10, 0, 59, DateTimeKind.Utc),
            MarketsInSync = true,
            Spot = spot,
            Futures = futures,
            RegimeSpot = spot,
            RegimeFutures = futures,
            SpotClose = spotCloses[^1],
            FuturesClose = futuresCloses[^1],
            BasisPercent = basisPercent,
            FundingRate = fundingRate,
            MarkPrice = futuresCloses[^1]
        };
    }

    private static MarketSnapshot Market(TradingSymbol symbol, IReadOnlyList<decimal> closes)
        => new()
        {
            Symbol = symbol,
            CurrentPrice = closes[^1],
            CurrentPriceSource = "ClosedCandle",
            CurrentPriceAsOfUtc = DateTime.UtcNow,
            OpenPrices = closes.Select(c => c - 0.02m).ToArray(),
            HighPrices = closes.Select(c => c + 0.10m).ToArray(),
            LowPrices = closes.Select(c => c - 0.10m).ToArray(),
            ClosePrices = closes,
            Volumes = closes.Select(_ => 100m).ToArray(),
            TakerBuyBaseVolumes = closes.Select(_ => 50m).ToArray()
        };

    private static IReadOnlyList<decimal> Series(int count, decimal start, decimal step)
        => Enumerable.Range(0, count).Select(i => start + i * step).ToArray();

    private sealed class FakeTrendStateService : ITrendStateService
    {
        public int GetRequiredPeriods(int shortPeriod, int longPeriod) => 2;

        public TrendAnalysisResult Analyze(MarketSnapshot marketData, int shortPeriod, int longPeriod)
            => new()
            {
                IsValid = true,
                CurrentTrendState = TrendState.Neutral,
                ConfidenceScore = 60
            };
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
