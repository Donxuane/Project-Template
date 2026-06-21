using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class DirectionalRuleFuturesSimulationV1Tests
{
    [Fact]
    public void BuildDefaultHoldoutRules_ContainsSevenRules()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        Assert.Equal(7, rules.Count);
        Assert.Equal(3, rules.Count(r => r.Direction == LongShortDirection.Long));
        Assert.Equal(4, rules.Count(r => r.Direction == LongShortDirection.Short));
    }

    [Fact]
    public void MatchesRule_QuantilePair_RespectsBounds()
    {
        var rule = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules()
            .First(r => r.RuleName == "Rule01_Short_DistHighQ1_AtrQ3");

        var matching = new MarketRegimeForwardEdgeScanner.RegimeCandleFeatures
        {
            DistanceFromRecentHighPercent = 0.10m,
            AtrPercent = 1.00m,
            VolatilityRegime = "Normal"
        };
        var nonMatching = matching with { DistanceFromRecentHighPercent = 0.90m };

        Assert.True(DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(matching, rule));
        Assert.False(DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(nonMatching, rule));
    }

    [Fact]
    public void MatchesRule_VolatilityOnly_MatchesAggregatorBehavior()
    {
        var elevatedRule = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules()
            .First(r => r.RuleName == "Rule06_Long_BtcBuckets_ElevatedVol");

        var features = new MarketRegimeForwardEdgeScanner.RegimeCandleFeatures
        {
            VolatilityRegime = "Elevated",
            BtcReturn30mPercent = -5m
        };

        Assert.True(DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(features, elevatedRule));
        Assert.True(DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(
            features with { BtcReturn30mPercent = 5m }, elevatedRule));
        Assert.False(DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(
            features with { VolatilityRegime = "Normal" }, elevatedRule));
    }

    [Fact]
    public void SimulateDirectionalTrade_Long_HitsProfitTarget()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<KlineCandle>
        {
            new(TradingSymbol.BNBUSDT, start, 100m, 100m, 100m, 100m, 1m),
            new(TradingSymbol.BNBUSDT, start.AddMinutes(1), 100m, 100.8m, 99.9m, 100.5m, 1m)
        };

        var result = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
            candles, start, 100m, 60, 0.50m, 0.50m, LongShortDirection.Long);

        Assert.Equal("ProfitTarget", result.ExitReason);
        Assert.True(result.MfePercent > 0m);
    }

    [Fact]
    public void SimulateDirectionalTrade_Short_HitsStopLoss()
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<KlineCandle>
        {
            new(TradingSymbol.BNBUSDT, start, 100m, 100m, 100m, 100m, 1m),
            new(TradingSymbol.BNBUSDT, start.AddMinutes(1), 100m, 100.8m, 99.9m, 100.5m, 1m)
        };

        var result = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
            candles, start, 100m, 60, 0.50m, 0.50m, LongShortDirection.Short);

        Assert.Equal("StopLoss", result.ExitReason);
    }

    [Fact]
    public void ComputeCostBreakdown_AppliesFuturesModerateComponents()
    {
        var simulation = new DirectionalTradeSimulationResult(
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            100m,
            new DateTime(2025, 1, 1, 1, 0, 0, DateTimeKind.Utc),
            100.5m,
            "ProfitTarget",
            0.5m,
            0.1m,
            60m);

        var scenario = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios()
            .First(s => s.Label == "futures-moderate");
        var breakdown = DirectionalRuleFuturesSimulationV1Simulator.ComputeCostBreakdown(
            simulation, LongShortDirection.Long, scenario, 1m);

        Assert.True(breakdown.SlippageEstimateQuote > 0m);
        Assert.True(breakdown.FundingEstimateQuote > 0m);
        Assert.True(breakdown.NetPnlQuote < 0.5m);
    }

    [Fact]
    public void BuildResearchAnswers_CoversDirectionalSimulationQuestions()
    {
        var rules = DirectionalRuleFuturesSimulationV1RuleCatalog.BuildDefaultHoldoutRules();
        var trades = new List<DirectionalRuleFuturesTradeRecord>();
        for (var i = 0; i < 60; i++)
        {
            trades.Add(SampleTrade(i, "futures-moderate", i % 2 == 0 ? 0.01m : -0.01m));
            trades.Add(SampleTrade(i, "futures-stress", -0.02m));
        }

        var summaries = DirectionalRuleFuturesSimulationV1Aggregator.BuildSummaries(trades);
        var performance = DirectionalRuleFuturesSimulationV1Aggregator.BuildRulePerformance(trades);
        var robustness = DirectionalRuleFuturesSimulationV1Aggregator.BuildWindowRobustness(summaries);
        var costSensitivity = DirectionalRuleFuturesSimulationV1Aggregator.BuildCostSensitivity(trades);
        var moderateTrades = trades
            .Where(t => string.Equals(t.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var entryStats = moderateTrades
            .GroupBy(t => t.EntryMode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (g.Sum(t => t.NetPnlQuote), g.Count()));
        var answers = DirectionalRuleFuturesSimulationV1Aggregator.BuildResearchAnswers(
            summaries, performance, robustness, costSensitivity, rules,
            moderateTrades.Length, moderateTrades.Sum(t => t.NetPnlQuote), entryStats);

        Assert.True(answers.Count >= 9);
        Assert.Contains(answers, a => a.Question.Contains("holdout-non-negative", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Verdict == "DoNotRecommendLiveFutures");
    }

    private static DirectionalRuleFuturesTradeRecord SampleTrade(int index, string costLabel, decimal netPnl)
    {
        var window = index < 20 ? "30d" : index < 40 ? "60d" : "90d";
        return new DirectionalRuleFuturesTradeRecord
        {
            RuleName = "Rule01_Short_DistHighQ1_AtrQ3",
            Direction = LongShortDirection.Short,
            Symbol = TradingSymbol.BNBUSDT,
            Interval = "15m",
            WindowLabel = window,
            TimeUtc = new DateTime(2025, 1, 1, 0, index, 0, DateTimeKind.Utc),
            EntryPrice = 100m,
            ExitPrice = 100.5m,
            ExitReason = "ProfitTarget",
            TargetPercent = 0.50m,
            StopPercent = 0.50m,
            MaxHoldMinutes = 240,
            CostScenarioLabel = costLabel,
            GrossPnlQuote = netPnl + 0.05m,
            NetPnlQuote = netPnl,
            FundingEstimateQuote = 0.01m,
            SlippageEstimateQuote = 0.02m,
            VolatilityRegime = "Normal",
            RangeWidthPercent = 2m,
            DistanceFromRecentHighPercent = 0.1m,
            DistanceFromRecentLowPercent = 1m,
            AtrPercent = 1m,
            TrendSlopePercent = -0.5m,
            MfePercent = 0.6m,
            MaePercent = 0.1m,
            DurationMinutes = 30m,
            EntryMode = index % 2 == 0 ? "NextOpen" : "NextClose"
        };
    }
}
