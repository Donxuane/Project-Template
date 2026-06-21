using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class DirectionalRuleFuturesRegimeDriftAnalysisV1Tests
{
    [Fact]
    public void BuildProfile_MatchesBestBnbCandidate()
    {
        var rule = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules()
            .First(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase));
        var profile = DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.BuildProfile(rule);

        Assert.Equal(TradingSymbol.BNBUSDT, profile.Symbol);
        Assert.Equal("5m", profile.Interval);
        Assert.Equal(1.75m, profile.TargetPercent);
        Assert.Equal(1.00m, profile.StopPercent);
        Assert.Equal(240, profile.MaxHoldMinutes);
        Assert.Equal(6, profile.CooldownCandlesAfterExit);
        Assert.True(profile.IsBestBnbCandidate);
    }

    [Fact]
    public void BuildSummary_SplitsRecentAndOlderPeriods()
    {
        var end = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var trades = new[]
        {
            MakeTrade(end.AddDays(-10), 5m),
            MakeTrade(end.AddDays(-10), -3m),
            MakeTrade(end.AddDays(-120), -2m),
            MakeTrade(end.AddDays(-120), 4m)
        };

        var summary = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildSummary(
            trades,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);

        var recent90 = summary.First(s => s.PeriodLabel == "90d");
        var older = summary.First(s => s.PeriodLabel == "older");
        Assert.Equal(2, recent90.TradeCount);
        Assert.Equal(2m, recent90.NetPnlQuote);
        Assert.Equal(2, older.TradeCount);
        Assert.Equal(2m, older.NetPnlQuote);
    }

    [Fact]
    public void BuildFeatureComparison_ProducesWinnerLoserBuckets()
    {
        var end = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var trades = new[]
        {
            MakeTrade(end.AddDays(-5), 2m, atr: 0.5m),
            MakeTrade(end.AddDays(-8), -1m, atr: 0.8m),
            MakeTrade(end.AddDays(-100), 3m, atr: 0.3m),
            MakeTrade(end.AddDays(-150), -4m, atr: 0.9m)
        };

        var rows = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildFeatureComparison(
            trades,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);

        Assert.Contains(rows, r => r.ComparisonGroup == "RecentWinners" && r.TradeCount == 1);
        Assert.Contains(rows, r => r.ComparisonGroup == "OlderLosers" && r.TradeCount == 1);
    }

    [Fact]
    public void BuildEntryTimeRules_FlagsSparseWhenTrainTooSmall()
    {
        var end = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var trades = Enumerable.Range(0, 10)
            .Select(i => MakeTrade(end.AddDays(-i % 3), i % 2 == 0 ? 1m : -1m))
            .ToArray();

        var rules = DirectionalRuleFuturesRegimeDriftAnalysisV1Aggregator.BuildEntryTimeRules(
            trades,
            DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario);

        Assert.Contains(rules, r => r.SparseWarning);
    }

    private static RegimeDriftDiagnosticTrade MakeTrade(DateTime entry, decimal net, decimal atr = 0.4m)
    {
        var end = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        return new RegimeDriftDiagnosticTrade
        {
            EntryTimeUtc = entry,
            ExitTimeUtc = entry.AddHours(4),
            NetPnlQuote = net,
            GrossPnlQuote = net,
            IsWinner = net > 0m,
            CostScenarioLabel = DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario,
            DistanceFromRecentHighPercent = 0.2m,
            DistanceFromRecentLowPercent = 0.5m,
            RangeWidthPercent = 1.1m,
            AtrPercent = atr,
            TrendSlopePercent = -0.1m,
            BtcReturn30mPercent = 0.05m,
            VolatilityRegime = "Normal",
            SessionBucket = "Europe",
            BtcTrendRegime = "BtcFlat",
            InRecent30d = entry >= end.AddDays(-30),
            InRecent60d = entry >= end.AddDays(-60),
            InRecent90d = entry >= end.AddDays(-90),
            InOlder = entry < end.AddDays(-90),
            InTrainReference = entry < end.AddDays(-30),
            InHoldout30d = entry >= end.AddDays(-30),
            MonthKey = $"{entry.Year}-{entry.Month:D2}"
        };
    }
}
