using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace TradingBot.Backtest;

public static class FuturesTestnetShadowKeySafety
{
    public const string SafetyBlockedRealKeysDetected = "SafetyBlockedRealKeysDetected";
    public const string MissingTestnetKeysButShadowOk = "MissingTestnetKeysButShadowOk";
    public const string TestnetKeysPresent = "TestnetKeysPresent";
    public const string NoTradingKeysConfigured = "NoTradingKeysConfigured";

    public sealed record KeySafetyResult(
        string Status,
        bool RealKeysDetected,
        bool TestnetKeysPresent,
        bool BlockShadowRun,
        string Notes);

    public static KeySafetyResult Evaluate(string appSettingsPath, FuturesTestnetShadowSettings shadowSettings)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var platformPath = Path.Combine(repoRoot, "TradingBot", "platformSettings.json");
        string? baseUrl = null;
        string? apiKey = null;
        string? secretKey = null;

        if (File.Exists(platformPath))
        {
            try
            {
                var platformJson = JsonDocument.Parse(File.ReadAllText(platformPath));
                if (platformJson.RootElement.TryGetProperty("BaseURL", out var urlEl))
                    baseUrl = urlEl.GetString();
                if (platformJson.RootElement.TryGetProperty("ApiKey", out var keyEl))
                    apiKey = keyEl.GetString();
                if (platformJson.RootElement.TryGetProperty("SecretKey", out var secretEl))
                    secretKey = secretEl.GetString();
            }
            catch
            {
                // Non-fatal; env vars may still apply.
            }
        }

        var envApiKey = FirstNonEmpty(
            Environment.GetEnvironmentVariable("BINANCE_API_KEY"),
            Environment.GetEnvironmentVariable("BINANCE_FUTURES_API_KEY"));
        var envSecret = FirstNonEmpty(
            Environment.GetEnvironmentVariable("BINANCE_SECRET_KEY"),
            Environment.GetEnvironmentVariable("BINANCE_FUTURES_SECRET_KEY"));
        var envBaseUrl = FirstNonEmpty(
            Environment.GetEnvironmentVariable("BINANCE_BASE_URL"),
            Environment.GetEnvironmentVariable("BINANCE_FUTURES_BASE_URL"));

        apiKey = FirstNonEmpty(shadowSettings.TestnetApiKey, envApiKey, apiKey);
        secretKey = FirstNonEmpty(shadowSettings.TestnetSecretKey, envSecret, secretKey);
        baseUrl = FirstNonEmpty(shadowSettings.TestnetBaseUrl, envBaseUrl, baseUrl);

        var futuresTestnetKey = FirstNonEmpty(
            shadowSettings.TestnetApiKey,
            Environment.GetEnvironmentVariable("BINANCE_FUTURES_TESTNET_API_KEY"));
        var futuresTestnetSecret = FirstNonEmpty(
            shadowSettings.TestnetSecretKey,
            Environment.GetEnvironmentVariable("BINANCE_FUTURES_TESTNET_SECRET_KEY"));
        var futuresTestnetUrl = FirstNonEmpty(
            shadowSettings.TestnetBaseUrl,
            Environment.GetEnvironmentVariable("BINANCE_FUTURES_TESTNET_BASE_URL"));

        var hasCredentials = HasCredentials(apiKey, secretKey);
        var realHost = IsRealBinanceHost(baseUrl);
        var realKeysDetected = hasCredentials && realHost;

        var testnetKeysPresent = HasCredentials(futuresTestnetKey, futuresTestnetSecret)
            || (hasCredentials && IsTestnetBinanceHost(baseUrl));

        if (realKeysDetected)
        {
            return new KeySafetyResult(
                SafetyBlockedRealKeysDetected,
                RealKeysDetected: true,
                testnetKeysPresent,
                BlockShadowRun: true,
                "Real Binance API credentials with a non-testnet BaseURL were detected. Shadow runner refuses to proceed.");
        }

        if (!testnetKeysPresent)
        {
            return new KeySafetyResult(
                MissingTestnetKeysButShadowOk,
                RealKeysDetected: false,
                TestnetKeysPresent: false,
                BlockShadowRun: false,
                "No futures testnet API keys configured; shadow evaluation proceeds without any order endpoint calls.");
        }

        return new KeySafetyResult(
            TestnetKeysPresent,
            RealKeysDetected: false,
            TestnetKeysPresent: true,
            BlockShadowRun: false,
            "Futures testnet credentials detected. Order placement remains disabled unless AllowTestnetOrders=true and DryRunOnly=false.");
    }

    private static bool HasCredentials(string? apiKey, string? secretKey)
        => !string.IsNullOrWhiteSpace(apiKey) && !string.IsNullOrWhiteSpace(secretKey);

    private static bool IsRealBinanceHost(string? url)
        => !string.IsNullOrWhiteSpace(url)
           && url.Contains("binance.com", StringComparison.OrdinalIgnoreCase)
           && !url.Contains("testnet", StringComparison.OrdinalIgnoreCase);

    private static bool IsTestnetBinanceHost(string? url)
        => !string.IsNullOrWhiteSpace(url)
           && url.Contains("testnet", StringComparison.OrdinalIgnoreCase);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
