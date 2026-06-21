using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class MeanReversionRangeBounceV1Tests
{
    [Fact]
    public void BuildMeanReversionRangeBounceV1Profiles_GeneratesCoreNineProfiles()
    {
        var profiles = BacktestApplication.BuildMeanReversionRangeBounceV1Profiles(includeResearchVariants: false);

        Assert.Equal(9, profiles.Count);
        Assert.Contains(profiles, p => p.ProfileName == "mean-reversion-range-v1-ETH-1m-midpoint-target");
        Assert.Contains(profiles, p => p.ProfileName == "mean-reversion-range-v1-BNB-5m-midpoint-target");
        Assert.All(profiles, p =>
        {
            Assert.Equal("true", p.ConfigOverrides["Backtest:MeanReversionRangeBounceV1:Enabled"]);
            Assert.Equal("false", p.ConfigOverrides["Backtest:ImpulseContinuationV1:Enabled"]);
        });
    }

    [Fact]
    public void BuildMeanReversionRangeBounceV1Profiles_IncludesResearchVariantsWhenRequested()
    {
        var profiles = BacktestApplication.BuildMeanReversionRangeBounceV1Profiles(includeResearchVariants: true);

        Assert.True(profiles.Count > 9);
        Assert.Contains(profiles, p => p.ProfileName == "mean-reversion-range-v1-ETH-3m-lookback20-midpoint-target");
        Assert.Contains(profiles, p => p.ProfileName == "mean-reversion-range-v1-SOL-5m-rangehigh-target");
        Assert.Contains(profiles, p => p.ProfileName == "mean-reversion-range-v1-BNB-1m-rejection-stop-midpoint-target");
    }

    [Fact]
    public void BuildResearchAnswers_CoversRangeBounceQuestions()
    {
        var candidates = new[]
        {
            SampleCandidate(executed: true, net: 0.05m) with { TargetReachableWithin60m = true },
            SampleCandidate(executed: false, net: null) with { RejectionReason = MeanReversionRangeBounceV1Model.TargetBelowRequiredGross }
        };
        var trades = new[] { SampleTrade(candidates[0]) };
        var summaries = MeanReversionRangeBounceV1Aggregator.BuildSummaries("90d", candidates, trades);
        var exitBreakdown = MeanReversionRangeBounceV1Aggregator.BuildExitBreakdown(trades);
        var windowRobustness = MeanReversionRangeBounceV1Aggregator.BuildWindowRobustness(summaries);

        var answers = MeanReversionRangeBounceV1Aggregator.BuildResearchAnswers(
            candidates, trades, summaries, exitBreakdown, windowRobustness);

        Assert.Equal(7, answers.Count);
        Assert.Contains(answers, a => a.Question.Contains("StopLoss/ProfitTarget balance", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("midpoint target outperform", StringComparison.OrdinalIgnoreCase));
    }

    private static MeanReversionRangeBounceV1CandidateRecord SampleCandidate(bool executed, decimal? net)
        => new()
        {
            WindowLabel = "90d",
            Interval = "5m",
            ProfileName = "mean-reversion-range-v1-ETH-5m-midpoint-target",
            Symbols = "ETHUSDT",
            Symbol = TradingSymbol.ETHUSDT,
            TimeUtc = DateTime.UtcNow,
            Executed = executed,
            RangeHigh = 101m,
            RangeLow = 99m,
            RangeMidpoint = 100m,
            RangeWidthPercent = 0.5m,
            DistanceToRangeLowPercent = 0.1m,
            DistanceToRangeHighPercent = 0.4m,
            ExpectedMovePercent = 0.35m,
            RequiredGrossMovePercent = 0.35m,
            StopDistancePercent = 0.2m,
            RewardRisk = 1.75m,
            NetPnlQuote = net,
            ExitReason = executed ? "ProfitTarget" : null
        };

    private static SimulatedTrade SampleTrade(MeanReversionRangeBounceV1CandidateRecord candidate)
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
            ProjectionMode = "RangeMidpointTarget"
        };
}
