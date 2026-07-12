using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.TestnetExecution;

/// <summary>
/// Safety configuration for the ETH15 fixed-frequency forward-incubation Binance Futures
/// Testnet execution path. This is a testnet-validation phase, not production. Real-money
/// trading is impossible by construction: <see cref="AllowRealOrders"/> is a compile-time
/// false and <see cref="Load"/> throws if configuration attempts to enable it.
/// </summary>
public sealed class Eth15TestnetExecutionSettings
{
    public const string SectionName = "Eth15TestnetExecution";

    public const string RealOrdersForbiddenError = "Eth15TestnetExecutionRealOrdersForbidden";
    public const string MissingTestnetCredentialsError = "Eth15TestnetExecutionMissingTestnetCredentials";
    public const string NonTestnetBaseUrlError = "Eth15TestnetExecutionNonTestnetBaseUrl";
    public const string ProductionKeysDetectedError = "Eth15TestnetExecutionProductionKeysDetected";

    public const string ExecutionEnvironment = ExecutionEnvironments.BinanceFuturesTestnet;
    public const string ProfileName = "Frozen_ETH_NearExtremeShort_15m_T1.25S0.75_PerfRecentNetPositiveChk24hAct12hLB14d_FixedFrequencyV1";
    public const TradingSymbol Symbol = TradingSymbol.ETHUSDT;

    public static readonly IReadOnlyList<string> AllowedTestnetHosts = new[] { "testnet.binancefuture.com" };
    public static readonly IReadOnlyList<string> ForbiddenMainnetHosts = new[] { "fapi.binance.com", "api.binance.com", "binance.com/api", "dapi.binance.com" };

    public bool Enabled { get; init; }

    /// <summary>Must be explicitly true (in addition to <see cref="Enabled"/>) before any testnet order is placed.</summary>
    public bool AllowTestnetOrders { get; init; }

    /// <summary>Real orders are never permitted. Hardcoded false; cannot be overridden by config.</summary>
    public bool AllowRealOrders { get; } = false;

    public string TestnetBaseUrl { get; init; } = "https://testnet.binancefuture.com";
    public string? TestnetApiKey { get; init; }
    public string? TestnetSecretKey { get; init; }

    public decimal NotionalUsdt { get; init; } = 10m;
    public int Leverage { get; init; } = 1;
    public int MaxOpenPositions { get; init; } = 1;
    public int DailyMaxTrades { get; init; } = 3;
    public int MaxConsecutiveLosses { get; init; } = 5;

    public string IncubationOutputDirectory { get; init; } = "TradingBot.Backtest/output/fixed-frequency-eth15-forward-incubation-v1";
    public string ReportOutputDirectory { get; init; } = "output/eth15-testnet-execution";
    public int IntervalSeconds { get; init; } = 300;

    /// <summary>Max time to hold an open testnet position before a forced time exit. Matches the frozen MaxHoldMinutes (240).</summary>
    public int MaxHoldMinutes { get; init; } = 240;

