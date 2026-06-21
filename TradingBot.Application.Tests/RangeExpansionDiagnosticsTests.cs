using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class RangeExpansionDiagnosticsTests
{
    [Fact]
    public void BuildRangeExpansionFastProfiles_GeneratesExpectedNames()
    {
        var profiles = BacktestApplication.BuildRangeExpansionFastProfiles(includeComparisonProfiles: true);

        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v1-fast-ETH-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v1-fast-BNB-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v1-fast-SOL-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v1-fast-ETH+BNB+SOL-1m-profitlock-90");
        Assert.Equal(4, profiles.Count);
    }

    [Fact]
    public void BuildRangeExpansionV2FastProfiles_EnableExperimentalFilters()
    {
        var profiles = BacktestApplication.BuildRangeExpansionV2FastProfiles();

        Assert.All(profiles, p =>
            Assert.Equal("true", p.ConfigOverrides["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:Enabled"]));
        Assert.Contains(profiles, p => p.ProfileName.StartsWith("range-expansion-v2-fast-", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildPnlDecomposition_GroupsBySymbolAndWindow()
    {
        var candidates = new[]
        {
            SampleCandidate("30d", executed: true, symbol: TradingSymbol.BNBUSDT, net: -0.1m),
            SampleCandidate("90d", executed: true, symbol: TradingSymbol.ETHUSDT, net: 0.05m)
        };
        var trades = new[]
        {
            SampleTrade(candidates[0]),
            SampleTrade(candidates[1])
        };

        var rows = RangeExpansionDiagnosticsAggregator.BuildPnlDecomposition(
            RangeExpansionDiagnosticsAggregator.EnrichExecutedCandidates(candidates, trades),
            trades,
            candidates);

        Assert.Contains(rows, r => r.Dimension == "Symbol" && r.Key == TradingSymbol.BNBUSDT.ToString() && r.NetPnlQuote == -0.1m);
        Assert.Contains(rows, r => r.Dimension == "Window" && r.Key == "30d" && r.TradeCount == 1);
    }

    [Fact]
    public void BuildTradeQualityBuckets_SplitsWinnersAndLosers()
    {
        var candidates = new[]
        {
            SampleCandidate("30d", executed: true, symbol: TradingSymbol.BNBUSDT, net: 0.2m) with { IsWinner = true, ForwardMfe60Percent = 0.5m },
            SampleCandidate("30d", executed: true, symbol: TradingSymbol.BNBUSDT, net: -0.3m) with { IsWinner = false, ForwardMae60Percent = -0.4m }
        };

        var buckets = RangeExpansionDiagnosticsAggregator.BuildTradeQualityBuckets(candidates);

        Assert.Contains(buckets, b => b.Bucket == "Winners" && b.Count == 1);
        Assert.Contains(buckets, b => b.Bucket == "Losers" && b.Count == 1);
    }

    [Fact]
    public void DetectSeparator_RequiresMinimumSampleSizeAndMeaningfulDelta()
    {
        var winners = Enumerable.Range(0, 6).Select(_ => SampleCandidate("30d", executed: true, symbol: TradingSymbol.ETHUSDT, net: 0.1m) with
        {
            IsWinner = true,
            RangeWidthPercent = 0.20m,
            DistanceFromBreakoutPercent = 0.04m,
            ForwardMae60Percent = -0.05m,
            BreakoutBodyStrengthPercent = 60m
        }).ToArray();
        var losers = Enumerable.Range(0, 6).Select(_ => SampleCandidate("30d", executed: true, symbol: TradingSymbol.ETHUSDT, net: -0.2m) with
        {
            IsWinner = false,
            RangeWidthPercent = 0.40m,
            DistanceFromBreakoutPercent = 0.14m,
            ForwardMae60Percent = -0.35m,
            BreakoutBodyStrengthPercent = 30m
        }).ToArray();
        var comparison = RangeExpansionDiagnosticsAggregator.BuildWinnerLoserComparison(winners.Concat(losers).ToArray());

        Assert.True(RangeExpansionDiagnosticsAggregator.DetectSeparator(comparison));
    }

    [Fact]
    public void BuildBlockedReachableSummary_CountsReachableBlockedCandidates()
    {
        var candidates = new[]
        {
            SampleCandidate("30d", executed: false, symbol: TradingSymbol.BNBUSDT, net: null) with
            {
                RejectionReason = RangeExpansionBreakoutV1Model.FollowThroughFailed,
                Lock90ReachableWithin60m = true,
                Lock90ReachableAndNetProfitableWithin60m = true,
                Lock90NetProfitPercent = 0.10m,
                RequiredNetProfitPercent = 0.05m,
                ForwardMfe60Percent = 0.4m,
                ForwardMae60Percent = -0.1m
            },
            SampleCandidate("30d", executed: false, symbol: TradingSymbol.BNBUSDT, net: null) with
            {
                RejectionReason = RangeExpansionBreakoutV1Model.AntiChaseBreakoutDistanceExceeded,
                Lock90ReachableWithin60m = false
            }
        };

        var summary = RangeExpansionDiagnosticsAggregator.BuildBlockedReachableSummary(candidates);

        Assert.Equal(1, summary.BlockedReachableCount);
        Assert.Equal(1, summary.Lock90ReachableAndNetProfitableCount);
        Assert.Contains(summary.ByReason, r => r.RejectionReason == RangeExpansionBreakoutV1Model.FollowThroughFailed && r.BlockedReachableCount == 1);
        Assert.True(summary.CouldRelaxSafelyCandidateCount >= 1);
    }

    [Fact]
    public void ClassifyTargetTooSmallRejection_SplitsNetTradableFromFeeUntradable()
    {
        var tradableCost = new RangeExpansionCostMetrics
        {
            RequiredNetProfitPercent = 0.05m,
            Lock90NetProfitPercent = 0.08m
        };
        var untradableCost = new RangeExpansionCostMetrics
        {
            RequiredNetProfitPercent = 0.05m,
            Lock90NetProfitPercent = 0.02m
        };

        Assert.Equal(
            RangeExpansionBreakoutV1Model.TargetTooSmallButNetTradable,
            RangeExpansionCostModel.ClassifyTargetTooSmallRejection(tradableCost, 0.30m));
        Assert.Equal(
            RangeExpansionBreakoutV1Model.TargetTooSmallAndFeeUntradable,
            RangeExpansionCostModel.ClassifyTargetTooSmallRejection(untradableCost, 0.30m));
    }

    [Fact]
    public void BuildExecutedTradeCostAnalysis_CountsProfitLockGrossPositiveNetNegative()
    {
        var candidates = new[]
        {
            SampleCandidate("30d", executed: true, symbol: TradingSymbol.ETHUSDT, net: -0.01m) with
            {
                IsProfitLockExit = true,
                GrossPnlQuote = 0.02m,
                NetPnlQuote = -0.01m,
                ExpectedMovePercent = 0.12m,
                Lock90NetProfitPercent = 0.03m,
                RequiredNetProfitPercent = 0.05m
            }
        };

        var analysis = RangeExpansionDiagnosticsAggregator.BuildExecutedTradeCostAnalysis(candidates);

        Assert.Equal(1, analysis.ProfitLockGrossPositiveNetNegativeCount);
        Assert.Equal(1, analysis.ExpectedMovePassedLock90BelowRequiredNetCount);
    }

    [Fact]
    public void BuildRangeExpansionBreakoutV2Profiles_GeneratesExpectedNames()
    {
        var profiles = BacktestApplication.BuildRangeExpansionBreakoutV2Profiles(includeComparisonProfiles: true);

        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v2-ETH-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v2-BNB-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v2-SOL-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v2-ETH+BNB+SOL-1m-profitlock-90");
        Assert.Equal("true", profiles[0].ConfigOverrides["Backtest:RangeExpansionBreakoutV2:Enabled"]);
        Assert.Equal("false", profiles[0].ConfigOverrides["Backtest:RangeExpansionBreakoutV1:Enabled"]);
    }

    [Fact]
    public void BuildRangeExpansionV2FeasibilityProfiles_GeneratesBnbBody80Variants()
    {
        var profiles = BacktestApplication.BuildRangeExpansionV2FeasibilityProfiles();

        Assert.Equal(3, profiles.Count);
        Assert.All(profiles, p => Assert.Equal(TradingSymbol.BNBUSDT, p.Symbols[0]));
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v2-feasibility-bnb-body80-halflock-current-1m-profitlock90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v2-feasibility-bnb-body80-halflock-costcover-1m-profitlock90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v2-feasibility-bnb-failed-breakout-ref-halflock-1m-profitlock90");
    }

    [Fact]
    public void FeasibilityCostModel_RecalculatesNetWithSlippageAndFunding()
    {
        var trade = new SimulatedTrade
        {
            EntryPrice = 600m,
            ExitPrice = 603m,
            Quantity = 0.03m,
            GrossPnlQuote = 0.09m,
            DurationMinutes = 60m
        };

        var lowCost = new FeasibilityCostScenario("low", "spot", 0.05m, 0.03m, 0m, 0m);
        var withSlip = new FeasibilityCostScenario("slip", "spot", 0.05m, 0.03m, 0.05m, 0m);
        var futuresSim = new FeasibilityCostScenario("futures", "futures-sim", 0.05m, 0.03m, 0.05m, 0.01m);

        Assert.True(RangeExpansionV2FeasibilityCostModel.RecalculateNetPnl(trade, lowCost) > RangeExpansionV2FeasibilityCostModel.RecalculateNetPnl(trade, withSlip));
        Assert.True(RangeExpansionV2FeasibilityCostModel.RecalculateNetPnl(trade, withSlip) > RangeExpansionV2FeasibilityCostModel.RecalculateNetPnl(trade, futuresSim));
    }

    [Fact]
    public void BuildRangeExpansionV24Profiles_GeneratesBody80ExitPolicyMatrix()
    {
        var profiles = BacktestApplication.BuildRangeExpansionV24Profiles();

        Assert.Equal(11, profiles.Count);
        Assert.All(profiles, p => Assert.Equal(TradingSymbol.BNBUSDT, p.Symbols[0]));
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v24-bnb-v24-body80-halflock-current-1m-profitlock90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v24-bnb-v24-body80-profitlock80-1m-profitlock80");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v24-bnb-v24-body80-no-progress-exit-30m-1m-profitlock90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v24-bnb-v24-body80-combo-profitlock85-costcover-1m-profitlock85");

        var body80 = profiles.Single(p => p.ProfileName.Contains("halflock-current", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("80", body80.ConfigOverrides["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:FailedBreakoutMinBreakoutBodyStrengthPercent"]);
        Assert.Equal("true", body80.ConfigOverrides["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"]);

        var costCover = profiles.Single(p => p.ProfileName.Contains("costcover-breakeven", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("true", costCover.ConfigOverrides["Backtest:ExitPolicy:EnableCostCoveredBreakevenExit"]);

        var noProgress = profiles.Single(p => p.ProfileName.Contains("no-progress-exit-20m", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("20", noProgress.ConfigOverrides["Backtest:ExitPolicy:NoProgressExitMinutes"]);
    }

    [Fact]
    public void BuildRangeExpansionV23Profiles_GeneratesFailedBreakoutSweepMatrix()
    {
        var profiles = BacktestApplication.BuildRangeExpansionV23Profiles();

        Assert.Equal(22, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v23-bnb-baseline-halflock-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v23-bnb-failed-breakout-ref-halflock-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v23-bnb-sweep-giveback-30-halflock-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v23-bnb-sweep-body-84-halflock-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v23-bnb-combo-giveback30-follow60-halflock-1m-profitlock-90");

        var bodySweep = profiles.Single(p => p.ProfileName.Contains("sweep-body-84", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("84", bodySweep.ConfigOverrides["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:FailedBreakoutMinBreakoutBodyStrengthPercent"]);
    }

    [Fact]
    public void CostSensitivity_RecalculatesNetUnderLowerFees()
    {
        var trade = new SimulatedTrade
        {
            EntryPrice = 600m,
            ExitPrice = 603m,
            Quantity = 0.001m,
            GrossPnlQuote = 0.003m,
            NetPnlQuote = -0.001m,
            FeeAndSpreadEstimateQuote = 0.004m
        };

        var optimistic = RangeExpansionCostSensitivity.RecalculateNetPnl(trade, RangeExpansionCostSensitivity.Optimistic);
        Assert.True(optimistic > trade.NetPnlQuote);
    }

    [Fact]
    public void BuildRangeExpansionV22Profiles_GeneratesSeparatorExperimentVariants()
    {
        var profiles = BacktestApplication.BuildRangeExpansionV22Profiles();

        Assert.Equal(7, profiles.Count);
        Assert.All(profiles, p => Assert.Equal(TradingSymbol.BNBUSDT, p.Symbols[0]));
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v22-bnb-baseline-halflock-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v22-bnb-combined-filter-halflock-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v22-bnb-timestop-fee-aware-halflock-1m-profitlock-90");

        var baseline = profiles.Single(p => p.ProfileName.Contains("baseline-halflock", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("true", baseline.ConfigOverrides["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"]);

        var combined = profiles.Single(p => p.ProfileName.Contains("combined-filter-halflock", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("true", combined.ConfigOverrides["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:Enabled"]);
        Assert.Equal("true", combined.ConfigOverrides["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableInflationFilter"]);
    }

    [Fact]
    public void BuildRangeExpansionV21FastProfiles_GeneratesBnbMatrixVariants()
    {
        var profiles = BacktestApplication.BuildRangeExpansionV21FastProfiles();

        Assert.Equal(6, profiles.Count);
        Assert.All(profiles, p => Assert.Single(p.Symbols));
        Assert.All(profiles, p => Assert.Equal(TradingSymbol.BNBUSDT, p.Symbols[0]));
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v21-bnb-baseline-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v21-bnb-timestop-30-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v21-bnb-halflock-breakeven-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v21-bnb-tighter-entry-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v21-bnb-tighter-entry-halflock-breakeven-1m-profitlock-90");
        Assert.Contains(profiles, p => p.ProfileName == "range-expansion-v21-bnb-tighter-entry-timestop-30-1m-profitlock-90");

        var tighter = profiles.Single(p => p.ProfileName.Contains("tighter-entry-1m", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("true", tighter.ConfigOverrides["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:Enabled"]);

        var combo = profiles.Single(p => p.ProfileName.Contains("tighter-entry-halflock-breakeven", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("true", combo.ConfigOverrides["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:Enabled"]);
        Assert.Equal("true", combo.ConfigOverrides["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"]);
    }

    [Fact]
    public void V2DiagnosticsAggregator_ClassifiesExitOutcomeBuckets()
    {
        var candidates = new[]
        {
            SampleV2Candidate("ProfitLock", 5m, 4m, 10m) with { Lock90DistancePercent = 0.5m },
            SampleV2Candidate("TimeStop", -0.01m, 0.02m, 30m) with { Lock90DistancePercent = 0.5m },
            SampleV2Candidate("TimeStop", -0.2m, -0.1m, 60m) with { Lock90DistancePercent = 0.5m },
            SampleV2Candidate("StopLoss", -0.3m, -0.2m, 15m) with { Lock90DistancePercent = 0.5m }
        };
        var trades = candidates.Select((c, i) => new SimulatedTrade
        {
            ProfileName = c.ProfileName,
            Interval = c.Interval,
            Symbol = c.Symbol,
            NetPnlQuote = c.NetPnlQuote ?? 0m,
            GrossPnlQuote = c.GrossPnlQuote ?? 0m,
            ExitReason = c.ExitReason,
            DurationMinutes = c.DurationMinutes
        }).ToArray();

        var extended = RangeExpansionV2DiagnosticsAggregator.BuildExtended(candidates, trades, includeV21Summary: false);

        Assert.Contains(extended.ExitOutcomeComparison, r => r.Bucket == "ProfitLockWinners" && r.Count == 1);
        Assert.Contains(extended.ExitOutcomeComparison, r => r.Bucket == "TimeStopGrossPositiveNetNegative" && r.Count == 1);
        Assert.Contains(extended.ExitOutcomeComparison, r => r.Bucket == "TimeStopGrossNegative" && r.Count == 1);
        Assert.Contains(extended.ExitOutcomeComparison, r => r.Bucket == "StopLossEarlyFailures" && r.Count == 1);
        Assert.NotEmpty(extended.FailureTiming);
        Assert.Contains(extended.SymbolExitBreakdown, r => r.Symbol == TradingSymbol.BNBUSDT);
    }

    private static RangeExpansionV2CandidateRecord SampleV2Candidate(
        string exitReason,
        decimal net,
        decimal gross,
        decimal durationMinutes)
        => new()
        {
            WindowLabel = "30d",
            Interval = "1m",
            ProfileName = "range-expansion-v21-bnb-baseline-1m-profitlock-90",
            Symbols = "BNBUSDT",
            Symbol = TradingSymbol.BNBUSDT,
            TimeUtc = DateTime.UtcNow,
            Executed = true,
            EntryPrice = 600m,
            RangeWidthPercent = 0.3m,
            AtrPercent = 0.08m,
            AtrExpansionRatio = 1.1m,
            VolumeExpansionRatio = 1.4m,
            NetPnlQuote = net,
            GrossPnlQuote = gross,
            ExitReason = exitReason,
            DurationMinutes = durationMinutes,
            MfePercent = 0.2m,
            MaePercent = -0.1m
        };

    [Fact]
    public void BuildRangeExpansionTargetFloorExperimentProfiles_GeneratesThreeModes()
    {
        var profiles = BacktestApplication.BuildRangeExpansionTargetFloorExperimentProfiles(includeComparisonProfiles: false);

        Assert.Contains(profiles, p => p.ProfileName.Contains("-targetfloor-current-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profiles, p => p.ProfileName.Contains("-targetfloor-relaxed-", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profiles, p => p.ProfileName.Contains("-targetfloor-costaware-", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(9, profiles.Count);
    }

    [Fact]
    public void Build_GeneratesDiagnosticAnswers()
    {
        var candidates = new[]
        {
            SampleCandidate("30d", executed: true, symbol: TradingSymbol.BNBUSDT, net: -0.1m) with { IsWinner = false },
            SampleCandidate("30d", executed: false, symbol: TradingSymbol.BNBUSDT, net: null) with
            {
                RejectionReason = RangeExpansionBreakoutV1Model.FollowThroughFailed,
                Lock90ReachableWithin60m = true,
                ForwardMfe60Percent = 0.3m
            }
        };
        var trades = new[] { SampleTrade(candidates[0]) };
        var bundle = RangeExpansionDiagnosticsAggregator.Build(candidates, trades);

        Assert.NotEmpty(bundle.PnlDecomposition);
        Assert.NotEmpty(bundle.TradeQualityBuckets);
        Assert.NotEmpty(bundle.DiagnosticAnswers);
        Assert.Contains(bundle.DiagnosticAnswers, a => a.Question.Contains("symbol/interval/profitlock", StringComparison.OrdinalIgnoreCase));
    }

    private static RangeExpansionCandidateRecord SampleCandidate(
        string window,
        bool executed,
        TradingSymbol symbol,
        decimal? net)
        => new()
        {
            WindowLabel = window,
            Interval = "1m",
            ProfileName = $"range-expansion-v1-fast-{symbol.ToString().Replace("USDT", "", StringComparison.Ordinal)}-1m-profitlock-90",
            Symbols = symbol.ToString(),
            Symbol = symbol,
            TimeUtc = DateTime.UtcNow,
            Executed = executed,
            EntryPrice = 100m,
            RangeWidthPercent = 0.25m,
            BreakoutBufferPercent = 0.02m,
            BreakoutClose = 100.5m,
            AtrPercent = 0.15m,
            ExpectedMovePercent = 0.30m,
            Lock90DistancePercent = 0.27m,
            EstimatedRoundTripCostPercent = 0.25m,
            RequiredNetProfitPercent = 0.05m,
            RequiredGrossProfitPercent = 0.30m,
            Lock90NetProfitPercent = 0.02m,
            NetPnlQuote = net,
            ProfitLockThresholdPercent = 90m,
            ExitReason = executed ? "ProfitLock" : null
        };

    private static SimulatedTrade SampleTrade(RangeExpansionCandidateRecord candidate)
        => new()
        {
            ProfileName = candidate.ProfileName,
            Interval = candidate.Interval,
            Symbol = candidate.Symbol,
            EntryTimeUtc = candidate.TimeUtc,
            NetPnlQuote = candidate.NetPnlQuote ?? 0m,
            GrossPnlQuote = candidate.NetPnlQuote ?? 0m,
            ExitReason = candidate.ExitReason ?? "ProfitLock",
            ProfitLockThresholdPercent = candidate.ProfitLockThresholdPercent
        };
}
