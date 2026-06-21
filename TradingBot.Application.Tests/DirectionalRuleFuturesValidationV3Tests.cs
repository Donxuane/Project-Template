using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class DirectionalRuleFuturesValidationV3Tests
{
    [Fact]
    public void BuildProfiles_GeneratesControlledMatrix()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        var profiles = DirectionalRuleFuturesValidationV3Catalog.BuildProfiles(rules);
        Assert.Equal(144, profiles.Count);
        Assert.All(profiles, p =>
        {
            Assert.Equal(TradingSymbol.BNBUSDT, p.Symbol);
            Assert.Equal("5m", p.Interval);
            Assert.True(p.Rule.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void PrimaryCandidate_MarkedForNextClose4hCd3()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        var profiles = DirectionalRuleFuturesValidationV3Catalog.BuildProfiles(rules);
        var primary = profiles.Where(p => p.IsPrimaryCandidate).ToArray();
        Assert.Equal(2, primary.Length);
        Assert.All(primary, p =>
        {
            Assert.Equal(DirectionalRuleEntryMode.NextClose, p.EntryMode);
            Assert.Equal(240, p.MaxHoldMinutes);
            Assert.Equal(3, p.CooldownCandlesAfterExit);
            Assert.Equal(1.50m, p.TargetPercent);
            Assert.Equal(1.00m, p.StopPercent);
        });
    }

    [Fact]
    public void BuildValidationScenarios_IncludesFuturesLow()
    {
        var scenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios();
        Assert.Contains(scenarios, s => s.Label == "futures-low");
        Assert.Contains(scenarios, s => s.Label == "futures-moderate-latency-005");
    }

    [Fact]
    public void BuildValidationWindows_AddsHoldoutAndTrain()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var windows = DirectionalRuleFuturesValidationV3Application.BuildValidationWindows(
            start, end, [30, 60, 90], null, null);
        Assert.Contains(windows, w => w.Label == "holdout30d");
        Assert.Contains(windows, w => w.Label == "trainReference");
        Assert.Contains(windows, w => w.Label == "90d");
    }

    [Fact]
    public void BuildFocusedProfiles_GeneratesSmallMatrix()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        var profiles = DirectionalRuleFuturesValidationV3Catalog.BuildFocusedProfiles(rules);
        Assert.Equal(8, profiles.Count);
        Assert.Single(profiles, p => p.IsPrimaryCandidate);
        Assert.Single(profiles, p => p.IsSmokeBestCandidate);
        Assert.All(profiles, p =>
            Assert.Equal(DirectionalRuleFuturesValidationV3Catalog.FocusedOverlapPolicy, p.OverlapPolicy));
    }

    [Fact]
    public void ResolveReferenceTradeCount_UsesLongestAvailableWindow()
    {
        var row = new DirectionalRuleV3WindowRobustnessRow
        {
            Window30dTrades = 12,
            Window60dTrades = 24,
            Window90dTrades = 0,
            Window180dTrades = 0
        };
        Assert.Equal(24, DirectionalRuleFuturesValidationV3Aggregator.ResolveReferenceTradeCount(row));
    }

    [Fact]
    public void BuildVariantComparison_Uses30dTradesWhen90dMissing()
    {
        var robust = new[]
        {
            WindowRow("primary", "futures-moderate", 30, 40.36m),
            WindowRow("smoke", "futures-moderate", 33, 55.35m)
        };
        var drawdown = new[]
        {
            new DirectionalRuleV3DrawdownRow
            {
                ProfileKey = "primary",
                WindowLabel = "30d",
                CostScenarioLabel = "futures-moderate",
                TradeCount = 30,
                MaxDrawdownQuote = 66.75m,
                MaxConsecutiveLosses = 4
            }
        };
        var comparison = DirectionalRuleFuturesValidationV3Aggregator.BuildVariantComparison(robust, drawdown);
        var primary = comparison.Single(c => c.ProfileKey == "primary");
        Assert.Equal(30, primary.ExecutedTrades);
        Assert.Equal(66.75m, primary.MaxDrawdownQuote);
        Assert.NotEqual("InsufficientSamples", primary.ComparisonVerdict);
    }

    [Fact]
    public void BuildReportConsistency_FlagsTradeCountMismatch()
    {
        var summaries = new[]
        {
            Summary("p1", "30d", "futures-moderate", 10m, 5)
        };
        var trades = new[]
        {
            Trade("p1", "30d", "futures-moderate"),
            Trade("p1", "30d", "futures-moderate")
        };
        var rows = DirectionalRuleFuturesValidationV3Aggregator.BuildReportConsistency(summaries, trades, [30]);
        var row = rows.Single(r => r.WindowLabel == "30d" && r.CostScenarioLabel == "futures-moderate");
        Assert.True(row.CountMismatch);
        Assert.Equal(5, row.ReportedTradeCount);
        Assert.Equal(2, row.ActualTradeRowCount);
    }

    [Fact]
    public void BuildWindowRobustness_LabelsHoldoutExplicitly()
    {
        var summaries = new[]
        {
            Summary("p1", "30d", "futures-moderate", 10m, 20),
            Summary("p1", "60d", "futures-moderate", 5m, 20),
            Summary("p1", "90d", "futures-moderate", 8m, 20),
            Summary("p1", "holdout30d", "futures-moderate", 12m, 15),
            Summary("p1", "trainReference", "futures-moderate", 11m, 30)
        };
        var robust = DirectionalRuleFuturesValidationV3Aggregator.BuildWindowRobustness(summaries).Single();
        Assert.True(robust.HoldoutPositive);
        Assert.True(robust.AllWindowsPositive);
        Assert.Equal(12m, robust.Holdout30dNetPnl);
    }

    private static DirectionalRuleV3WindowRobustnessRow WindowRow(
        string profileKey,
        string cost,
        int trades30,
        decimal net30)
        => new()
        {
            ProfileKey = profileKey,
            VariantLabel = profileKey,
            CostScenarioLabel = cost,
            Window30dTrades = trades30,
            Window30dNetPnl = net30,
            AggregateNetPnl = net30,
            AggregatePositive = net30 >= 0m,
            AllWindowsPositive = net30 >= 0m,
            HoldoutPositive = net30 >= 0m,
            EntryMode = "NextClose",
            OverlapPolicy = "OneOpenTradePerRuleSymbol",
            CooldownCandlesAfterExit = 3,
            TargetPercent = 1.50m,
            StopPercent = 1.00m,
            MaxHoldMinutes = 240
        };

    private static DirectionalRuleV3TradeRecord Trade(string profileKey, string window, string cost)
        => new()
        {
            ProfileKey = profileKey,
            WindowLabel = window,
            CostScenarioLabel = cost,
            ExitReason = "Target",
            NetPnlQuote = 1m,
            EntryTimeUtc = DateTime.UtcNow
        };

    private static DirectionalRuleV3FocusedSummaryRow Summary(
        string profileKey,
        string window,
        string cost,
        decimal net,
        int trades)
        => new()
        {
            ProfileKey = profileKey,
            VariantLabel = "test",
            WindowLabel = window,
            CostScenarioLabel = cost,
            NetPnlQuote = net,
            ExecutedTrades = trades,
            EntryMode = "NextClose",
            OverlapPolicy = "OneOpenTradePerSymbol",
            CooldownCandlesAfterExit = 3,
            TargetPercent = 1.50m,
            StopPercent = 1.00m,
            MaxHoldMinutes = 240
        };
}
