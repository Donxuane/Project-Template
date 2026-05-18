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
}
