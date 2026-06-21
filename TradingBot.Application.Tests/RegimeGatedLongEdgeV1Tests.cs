using Microsoft.Extensions.Configuration;
using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class RegimeGatedLongEdgeV1Tests
{
    [Fact]
    public void BuildRegimeGatedLongEdgeV1Profiles_CoreMatrix_HasNineBnbProfiles()
    {
        var profiles = BacktestApplication.BuildRegimeGatedLongEdgeV1Profiles(includeResearchVariants: false);

        Assert.Equal(9, profiles.Count);
        Assert.All(profiles, p => Assert.Contains(TradingSymbol.BNBUSDT, p.Symbols));
        Assert.Contains(profiles, p => p.ProfileName.Contains("bnb30m-baseline", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profiles, p => p.ProfileName.Contains("elevated-vol-market-return", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(profiles, p => p.ProfileName.Contains("wide-range-near-low", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WideRangeNearLowGate_UsesStudyBounds()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Backtest:RegimeGatedLongEdgeV1:Enabled"] = "true",
                ["Backtest:RegimeGatedLongEdgeV1:RegimeGateName"] = "WideRangeNearLowGate",
                ["Backtest:RegimeGatedLongEdgeV1:RangeWidthMinPercent"] = "0.8608",
                ["Backtest:RegimeGatedLongEdgeV1:RangeWidthMaxPercent"] = "10.5309",
                ["Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMinPercent"] = "0",
                ["Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMaxPercent"] = "0.1671"
            })
            .Build();

        var model = new RegimeGatedLongEdgeV1Model(config);
        Assert.Equal(RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate.ToString(), model.GateName);
    }

    [Fact]
    public void BuildResearchAnswers_CoversRegimeGatedQuestions()
    {
        var trades = new[]
        {
            SampleTrade("Bnb30mUnconditionalPositiveBaseline", TradingSymbol.BNBUSDT, "30m", net: -0.01m),
            SampleTrade("ElevatedVolMarketReturnGate", TradingSymbol.BNBUSDT, "30m", net: -0.02m),
            SampleTrade("WideRangeNearLowGate", TradingSymbol.BNBUSDT, "30m", net: -0.03m)
        };
        var summaries = RegimeGatedLongEdgeV1Aggregator.BuildSummaries("90d", trades, []);
        var rulePerformance = RegimeGatedLongEdgeV1Aggregator.BuildRulePerformance(trades);
        var windowRobustness = RegimeGatedLongEdgeV1Aggregator.BuildWindowRobustness(summaries);
        var answers = RegimeGatedLongEdgeV1Aggregator.BuildResearchAnswers(
            trades, summaries, rulePerformance, windowRobustness, [],
            0.35m, BacktestApplication.ResolveRegimeGatedLongEdgeV1GateThresholds());

        Assert.True(answers.Count >= 8);
        Assert.Contains(answers, a => a.Question.Contains("BNB 30m unconditional edge survive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("BTC context", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(answers, a => a.Question.Contains("continue Spot long-only research or pause", StringComparison.OrdinalIgnoreCase));
    }

    private static RegimeGatedLongEdgeV1TradeRecord SampleTrade(
        string ruleName,
        TradingSymbol symbol,
        string interval,
        decimal net)
        => new()
        {
            WindowLabel = "90d",
            RuleName = ruleName,
            ProfileName = $"regime-gated-v1-{ruleName}",
            Symbol = symbol,
            Interval = interval,
            TimeUtc = DateTime.UtcNow,
            EntryPrice = 100m,
            ExitPrice = 100m,
            ExitReason = "TimeStop",
            TargetPercent = 0.50m,
            StopPercent = 0.50m,
            TimeStopHours = 8m,
            GrossPnlQuote = net,
            NetPnlQuote = net,
            EntryConfirmationMode = "NextClose"
        };
}
