using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class ImpulseContinuationV1Tests
{
    [Fact]
    public void BuildImpulseContinuationV1Profiles_GeneratesCoreNineProfiles()
    {
        var profiles = BacktestApplication.BuildImpulseContinuationV1Profiles(includeResearchVariants: false);

        Assert.Equal(9, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "impulse-continuation-v1-ETH-1m-lock90");
        Assert.Contains(profiles, p => p.ProfileName == "impulse-continuation-v1-BNB-3m-lock90");
        Assert.Contains(profiles, p => p.ProfileName == "impulse-continuation-v1-SOL-5m-lock90");
        Assert.All(profiles, p =>
        {
            Assert.Equal("true", p.ConfigOverrides["Backtest:ImpulseContinuationV1:Enabled"]);
            Assert.Equal("false", p.ConfigOverrides["Backtest:RangeExpansionBreakoutV2:Enabled"]);
        });
    }

    [Fact]
    public void BuildImpulseContinuationV1Profiles_IncludesResearchVariantsWhenRequested()
    {
        var profiles = BacktestApplication.BuildImpulseContinuationV1Profiles(includeResearchVariants: true);

        Assert.True(profiles.Count > 9);
        Assert.Contains(profiles, p => p.ProfileName == "impulse-continuation-v1-ETH-5m-net15-lock90");
        Assert.Contains(profiles, p => p.ProfileName == "impulse-continuation-v1-BNB-5m-hold30-lock90");
        Assert.Contains(profiles, p => p.ProfileName == "impulse-continuation-v1-SOL-1m-atr-expand-lock90");
        Assert.Contains(profiles, p => p.ProfileName == "impulse-continuation-v1-ETH-5m-midpoint-stop-lock90");
    }

    [Fact]
    public void ResolveImpulseContinuationV1ProfileInterval_ReadsIntervalFromProfileName()
    {
        var profile = BacktestApplication.BuildImpulseContinuationV1Profiles(false)
            .First(p => p.ProfileName.Contains("-5m-", StringComparison.Ordinal));

        Assert.Equal("5m", BacktestApplication.ResolveImpulseContinuationV1ProfileInterval(profile));
    }

    [Fact]
    public void BuildResearchAnswers_CoversImpulseQuestions()
    {
        var candidates = new[]
        {
            SampleCandidate(executed: true, net: -0.1m) with { Lock90MeetsRequiredGross = true, Lock90ReachableWithin60m = true },
            SampleCandidate(executed: false, net: null) with { Lock90MeetsRequiredGross = true, RejectionReason = ImpulseContinuationV1Model.LockBelowRequiredGross }
        };
        var trades = new[] { SampleTrade(candidates[0]) };
        var summaries = ImpulseContinuationV1Aggregator.BuildSummaries("90d", candidates, trades);
        var exitBreakdown = ImpulseContinuationV1Aggregator.BuildExitBreakdown(trades);
        var windowRobustness = ImpulseContinuationV1Aggregator.BuildWindowRobustness(summaries, trades);

        var answers = ImpulseContinuationV1Aggregator.BuildResearchAnswers(
            candidates, trades, summaries, exitBreakdown, windowRobustness);

        Assert.Equal(7, answers.Count);
        Assert.Contains(answers, a => a.Question.Contains("repeated cost-aware impulse candidates", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("bigger average gross move", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildWindowRobustness_AggregatesAcrossWindows()
    {
        var summaries = new[]
        {
            new ImpulseContinuationV1SummaryRow
            {
                WindowLabel = "30d",
                ProfileName = "impulse-continuation-v1-ETH-1m-lock90",
                Symbol = TradingSymbol.ETHUSDT,
                Interval = "1m",
                CandidateCount = 4,
                TradesCount = 2,
                EstimatedNetPnlQuote = -0.1m,
                Lock90MeetsRequiredGrossCount = 3
            },
            new ImpulseContinuationV1SummaryRow
            {
                WindowLabel = "60d",
                ProfileName = "impulse-continuation-v1-ETH-1m-lock90",
                Symbol = TradingSymbol.ETHUSDT,
                Interval = "1m",
                CandidateCount = 5,
                TradesCount = 3,
                EstimatedNetPnlQuote = -0.2m,
                Lock90MeetsRequiredGrossCount = 4
            }
        };

        var rows = ImpulseContinuationV1Aggregator.BuildWindowRobustness(summaries, []);
        Assert.Single(rows);
        Assert.Equal(4, rows[0].Window30dCandidates);
        Assert.Equal(5, rows[0].Window60dCandidates);
    }

    private static ImpulseContinuationV1CandidateRecord SampleCandidate(bool executed, decimal? net)
        => new()
        {
            WindowLabel = "90d",
            Interval = "1m",
            ProfileName = "impulse-continuation-v1-ETH-1m-lock90",
            Symbols = "ETHUSDT",
            Symbol = TradingSymbol.ETHUSDT,
            TimeUtc = DateTime.UtcNow,
            Executed = executed,
            EntryPrice = 100m,
            ImpulseBodyStrengthPercent = 70m,
            ImpulseRangePercent = 0.25m,
            ImpulseRangeVsAverage = 1.5m,
            VolumeExpansionRatio = 1.4m,
            CloseNearHighPercent = 5m,
            ExpectedMovePercent = 0.45m,
            Lock90DistancePercent = 0.40m,
            EstimatedRoundTripCostPercent = 0.25m,
            RequiredNetProfitPercent = 0.10m,
            RequiredGrossMovePercent = 0.35m,
            Lock90NetProfitPercent = 0.15m,
            NetPnlQuote = net,
            ExitReason = executed ? "StopLoss" : null
        };

    private static SimulatedTrade SampleTrade(ImpulseContinuationV1CandidateRecord candidate)
        => new()
        {
            ProfileName = candidate.ProfileName,
            Interval = candidate.Interval,
            Symbol = candidate.Symbol,
            EntryTimeUtc = candidate.TimeUtc,
            NetPnlQuote = candidate.NetPnlQuote ?? 0m,
            GrossPnlQuote = candidate.NetPnlQuote ?? 0m,
            ExitReason = candidate.ExitReason ?? "StopLoss"
        };
}
