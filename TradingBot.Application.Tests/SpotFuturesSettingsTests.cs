using Microsoft.Extensions.Configuration;
using TradingBot.Application.SpotFuturesCrossMarket;
using Xunit;

namespace TradingBot.Application.Tests;

public sealed class SpotFuturesSettingsTests
{
    [Fact]
    public void TestnetSafety_AcceptsRecognizedHostAndDedicatedCredentials()
    {
        var settings = new SpotFuturesCrossMarketSettings
        {
            Enabled = true,
            AllowTestnetOrders = true
        };
        var configuration = BuildConfiguration(
            "https://demo-fapi.binance.com",
            "test-key",
            "test-secret");

        settings.ValidateTestnetSafety(configuration);
    }

    [Fact]
    public void TestnetSafety_RejectsMainnetHost()
    {
        var settings = new SpotFuturesCrossMarketSettings
        {
            Enabled = true,
            AllowTestnetOrders = true
        };
        var configuration = BuildConfiguration(
            "https://fapi.binance.com",
            "test-key",
            "test-secret");

        var error = Assert.Throws<InvalidOperationException>(
            () => settings.ValidateTestnetSafety(configuration));

        Assert.Contains(SpotFuturesCrossMarketSettings.UnsafeTestnetConfigurationError, error.Message);
    }

    [Fact]
    public void TestnetSafety_RequiresCredentialsWhenOrderingIsEnabled()
    {
        var settings = new SpotFuturesCrossMarketSettings
        {
            Enabled = true,
            AllowTestnetOrders = true
        };
        var configuration = BuildConfiguration(
            "https://demo-fapi.binance.com",
            string.Empty,
            string.Empty);

        var error = Assert.Throws<InvalidOperationException>(
            () => settings.ValidateTestnetSafety(configuration));

        Assert.Contains("ApiKey", error.Message);
    }

    private static IConfiguration BuildConfiguration(string baseUrl, string apiKey, string secretKey) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FuturesTestnet:BaseUrl"] = baseUrl,
                ["FuturesTestnet:ApiKey"] = apiKey,
                ["FuturesTestnet:SecretKey"] = secretKey
            })
            .Build();
}
