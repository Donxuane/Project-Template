using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class DirectionalRuleFuturesRegimeConditionalV2Tests
{
    [Fact]
    public void BuildProfile_MatchesBestBnbCandidate()
    {
        var rule = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules()
            .First(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase));
        var profile = DirectionalRuleFuturesRegimeConditionalV2Catalog.BuildProfile(rule);

        Assert.Equal(TradingSymbol.BNBUSDT, profile.Symbol);
        Assert.Equal("5m", profile.Interval);
        Assert.Equal(1.75m, profile.TargetPercent);
        Assert.Equal(1.00m, profile.StopPercent);
        Assert.Equal(240, profile.MaxHoldMinutes);
        Assert.Equal(6, profile.CooldownCandlesAfterExit);
    }

    [Fact]
    public void BuildFilters_IncludesBaselineAndControlledMatrix()
    {
        var trades = MakeTrades(120);
        var filters = DirectionalRuleFuturesRegimeConditionalV2Catalog.BuildFilters(trades);

        Assert.Contains(filters, f => f.Name == "Baseline");
        Assert.Contains(filters, f => f.FilterGroup == "BtcMomentum");
        Assert.Contains(filters, f => f.FilterGroup == "Volatility");
        Assert.Contains(filters, f => f.FilterGroup == "Combined");
        // Controlled: must not explode into hundreds of combinations.
        Assert.True(filters.Count < 30);
    }

    [Fact]
    public void BuildSummary_FlagsSparseWhenBelowThresholds()
    {
        var trades = MakeTrades(20);
        var filters = DirectionalRuleFuturesRegimeConditionalV2Catalog.BuildFilters(trades);
        var summary = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildSummary(
            trades, filters, DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario);

        var baseline = summary.First(s => s.FilterName == "Baseline");
        Assert.True(baseline.SparseWarning);
        Assert.False(baseline.PassesAllCriteria);
    }

    [Fact]
    public void BuildSummary_RequiresBothPeriodsAndFull365Positive()
    {
        // Recent-only winner: older strongly negative, recent positive -> should not pass.
        var trades = new List<RegimeDriftDiagnosticTrade>();
        trades.AddRange(MakePeriodTrades(60, recent: true, net: 3m));
        trades.AddRange(MakePeriodTrades(60, recent: false, net: -3m));
        var filters = DirectionalRuleFuturesRegimeConditionalV2Catalog.BuildFilters(trades);
        var summary = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildSummary(
            trades, filters, DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario);

        var baseline = summary.First(s => s.FilterName == "Baseline");
        Assert.False(baseline.OlderViable);
        Assert.False(baseline.PassesAllCriteria);
    }

    [Fact]
    public void BuildAnswers_ParksWhenNoFilterQualifies()
    {
        var trades = new List<RegimeDriftDiagnosticTrade>();
        trades.AddRange(MakePeriodTrades(60, recent: true, net: 3m));
        trades.AddRange(MakePeriodTrades(60, recent: false, net: -3m));
        var filters = DirectionalRuleFuturesRegimeConditionalV2Catalog.BuildFilters(trades);
        var summary = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildSummary(
            trades, filters, DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario);
        var cost = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildCostSensitivity(
            trades, filters, DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario);
        var answers = DirectionalRuleFuturesRegimeConditionalV2Aggregator.BuildAnswers(
            summary, cost, trades.Count, DateTime.UtcNow.AddDays(-365), DateTime.UtcNow);

        Assert.Contains(answers, a => a.Verdict == "ParkRecentRegimeOnly");
        Assert.Contains(answers, a => a.Verdict == "DoNotRecommendLiveFutures");
    }

    private static IReadOnlyList<RegimeDriftDiagnosticTrade> MakeTrades(int count)
    {
        var end = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var entry = end.AddDays(-(i * 3 % 360));
                return Build(entry, end, i % 2 == 0 ? 1m : -1m, btc30: (i % 5) * 0.1m - 0.2m, atr: 0.3m + (i % 4) * 0.05m);
            })
            .ToArray();
    }

    private static IReadOnlyList<RegimeDriftDiagnosticTrade> MakePeriodTrades(int count, bool recent, decimal net)
    {
        var end = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var entry = recent ? end.AddDays(-(i % 80) - 1) : end.AddDays(-100 - (i % 200));
                return Build(entry, end, net, btc30: 0.3m, atr: 0.35m);
            })
            .ToArray();
    }

    private static RegimeDriftDiagnosticTrade Build(DateTime entry, DateTime end, decimal net, decimal btc30, decimal atr)
        => new()
        {
            EntryTimeUtc = entry,
            ExitTimeUtc = entry.AddHours(4),
            NetPnlQuote = net,
            GrossPnlQuote = net,
            IsWinner = net > 0m,
            CostScenarioLabel = DirectionalRuleFuturesRegimeConditionalV2Catalog.PrimaryCostScenario,
            DistanceFromRecentHighPercent = 0.2m,
            DistanceFromRecentLowPercent = 0.5m,
            RangeWidthPercent = 1.1m,
            AtrPercent = atr,
            TrendSlopePercent = 0.05m,
            BtcReturn30mPercent = btc30,
            BtcReturn60mPercent = btc30 * 1.5m,
            VolatilityRegime = "Normal",
            BtcTrendRegime = "BtcUp",
            SessionBucket = "US",
            HourOfDayUtc = entry.Hour,
            DayOfWeek = entry.DayOfWeek.ToString(),
            MonthKey = $"{entry.Year}-{entry.Month:D2}",
            InRecent30d = entry >= end.AddDays(-30),
            InRecent60d = entry >= end.AddDays(-60),
            InRecent90d = entry >= end.AddDays(-90),
            InOlder = entry < end.AddDays(-90),
            InTrainReference = entry < end.AddDays(-30),
            InHoldout30d = entry >= end.AddDays(-30)
        };
}
