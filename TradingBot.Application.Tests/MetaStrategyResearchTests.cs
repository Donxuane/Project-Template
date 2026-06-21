using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class MetaStrategyResearchTests
{
    [Fact]
    public void ResolveExitPolicyName_DetectsLock90AndProfitTarget()
    {
        Assert.Equal("Lock90", MetaStrategyResearchImporter.ResolveExitPolicyName("impulse-continuation-v1-ETH-1m-lock90"));
        Assert.Equal("ProfitTarget", MetaStrategyResearchImporter.ResolveExitPolicyName("mean-reversion-range-v1-ETH-1m-profit-target"));
        Assert.Equal("MidpointTarget", MetaStrategyResearchImporter.ResolveExitPolicyName("mean-reversion-range-v1-ETH-1m-midpoint-target"));
    }

    [Fact]
    public void BuildStrategyFamilySummary_AggregatesExecutedAndBlocked()
    {
        var records = new[]
        {
            SampleRecord(executed: true, net: 0.2m, family: "ImpulseContinuationV1", exitReason: "ProfitLock"),
            SampleRecord(executed: true, net: -0.5m, family: "ImpulseContinuationV1", exitReason: "StopLoss"),
            SampleRecord(executed: false, net: null, family: "ImpulseContinuationV1", exitReason: null)
        };

        var summary = MetaStrategyResearchAggregator.BuildStrategyFamilySummary(records, records.Where(r => r.CandidateWasExecuted).ToArray());
        var row = Assert.Single(summary);
        Assert.Equal(2, row.Trades);
        Assert.Equal(1, row.BlockedCandidates);
        Assert.Equal(-0.3m, row.NetPnlQuote);
    }

    [Fact]
    public void BuildBestSubsets_FlagsSparsePositiveSubsets()
    {
        var records = Enumerable.Range(0, 10)
            .Select(i => SampleRecord(
                executed: true,
                net: 0.1m,
                family: "MeanReversionRangeBounceV1",
                symbol: "ETHUSDT",
                interval: "1m",
                window: "30d",
                exitReason: "ProfitTarget"))
            .ToArray();

        var subsets = MetaStrategyResearchAggregator.BuildBestSubsets(records);
        var row = subsets.First(x => x.RuleDescription.Contains("MeanReversionRangeBounceV1", StringComparison.Ordinal));
        Assert.False(row.MeetsRobustnessCriteria);
        Assert.Equal("Sparse", row.Verdict);
    }

    [Fact]
    public void BuildResearchAnswers_ReportsNoRobustPositiveSubset_WhenOnlySparseData()
    {
        var records = Enumerable.Range(0, 20)
            .Select(i => SampleRecord(
                executed: true,
                net: -0.1m,
                family: "MeanReversionRangeBounceV1",
                symbol: "ETHUSDT",
                interval: "1m",
                window: i % 2 == 0 ? "30d" : "60d",
                exitReason: "StopLoss"))
            .ToArray();
        var executed = records.Where(r => r.CandidateWasExecuted).ToArray();
        var importReport = new MetaStrategyResearchImportReport(
            ["test"],
            [new MetaStrategyResearchImportSourceReport("test", "MeanReversionRangeBounceV1", "trades.json", executed.Length, 0, null, "Directory")],
            false,
            0);

        var diagnostics = MetaStrategyResearchAggregator.Build(records, importReport);
        var answer = diagnostics.ResearchAnswers.First(a =>
            a.Question.Contains("entry-time subset", StringComparison.OrdinalIgnoreCase));

        Assert.Equal("NoEntryTimeRobustSubset", answer.Verdict);
    }

    [Fact]
    public void BuildEntryTimeRuleDiscovery_MarksTradableRules()
    {
        var records = BuildRuleDiscoverySample(usesForwardOutcome: false);
        var rules = MetaStrategyResearchAggregator.BuildEntryTimeRuleDiscovery(records);
        Assert.All(rules, r =>
        {
            Assert.Equal("EntryTime", r.RuleGroup);
            Assert.False(r.UsesFutureInformation);
            Assert.True(r.TradableRule);
        });
    }

    [Fact]
    public void BuildOutcomeDiagnosticRuleDiscovery_FlagsFutureInformation()
    {
        var records = BuildRuleDiscoverySample(usesForwardOutcome: true);
        var rules = MetaStrategyResearchAggregator.BuildOutcomeDiagnosticRuleDiscovery(records);
        Assert.All(rules, r =>
        {
            Assert.Equal("OutcomeDiagnostic", r.RuleGroup);
            Assert.True(r.UsesFutureInformation);
            Assert.False(r.TradableRule);
        });
    }

    private static IReadOnlyList<MetaStrategyResearchRecord> BuildRuleDiscoverySample(bool usesForwardOutcome)
    {
        var rows = new List<MetaStrategyResearchRecord>();
        for (var i = 0; i < 120; i++)
        {
            var window = i % 3 == 0 ? "90d" : i % 2 == 0 ? "60d" : "30d";
            rows.Add(new MetaStrategyResearchRecord
            {
                StrategyFamily = "ImpulseContinuationV1",
                ProfileName = "impulse-test",
                Symbol = "ETHUSDT",
                Interval = "1m",
                WindowLabel = window,
                TimeUtc = DateTime.UtcNow.AddMinutes(i),
                CandidateWasExecuted = true,
                NetPnlQuote = i % 10 == 0 ? 0.05m : -0.02m,
                IsNetWinner = i % 10 == 0,
                StopDistancePercent = 0.1m + (i % 5) * 0.05m,
                BreakoutBodyStrengthPercent = 50m + (i % 7),
                ExpectedMovePercent = 0.3m + (i % 4) * 0.02m,
                ForwardMfe60Percent = usesForwardOutcome ? 0.5m + (i % 6) * 0.1m : null,
                ForwardMae60Percent = usesForwardOutcome ? -0.2m - (i % 4) * 0.05m : null,
                MfePercent = usesForwardOutcome ? 0.4m : null,
                MaePercent = usesForwardOutcome ? -0.1m : null
            });
        }

        return rows;
    }

    private static MetaStrategyResearchRecord SampleRecord(
        bool executed,
        decimal? net,
        string family,
        string exitReason,
        string symbol = "ETHUSDT",
        string interval = "1m",
        string window = "90d")
    {
        return new MetaStrategyResearchRecord
        {
            StrategyFamily = family,
            ProfileName = $"{family}-{symbol}-{interval}",
            Symbol = symbol,
            Interval = interval,
            WindowLabel = window,
            TimeUtc = DateTime.UtcNow,
            CandidateWasExecuted = executed,
            NetPnlQuote = net,
            GrossPnlQuote = net,
            IsNetWinner = net > 0m,
            ExitReason = exitReason,
            StopDistancePercent = 0.5m,
            BreakoutBodyStrengthPercent = 75m,
            ForwardMfe60Percent = 1.2m,
            ForwardMae60Percent = -0.4m
        };
    }
}
