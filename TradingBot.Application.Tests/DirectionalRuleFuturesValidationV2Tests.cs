using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class DirectionalRuleFuturesValidationV2Tests
{
    [Fact]
    public void BuildCandidates_IncludesFiveNarrowConfigs()
    {
        var candidates = DirectionalRuleFuturesValidationV2Catalog.BuildCandidates();
        Assert.Equal(5, candidates.Count);
        Assert.Contains(candidates, c => c.RuleKey == "Rule01" && c.Symbol == TradingSymbol.ETHUSDT && c.Interval == "30m");
        Assert.Contains(candidates, c => c.RuleKey == "Rule01" && c.Symbol == TradingSymbol.BNBUSDT && c.Interval == "5m");
        Assert.Contains(candidates, c => c.RuleKey == "Rule05" && c.Symbol == TradingSymbol.ETHUSDT && c.Interval == "15m");
    }

    [Fact]
    public void BuildProfiles_GeneratesSeventyTwoPerCandidateRule()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        var profiles = DirectionalRuleFuturesValidationV2Catalog.BuildProfiles(rules);
        Assert.Equal(5 * 72, profiles.Count);
        Assert.All(profiles, p =>
        {
            Assert.Equal(1.50m, p.TargetPercent);
            Assert.Equal(1.00m, p.StopPercent);
            Assert.Contains(p.MaxHoldMinutes, DirectionalRuleFuturesValidationV2Catalog.HoldMinutesOptions);
        });
    }

    [Fact]
    public void BuildValidationScenarios_IncludesStressPlusAndLatency()
    {
        var scenarios = DirectionalRuleFuturesValidationV2CostModel.BuildValidationScenarios();
        Assert.Contains(scenarios, s => s.Label == "futures-stress-plus");
        Assert.Contains(scenarios, s => s.Label == "futures-moderate-latency-005");
        Assert.Contains(scenarios, s => s.Label == "futures-stress-latency-002");
    }

    [Fact]
    public void ApplyRobustnessLabels_SeparatesAggregateAndHoldout()
    {
        var summaries = new[]
        {
            SampleSummary("p1", "30d", "futures-moderate", 20m),
            SampleSummary("p1", "60d", "futures-moderate", -5m),
            SampleSummary("p1", "90d", "futures-moderate", 10m),
            SampleSummary("p1", "30d", "futures-stress", -1m),
            SampleSummary("p1", "60d", "futures-stress", -1m),
            SampleSummary("p1", "90d", "futures-stress", -1m)
        };

        var labeled = DirectionalRuleFuturesValidationV2Aggregator.ApplyRobustnessLabels(summaries);
        var moderate30 = labeled.First(r => r.WindowLabel == "30d" && r.CostScenarioLabel == "futures-moderate");
        Assert.True(moderate30.AggregateNetPositive);
        Assert.False(moderate30.AllWindowsPositive);
        Assert.True(moderate30.Holdout90dPositive);
        Assert.False(moderate30.Window60dNetPositive);

        var stress30 = labeled.First(r => r.WindowLabel == "30d" && r.CostScenarioLabel == "futures-stress");
        Assert.False(stress30.StressAggregatePositive);
        Assert.False(stress30.StressAllWindowsPositive);
    }

    [Fact]
    public void BuildResearchAnswers_DoesNotConflateStressLabels()
    {
        var windowRobustness = new[]
        {
            new DirectionalRuleV2WindowRobustnessRow
            {
                ProfileKey = "p1",
                CostScenarioLabel = "futures-stress",
                AggregateNetPositive = true,
                AllWindowsPositive = false,
                Holdout90dPositive = false,
                Window30dTrades = 20,
                Window60dTrades = 20,
                Window90dTrades = 20,
                AggregateNetPnl = 1m
            }
        };
        var costSensitivity = new[]
        {
            new DirectionalRuleV2CostSensitivityRow
            {
                ProfileKey = "p1",
                CostScenarioLabel = "futures-stress",
                TradeCount = 60,
                NetPnlQuote = 1m,
                AggregateNetPositive = true,
                StressAggregatePositive = true,
                StressAllWindowsPositive = false
            }
        };

        var answers = DirectionalRuleFuturesValidationV2Aggregator.BuildResearchAnswers(
            [],
            windowRobustness,
            costSensitivity,
            [],
            [],
            60,
            0);

        var stressAnswer = answers.First(a => a.Question.Contains("stress", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("StressAggregatePositiveFound", stressAnswer.Verdict);
        Assert.Contains("StressAllWindowsPositive is reported separately", stressAnswer.Answer);
    }

    [Fact]
    public void ComputeCostBreakdown_AppliesExtraLatencySlippage()
    {
        var simulation = new DirectionalTradeSimulationResult(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            100m,
            new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            99m,
            "StopLoss",
            0.1m,
            1m,
            60m);
        var scenario = DirectionalRuleFuturesValidationV2CostModel.BuildValidationScenarios()
            .First(s => s.Label == "futures-moderate-latency-005");

        var breakdown = DirectionalRuleFuturesValidationV2CostModel.ComputeCostBreakdown(
            simulation, LongShortDirection.Short, scenario);

        Assert.True(breakdown.SlippageEstimateQuote > 0m);
        Assert.True(breakdown.NetPnlQuote < simulation.EntryPrice - simulation.ExitPrice);
    }

    private static DirectionalRuleV2SummaryRow SampleSummary(
        string profileKey,
        string window,
        string cost,
        decimal net)
        => new()
        {
            ProfileKey = profileKey,
            RuleName = "Rule01",
            Symbol = TradingSymbol.ETHUSDT,
            Interval = "30m",
            WindowLabel = window,
            EntryMode = "NextOpen",
            OverlapPolicy = "OneOpenTradePerRuleSymbol",
            CostScenarioLabel = cost,
            ExecutedTrades = 20,
            NetPnlQuote = net,
            GrossPnlQuote = net
        };
}
