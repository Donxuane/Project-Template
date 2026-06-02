using Microsoft.Extensions.Configuration;
using TradingBot.Shared.Configuration;
using Xunit;

namespace TradingBot.Application.Tests;

public class RuntimeTradingConfigResolverTests
{
    [Fact]
    public void ResolveConfidenceThreshold_SpotCloseLong_PrefersExitMinConfidenceOverSignalFloor()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecisionEngine:MinimumSignalConfidence"] = "0.25",
                ["DecisionEngine:MinConfidence"] = "0.70",
                ["DecisionEngine:ExitMinConfidence"] = "0.45",
                ["DecisionEngine:Strategies:MovingAverageCrossover:ExitMinConfidence"] = "0.45"
            })
            .Build();

        var resolution = RuntimeTradingConfigResolver.ResolveConfidenceThreshold(
            configuration,
            "MovingAverageCrossover",
            "Sell",
            "Spot",
            "CloseLong");

        Assert.Equal(0.45m, resolution.MinConfidence);
        Assert.Equal("ExitMinConfidence", resolution.ThresholdKind);
    }

    [Fact]
    public void FindRuntimeTradingKeysInPlatform_ReturnsDuplicateRuntimeKeys()
    {
        var app = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DecisionEngine:ExitMinConfidence"] = "0.45",
            ["DecisionEngine:MinConfidence"] = "0.70",
            ["Trading:Mode"] = "Spot"
        };
        var platform = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DecisionEngine:ExitMinConfidence"] = "0.50",
            ["Trading:Mode"] = "Spot",
            ["BaseURL"] = "https://testnet.binance.vision"
        };

        var duplicates = RuntimeTradingConfigResolver.FindRuntimeTradingKeysInPlatform(app, platform);

        Assert.Contains("DecisionEngine:ExitMinConfidence", duplicates);
        Assert.Contains("Trading:Mode", duplicates);
        Assert.DoesNotContain("BaseURL", duplicates);
    }

    [Fact]
    public void FindRuntimeTradingKeysInPlatform_IgnoresPlatformMetadataOnly()
    {
        var app = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DecisionEngine:ExitMinConfidence"] = "0.45"
        };
        var platform = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BaseURL"] = "https://testnet.binance.vision",
            ["Trading:NewOrder:api"] = "/api/v3/order"
        };

        var duplicates = RuntimeTradingConfigResolver.FindRuntimeTradingKeysInPlatform(app, platform);

        Assert.Empty(duplicates);
    }

    [Fact]
    public void ResolveMovingAverageStrategy_Defaults_NormalTrendTargetProjection_BackwardCompatible()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "8"
            })
            .Build();

        var settings = RuntimeTradingConfigResolver.ResolveMovingAverageStrategy(configuration);

        Assert.Equal(0.35m, settings.NormalTrendAtrExtensionMultiplier);
        Assert.Equal(0.35m, settings.NormalTrendStructureExtensionMultiplier);
        Assert.Equal(8, settings.NormalTrendExpectedTargetLookbackCandles);
        Assert.True(settings.NormalTrendUseMinAtrStructureExtension);
        Assert.False(settings.EnableNormalTrendRewardRiskFilter);
        Assert.Equal(0.80m, settings.NormalTrendMinExpectedRewardRisk);
        Assert.False(settings.EnableNormalTrendNearRecentHighRejection);
        Assert.Equal(1.20m, settings.NormalTrendNearRecentHighRequiresRewardRisk);
        Assert.Null(settings.NormalTrendNearRecentHighRequiresTrendStrengthPercent);
        Assert.False(settings.UseConfirmedClosedCandlesForEntryQuality);
        Assert.False(settings.UseConfirmedClosedCandlesForLowVolBreakout);
        Assert.False(settings.EnableNormalTrendPullbackContinuationOverride);
        Assert.Equal(0.80m, settings.NormalTrendPullbackMinExpectedRewardRisk);
        Assert.True(settings.NormalTrendPullbackRequireCloseAboveShortAndLongMa);
        Assert.True(settings.NormalTrendPullbackRequirePositiveShortSlope);
        Assert.True(settings.NormalTrendPullbackRejectPreviousBearishCandle);
    }

    [Fact]
    public void ResolveMovingAverageStrategy_ReadsCustom_NormalTrendTargetProjection_Settings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "8",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendAtrExtensionMultiplier"] = "0.50",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendStructureExtensionMultiplier"] = "0.80",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendExpectedTargetLookbackCandles"] = "16",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendUseMinAtrStructureExtension"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendRewardRiskFilter"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinExpectedRewardRisk"] = "0.95",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendNearRecentHighRejection"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresRewardRisk"] = "1.35",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresTrendStrengthPercent"] = "0.0045",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.10",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackRequireCloseAboveShortAndLongMa"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackRequirePositiveShortSlope"] = "false",
                ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackRejectPreviousBearishCandle"] = "false"
            })
            .Build();

        var settings = RuntimeTradingConfigResolver.ResolveMovingAverageStrategy(configuration);

        Assert.Equal(0.50m, settings.NormalTrendAtrExtensionMultiplier);
        Assert.Equal(0.80m, settings.NormalTrendStructureExtensionMultiplier);
        Assert.Equal(16, settings.NormalTrendExpectedTargetLookbackCandles);
        Assert.False(settings.NormalTrendUseMinAtrStructureExtension);
        Assert.True(settings.EnableNormalTrendRewardRiskFilter);
        Assert.Equal(0.95m, settings.NormalTrendMinExpectedRewardRisk);
        Assert.True(settings.EnableNormalTrendNearRecentHighRejection);
        Assert.Equal(1.35m, settings.NormalTrendNearRecentHighRequiresRewardRisk);
        Assert.Equal(0.0045m, settings.NormalTrendNearRecentHighRequiresTrendStrengthPercent);
        Assert.True(settings.UseConfirmedClosedCandlesForEntryQuality);
        Assert.True(settings.UseConfirmedClosedCandlesForLowVolBreakout);
        Assert.True(settings.EnableNormalTrendPullbackContinuationOverride);
        Assert.Equal(1.10m, settings.NormalTrendPullbackMinExpectedRewardRisk);
        Assert.False(settings.NormalTrendPullbackRequireCloseAboveShortAndLongMa);
        Assert.False(settings.NormalTrendPullbackRequirePositiveShortSlope);
        Assert.False(settings.NormalTrendPullbackRejectPreviousBearishCandle);
    }
}
