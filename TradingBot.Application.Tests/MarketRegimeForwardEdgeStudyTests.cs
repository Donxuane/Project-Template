using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class MarketRegimeForwardEdgeStudyTests
{
    [Fact]
    public void BuildSummary_GroupsBySymbolIntervalAndWindow()
    {
        var observations = new[]
        {
            SampleObservation("30d", TradingSymbol.BNBUSDT, "15m", expectedNet: -0.10m, targetFirst: false),
            SampleObservation("30d", TradingSymbol.BNBUSDT, "15m", expectedNet: 0.05m, targetFirst: true),
            SampleObservation("90d", TradingSymbol.ETHUSDT, "30m", expectedNet: -0.20m, targetFirst: false)
        };

        var summary = MarketRegimeForwardEdgeAggregator.BuildSummary(observations);

        Assert.Equal(2, summary.Count);
        Assert.Contains(summary, row => row.Symbol == TradingSymbol.BNBUSDT && row.Interval == "15m" && row.WindowLabel == "30d" && row.SampleCount == 2);
    }

    [Fact]
    public void BuildResearchAnswers_CoversRegimeDiscoveryQuestions()
    {
        var observations = Enumerable.Range(0, 250)
            .Select(i => SampleObservation(
                i < 120 ? "30d" : i < 200 ? "60d" : "90d",
                TradingSymbol.BNBUSDT,
                "15m",
                expectedNet: i % 5 == 0 ? 0.02m : -0.08m,
                targetFirst: i % 7 == 0))
            .ToArray();

        var summary = MarketRegimeForwardEdgeAggregator.BuildSummary(observations);
        var symbolRanking = MarketRegimeForwardEdgeAggregator.BuildSymbolIntervalRanking(summary);
        var regimeRanking = MarketRegimeForwardEdgeAggregator.BuildRegimeBucketRanking(observations);
        var sessionRanking = MarketRegimeForwardEdgeAggregator.BuildSessionRanking(observations);
        var matrix = new[]
        {
            new TargetBeforeStopMatrixRow
            {
                WindowLabel = "90d",
                Symbol = TradingSymbol.BNBUSDT,
                Interval = "15m",
                TargetPercent = 0.50m,
                StopPercent = -0.50m,
                SampleCount = 250,
                TargetBeforeStopCount = 35,
                StopBeforeTargetCount = 180,
                TargetBeforeStopRate = 0.14m,
                ExpectedNetAfterCostPercent = -0.20m
            }
        };
        var rules = MarketRegimeForwardEdgeAggregator.BuildEntryTimeRules(observations);
        var answers = MarketRegimeForwardEdgeAggregator.BuildResearchAnswers(
            observations, symbolRanking, regimeRanking, sessionRanking, matrix, rules, 0.35m);

        Assert.Equal(7, answers.Count);
        Assert.Contains(answers, a => a.Question.Contains("symbol/interval has the best forward edge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("Spot research be paused", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResolveSessionBucket_MapsUtcHours()
    {
        Assert.Equal("Asia", MarketRegimeForwardEdgeScanner.ResolveSessionBucket(3));
        Assert.Equal("Europe", MarketRegimeForwardEdgeScanner.ResolveSessionBucket(10));
        Assert.Equal("US", MarketRegimeForwardEdgeScanner.ResolveSessionBucket(16));
        Assert.Equal("LateUS", MarketRegimeForwardEdgeScanner.ResolveSessionBucket(22));
    }

    private static MarketRegimeForwardEdgeObservation SampleObservation(
        string window,
        TradingSymbol symbol,
        string interval,
        decimal expectedNet,
        bool targetFirst)
        => new()
        {
            WindowLabel = window,
            Symbol = symbol,
            Interval = interval,
            TimeUtc = DateTime.UtcNow,
            HourOfDayUtc = 10,
            SessionBucket = "Europe",
            RecentReturn5CandlesPercent = 0.05m,
            RecentReturn15CandlesPercent = 0.10m,
            RecentReturn30CandlesPercent = 0.12m,
            RecentReturn60CandlesPercent = 0.15m,
            RangeWidthPercent = 0.80m,
            AtrPercent = 0.35m,
            VolumeExpansionRatio = 1.10m,
            VolatilityRegime = "Normal",
            TrendSlopePercent = 0.04m,
            TrendStrengthPercent = 0.12m,
            TrendRegime = "Uptrend",
            DistanceFromRecentHighPercent = 0.40m,
            DistanceFromRecentLowPercent = 0.20m,
            CandleBodyStrengthPercent = 55m,
            ClosePositionInRange = 0.70m,
            EntryPrice = 100m,
            ForwardMfePercent = 0.60m,
            ForwardMaePercent = -0.40m,
            Target050BeforeStop050 = targetFirst,
            ExpectedNetAfterCostPercent = expectedNet,
            RoundTripCostPercent = 0.35m,
            LongEdgeScore = expectedNet
        };
}
