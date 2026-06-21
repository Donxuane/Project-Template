using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class HigherTimeframeMomentumPullbackV1Tests
{
    [Fact]
    public void BuildHigherTimeframeMomentumPullbackV1Profiles_GeneratesCoreSixProfiles()
    {
        var profiles = BacktestApplication.BuildHigherTimeframeMomentumPullbackV1Profiles(includeResearchVariants: false);

        Assert.Equal(6, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "htf-momentum-v1-ETH-15m-hybrid-target");
        Assert.Contains(profiles, p => p.ProfileName == "htf-momentum-v1-SOL-30m-hybrid-target");
        Assert.All(profiles, p =>
        {
            Assert.Equal("true", p.ConfigOverrides["Backtest:HigherTimeframeMomentumPullbackV1:Enabled"]);
            Assert.Equal("false", p.ConfigOverrides["Backtest:ImpulseContinuationV1:Enabled"]);
            Assert.Equal("false", p.ConfigOverrides["Backtest:MeanReversionRangeBounceV1:Enabled"]);
        });
    }

    [Fact]
    public void BuildHigherTimeframeMomentumPullbackV1Profiles_IncludesResearchVariantsWhenRequested()
    {
        var profiles = BacktestApplication.BuildHigherTimeframeMomentumPullbackV1Profiles(includeResearchVariants: true);

        Assert.True(profiles.Count > 6);
        Assert.Contains(profiles, p => p.ProfileName == "htf-momentum-v1-BNB-15m-net30-hybrid-target");
        Assert.Contains(profiles, p => p.ProfileName == "htf-momentum-v1-ETH-30m-swing-target");
        Assert.Contains(profiles, p => p.ProfileName == "htf-momentum-v1-SOL-15m-hold12h-hybrid-target");
        Assert.Contains(profiles, p => p.ProfileName == "htf-momentum-v1-BNB-30m-lock90-hybrid-target");
    }

    [Fact]
    public void BuildResearchAnswers_CoversHtfMomentumQuestions()
    {
        var candidates = new[]
        {
            SampleCandidate(executed: true, net: 0.05m),
            SampleCandidate(executed: false, net: null) with
            {
                RejectionReason = HigherTimeframeMomentumPullbackV1Model.TargetBelowRequiredGross
            }
        };
        var trades = new[] { SampleTrade(candidates[0]) };
        var summaries = HigherTimeframeMomentumPullbackV1Aggregator.BuildSummaries("90d", candidates, trades);
        var exitBreakdown = HigherTimeframeMomentumPullbackV1Aggregator.BuildExitBreakdown(trades);
        var windowRobustness = HigherTimeframeMomentumPullbackV1Aggregator.BuildWindowRobustness(summaries);

        var answers = HigherTimeframeMomentumPullbackV1Aggregator.BuildResearchAnswers(
            candidates, trades, summaries, exitBreakdown, windowRobustness);

        Assert.Equal(6, answers.Count);
        Assert.Contains(answers, a => a.Question.Contains("higher timeframes produce larger gross edge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("stop-loss rate lower", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("market edge or by execution costs", StringComparison.OrdinalIgnoreCase));
    }

    private static HigherTimeframeMomentumPullbackV1CandidateRecord SampleCandidate(bool executed, decimal? net)
        => new()
        {
            WindowLabel = "90d",
            Interval = "15m",
            ProfileName = "htf-momentum-v1-ETH-15m-hybrid-target",
            Symbols = "ETHUSDT",
            Symbol = TradingSymbol.ETHUSDT,
            TimeUtc = DateTime.UtcNow,
            Executed = executed,
            TrendSlopePercent = 0.05m,
            TrendStrengthPercent = 0.12m,
            PullbackDepthPercent = 0.45m,
            DistanceToMaPercent = 0.20m,
            ReclaimConfirmed = executed,
            ExpectedMovePercent = 1.20m,
            RequiredGrossMovePercent = 0.70m,
            StopDistancePercent = 0.35m,
            RewardRisk = 3.43m,
            TargetModelName = "HybridMinReasonableTarget",
            ForwardMfe8hPercent = 1.50m,
            NetPnlQuote = net,
            ExitReason = executed ? "ProfitTarget" : null
        };

    private static SimulatedTrade SampleTrade(HigherTimeframeMomentumPullbackV1CandidateRecord candidate)
        => new()
        {
            ProfileName = candidate.ProfileName,
            Interval = candidate.Interval,
            Symbol = candidate.Symbol,
            EntryTimeUtc = candidate.TimeUtc,
            NetPnlQuote = candidate.NetPnlQuote ?? 0m,
            GrossPnlQuote = candidate.NetPnlQuote ?? 0m,
            ExitReason = candidate.ExitReason ?? "ProfitTarget",
            ProfitLockThresholdPercent = 100m,
            ProjectionMode = "HybridMinReasonableTarget"
        };
}
