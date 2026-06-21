using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class LongShortFuturesFeasibilityStudyV1Tests
{
    [Fact]
    public void ExpectedNetPercent_TargetBeforeStop_SubtractsCost()
    {
        var net = LongShortFuturesFeasibilityStudyV1CostModel.ExpectedNetPercent(
            targetBeforeStop: true,
            stopBeforeTarget: false,
            horizonReturnPercent: 0.10m,
            targetPercent: 0.50m,
            stopPercent: 0.50m,
            totalCostPercent: 0.18m);

        Assert.Equal(0.32m, net);
    }

    [Fact]
    public void ResolveScenarioForTradeMode_MapsSpotAndFutures()
    {
        var spot = LongShortFuturesFeasibilityStudyV1CostModel.ResolveScenarioForTradeMode(LongShortTradeMode.SpotLongOnly);
        var futures = LongShortFuturesFeasibilityStudyV1CostModel.ResolveScenarioForTradeMode(LongShortTradeMode.FuturesLongOnly);

        Assert.Equal("spot-conservative", spot.Label);
        Assert.Equal("futures-moderate", futures.Label);
    }

    [Fact]
    public void SimulatePair_ShortDirection_UsesInvertedPrices()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<KlineCandle>
        {
            new(TradingSymbol.BNBUSDT, start, 100m, 100.2m, 99.8m, 100m, 1m),
            new(TradingSymbol.BNBUSDT, start.AddMinutes(1), 100m, 100.6m, 99.4m, 99.5m, 1m)
        };

        var batch = LongShortFuturesFeasibilityStudyV1Scanner.ScanSymbolInterval(
            TradingSymbol.BNBUSDT,
            "5m",
            "30d",
            BuildIntervalCandles(start, 120),
            candles,
            btcContext: null,
            marketWideContext: null,
            CancellationToken.None);

        Assert.NotEmpty(batch.Observations);
        Assert.NotEmpty(batch.TargetStopMatrix);
        Assert.True(batch.Observations.Any(o => o.ShortExpectedNetFuturesModeratePercent != o.LongExpectedNetFuturesModeratePercent)
            || batch.Observations.All(o => o.LongTarget050BeforeStop050 == o.ShortTarget050BeforeStop050));
    }

    [Fact]
    public void BuildResearchAnswers_CoversLongShortQuestions()
    {
        var observations = Enumerable.Range(0, 250)
            .Select(i => SampleObservation(i < 120 ? "30d" : i < 200 ? "60d" : "90d", i))
            .ToArray();

        var summary = LongShortFuturesFeasibilityStudyV1Aggregator.BuildSummary(observations);
        var symbolRanking = LongShortFuturesFeasibilityStudyV1Aggregator.BuildSymbolIntervalRanking(observations);
        var regimeRanking = LongShortFuturesFeasibilityStudyV1Aggregator.BuildRegimeRanking(observations);
        var matrix = new[]
        {
            new LongShortTargetStopMatrixRow
            {
                WindowLabel = "90d",
                Symbol = TradingSymbol.BNBUSDT,
                Interval = "15m",
                Direction = LongShortDirection.Short,
                CostScenarioLabel = "futures-moderate",
                TargetPercent = 0.50m,
                StopPercent = 0.50m,
                SampleCount = 250,
                TargetBeforeStopCount = 40,
                StopBeforeTargetCount = 170,
                TargetBeforeStopRate = 0.16m,
                MedianExpectedNetPercent = -0.12m
            }
        };
        var costSensitivity = LongShortFuturesFeasibilityStudyV1Aggregator.BuildCostSensitivity(observations);
        var rules = LongShortFuturesFeasibilityStudyV1Aggregator.BuildEntryTimeRules(observations);
        var answers = LongShortFuturesFeasibilityStudyV1Aggregator.BuildResearchAnswers(
            observations, summary, symbolRanking, regimeRanking, matrix, costSensitivity, rules);

        Assert.Equal(8, answers.Count);
        Assert.Contains(answers, a => a.Question.Contains("short-side simulation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("pause candle-rule", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("5m", 240)]
    [InlineData("15m", 480)]
    [InlineData("30m", 720)]
    public void ResolvePrimaryHorizonMinutes_MapsIntervals(string interval, int expectedMinutes)
    {
        Assert.Equal(expectedMinutes, LongShortFuturesFeasibilityStudyV1Scanner.ResolvePrimaryHorizonMinutes(interval));
    }

    private static LongShortFuturesFeasibilityObservation SampleObservation(string window, int seed)
        => new()
        {
            WindowLabel = window,
            Symbol = TradingSymbol.BNBUSDT,
            Interval = "15m",
            TimeUtc = DateTime.UtcNow.AddMinutes(seed),
            HourOfDayUtc = 10,
            SessionBucket = "Europe",
            VolatilityRegime = seed % 2 == 0 ? "Normal" : "Elevated",
            TrendRegime = seed % 3 == 0 ? "Uptrend" : "Sideways",
            TrendSlopePercent = 0.04m,
            RangeWidthPercent = 0.80m,
            DistanceFromRecentHighPercent = 0.40m,
            DistanceFromRecentLowPercent = 0.20m,
            RecentReturn60CandlesPercent = 0.15m,
            AtrPercent = 0.35m,
            VolumeExpansionRatio = 1.10m,
            BtcReturn30mPercent = 0.05m,
            BtcTrendRegime = "Uptrend",
            BtcMarketDirectionBucket = "Up",
            MarketWideReturnProxyPercent = 0.03m,
            EntryPrice = 100m,
            PrimaryForwardHorizonMinutes = 480,
            LongTarget050BeforeStop050 = seed % 7 == 0,
            ShortTarget050BeforeStop050 = seed % 5 == 0,
            LongExpectedNetSpotConservativePercent = -0.25m,
            ShortExpectedNetSpotConservativePercent = -0.22m,
            LongExpectedNetFuturesModeratePercent = seed % 5 == 0 ? 0.02m : -0.08m,
            ShortExpectedNetFuturesModeratePercent = seed % 4 == 0 ? 0.03m : -0.06m,
            LongExpectedNetFuturesLowPercent = 0.01m,
            ShortExpectedNetFuturesLowPercent = 0.02m,
            LongExpectedNetFuturesStressPercent = -0.15m,
            ShortExpectedNetFuturesStressPercent = -0.12m,
            BestDirectionExpectedNetFuturesModeratePercent = seed % 4 == 0 ? 0.03m : -0.06m,
            BestDirectionFuturesModerate = seed % 4 == 0 ? LongShortDirection.Short : LongShortDirection.Long
        };

    private static IReadOnlyList<KlineCandle> BuildIntervalCandles(DateTime start, int count)
    {
        var candles = new List<KlineCandle>();
        for (var i = 0; i < count; i++)
        {
            var price = 100m + i * 0.01m;
            candles.Add(new KlineCandle(
                TradingSymbol.BNBUSDT,
                start.AddMinutes(i * 5),
                price,
                price + 0.5m,
                price - 0.5m,
                price,
                10m));
        }

        return candles;
    }
}