    public static Eth15TestnetExecutionSettings Load(IConfiguration configuration, string contentRootPath)
    {
        var section = configuration.GetSection(SectionName);

        if (section.GetValue("AllowRealOrders", false))
            throw new InvalidOperationException(RealOrdersForbiddenError);

        var incubationDir = section.GetValue<string>("IncubationOutputDirectory")
            ?? "TradingBot.Backtest/output/fixed-frequency-eth15-forward-incubation-v1";
        var reportDir = section.GetValue<string>("ReportOutputDirectory")
            ?? "output/eth15-testnet-execution";

        return new Eth15TestnetExecutionSettings
        {
            Enabled = section.GetValue("Enabled", false),
            AllowTestnetOrders = section.GetValue("AllowTestnetOrders", false),
            TestnetBaseUrl = section.GetValue<string>("TestnetBaseUrl") is { Length: > 0 } baseUrl
                ? baseUrl
                : "https://testnet.binancefuture.com",
            TestnetApiKey = section["TestnetApiKey"],
            TestnetSecretKey = section["TestnetSecretKey"],
            NotionalUsdt = Math.Max(0m, section.GetValue("NotionalUsdt", 10m)),
            Leverage = Math.Max(1, section.GetValue("Leverage", 1)),
            MaxOpenPositions = Math.Max(1, section.GetValue("MaxOpenPositions", 1)),
            DailyMaxTrades = Math.Max(0, section.GetValue("DailyMaxTrades", 3)),
            MaxConsecutiveLosses = Math.Max(1, section.GetValue("MaxConsecutiveLosses", 5)),
            MaxHoldMinutes = Math.Max(1, section.GetValue("MaxHoldMinutes", 240)),
            IncubationOutputDirectory = ResolvePath(contentRootPath, incubationDir),
            ReportOutputDirectory = ResolvePath(contentRootPath, reportDir),
            IntervalSeconds = Math.Max(30, section.GetValue("IntervalSeconds", 300))
        };
    }

    /// <summary>
    /// Fail-fast safety validation. Throws if the configuration could possibly reach a
    /// production/mainnet endpoint or reuse the live (production) keys. Called both at
    /// startup and immediately before any order is placed (defense in depth).
    /// </summary>
    public void ValidateTestnetSafety(string? liveApiKey, string? liveSecretKey)
    {
        if (AllowRealOrders)
            throw new InvalidOperationException(RealOrdersForbiddenError);

        var baseUrl = TestnetBaseUrl ?? string.Empty;

        foreach (var forbidden in ForbiddenMainnetHosts)
        {
            if (baseUrl.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"{NonTestnetBaseUrlError}: TestnetBaseUrl '{baseUrl}' targets a mainnet host '{forbidden}'. Only Binance Futures Testnet is permitted.");
        }

        var isAllowedTestnetHost = AllowedTestnetHosts.Any(h => baseUrl.Contains(h, StringComparison.OrdinalIgnoreCase));
        if (!isAllowedTestnetHost)
            throw new InvalidOperationException(
                $"{NonTestnetBaseUrlError}: TestnetBaseUrl '{baseUrl}' is not a recognized Binance Futures Testnet host ({string.Join(", ", AllowedTestnetHosts)}).");

        // Only require/validate credentials when ordering is actually requested.
        if (!Enabled || !AllowTestnetOrders)
            return;

        if (string.IsNullOrWhiteSpace(TestnetApiKey) || string.IsNullOrWhiteSpace(TestnetSecretKey))
            throw new InvalidOperationException(
                $"{MissingTestnetCredentialsError}: TestnetApiKey/TestnetSecretKey must be set when Enabled and AllowTestnetOrders are true.");

        if (!string.IsNullOrWhiteSpace(liveApiKey) && string.Equals(TestnetApiKey, liveApiKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{ProductionKeysDetectedError}: TestnetApiKey matches the live (production) ApiKey. Refusing to start.");

        if (!string.IsNullOrWhiteSpace(liveSecretKey) && string.Equals(TestnetSecretKey, liveSecretKey, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{ProductionKeysDetectedError}: TestnetSecretKey matches the live (production) SecretKey. Refusing to start.");
    }

    public bool CanPlaceTestnetOrders => Enabled && AllowTestnetOrders && !AllowRealOrders;

    private static string ResolvePath(string contentRootPath, string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
            return Path.GetFullPath(configuredPath);

        var fromContent = Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
        if (Directory.Exists(fromContent) || File.Exists(fromContent))
            return fromContent;

        // Backtest outputs live one level up from the web host content root in this repo layout.
        var fromParent = Path.GetFullPath(Path.Combine(contentRootPath, "..", configuredPath));
        return Directory.Exists(fromParent) || File.Exists(fromParent) ? fromParent : fromContent;
    }
}
