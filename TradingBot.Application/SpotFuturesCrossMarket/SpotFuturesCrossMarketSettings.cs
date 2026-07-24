using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// Configuration for the SpotFuturesCrossMarketTestnetV1 runtime strategy. It trades real
/// Binance USD-M Futures TESTNET orders (fake funds) driven by synchronized Spot + Futures
/// market data. Real-money trading is impossible by construction: <see cref="AllowRealOrders"/>
/// is hardcoded false, <see cref="Load"/> throws if config tries to enable it, and orders go
/// through the testnet-host-locked <c>IFuturesTestnetClient</c>.
/// </summary>
public sealed record SpotFuturesCrossMarketSettings
{
    public const string SectionName = "SpotFuturesCrossMarketTestnetV1";
    public const string StrategyName = "SpotFuturesCrossMarketAggressiveV3";
    public const string ExecutionEnvironment = ExecutionEnvironments.SpotFuturesCrossMarketTestnetV3;

    public const string RealOrdersForbiddenError = "SpotFuturesCrossMarketRealOrdersForbidden";
    public const string UnsafeTestnetConfigurationError = "SpotFuturesCrossMarketUnsafeTestnetConfiguration";

    public bool Enabled { get; init; }

    /// <summary>Must be explicitly true (in addition to <see cref="Enabled"/>) before any testnet order is placed.</summary>
    public bool AllowTestnetOrders { get; init; }

    /// <summary>Real orders are never permitted. Hardcoded false; cannot be overridden by config.</summary>
    public bool AllowRealOrders { get; } = false;

    /// <summary>
    /// All traded symbols; each must exist on both Spot and USD-M Futures testnets.
    /// The worker evaluates and trades every symbol independently each cycle.
    /// </summary>
    public IReadOnlyList<TradingSymbol> Symbols { get; init; } = [TradingSymbol.BNBUSDT];

    /// <summary>
    /// The symbol of the current per-symbol view (see <see cref="ForSymbol"/>). Downstream
    /// services (data service, signal engine, persistence) operate on one symbol at a time.
    /// </summary>
    public TradingSymbol Symbol { get; init; } = TradingSymbol.BNBUSDT;

    /// <summary>Per-symbol view used by the worker loop; only <see cref="Symbol"/> differs.</summary>
    public SpotFuturesCrossMarketSettings ForSymbol(TradingSymbol symbol) => this with { Symbol = symbol };

    /// <summary>Candle interval used on both markets (e.g. 15m).</summary>
    public string Interval { get; init; } = "15m";

    /// <summary>Higher timeframe used to decide whether the market is trending or ranging.</summary>
    public string RegimeInterval { get; init; } = "1h";

    /// <summary>Closed candles fetched per market per evaluation.</summary>
    public int CandleHistory { get; init; } = 80;

    // Signal engine.
    public int ShortMaPeriod { get; init; } = 7;
    public int LongMaPeriod { get; init; } = 25;
    public int MomentumLookbackCandles { get; init; } = 4;
    public int MinEntryTrendConfidenceScore { get; init; } = 45;
    public int MinExitTrendConfidenceScore { get; init; } = 35;
    public int RegimeShortMaPeriod { get; init; } = 20;
    public int RegimeLongMaPeriod { get; init; } = 50;
    public decimal MinRegimeAdx { get; init; } = 18m;
    public int RsiPeriod { get; init; } = 14;
    public decimal LongRsiMin { get; init; } = 50m;
    public decimal LongRsiMax { get; init; } = 70m;
    public decimal ShortRsiMin { get; init; } = 30m;
    public decimal ShortRsiMax { get; init; } = 50m;
    public int EntryBreakoutLookbackCandles { get; init; } = 12;
    public int EntryPullbackLookbackCandles { get; init; } = 6;
    public decimal MinEntryVolumeRatio { get; init; } = 0.80m;
    public decimal MinLongTakerBuyRatio { get; init; } = 0.51m;
    public decimal MaxShortTakerBuyRatio { get; init; } = 0.49m;
    public decimal MaxEntryExtensionAtr { get; init; } = 1.25m;
    public decimal MinRewardRiskRatio { get; init; } = 2.20m;
    public bool RequireMicrostructureConfirmation { get; init; } = true;
    public decimal MaxEntrySpreadBps { get; init; } = 3m;
    public decimal MinEntryMicrostructureScore { get; init; } = 3m;
    /// <summary>
    /// Testnet-only evidence mode. When the normal multi-timeframe setup rejects a synchronized
    /// candle, the signal engine may place a clearly labelled directional probe so the order,
    /// fill, position-management and reporting paths can be exercised during a short run.
    /// This never bypasses runtime risk/execution guards and cannot reach a real-money client.
    /// </summary>
    public bool EnableTestnetEvidenceEntries { get; init; }
    public bool EnableEntryQualityFilters { get; init; } = false;
    public bool RequireEntryClosedCandleDirectionConfirmation { get; init; } = true;
    public decimal MinEntrySpotMomentumAbsPercent { get; init; } = 0m;
    public int EntryExhaustionLookbackCandles { get; init; } = 8;
    public decimal EntryExhaustionExtremeZonePercent { get; init; } = 15m;
    public decimal EntryExhaustionMinMovePercent { get; init; } = 0.50m;

    /// <summary>Both markets' latest closed candles must open within this tolerance to be "in sync".</summary>
    public int MaxCandleMisalignmentSeconds { get; init; } = 5;

    /// <summary>Skip longs when funding is above this, skip shorts when below the negative of it (percent as decimal, e.g. 0.0005 = 0.05%).</summary>
    public decimal MaxAbsFundingRateForEntry { get; init; } = 0.0008m;

    /// <summary>Skip entries when |basis| exceeds this percent (dislocated/desynced markets).</summary>
    public decimal MaxAbsBasisPercentForEntry { get; init; } = 1.0m;

    /// <summary>
    /// Separate fake-funds evidence-probe tolerance for independently simulated Spot/Futures
    /// testnet feeds. Normal qualified strategy entries always use
    /// <see cref="MaxAbsBasisPercentForEntry"/>.
    /// </summary>
    public decimal TestnetEvidenceMaxAbsBasisPercent { get; init; } = 1.0m;

    // Risk model (ATR-anchored).
    public decimal AtrStopMultiplier { get; init; } = 1.6m;
    public decimal AtrTargetMultiplier { get; init; } = 2.4m;
    public decimal MinStopPercent { get; init; } = 0.35m;
    public decimal MaxStopPercent { get; init; } = 2.0m;

    /// <summary>Round-trip taker fee + spread the expected move must clear (percent).</summary>
    public decimal FeeAndSpreadPercent { get; init; } = 0.15m;
    public decimal MinNetExpectedMovePercent { get; init; } = 0.10m;

    // Sizing.
    /// <summary>
    /// When true, position size is derived from the live futures wallet balance:
    /// margin per symbol = balance * (BalanceAllocationPercent / 100) / Symbols.Count,
    /// notional = margin * Leverage. When false, the fixed <see cref="NotionalUsdt"/> is used.
    /// </summary>
    public bool UseBalanceBasedSizing { get; init; } = true;

    /// <summary>
    /// Total percent of the futures wallet balance committed as margin across ALL symbols.
    /// E.g. 30 with two symbols = 15% margin each; with 10x leverage each symbol trades
    /// a notional of 150% of the wallet (15% * 10).
    /// </summary>
    public decimal BalanceAllocationPercent { get; init; } = 30m;

    /// <summary>Fixed notional fallback (USDT) when balance-based sizing is disabled or the balance is unavailable.</summary>
    public decimal NotionalUsdt { get; init; } = 50m;

    public int Leverage { get; init; } = 10;

    /// <summary>Max simultaneously open positions across all symbols (one per symbol at most).</summary>
    public int MaxOpenPositions { get; init; } = 1;
    public int DailyMaxTrades { get; init; } = 6;
    public int MaxConsecutiveLosses { get; init; } = 4;
    public int MaxHoldMinutes { get; init; } = 360;

    /// <summary>Closed candles to wait after closing a position before a new entry.</summary>
    public int ReentryCooldownCandles { get; init; } = 1;

    /// <summary>Worker cycle period. Exits are checked every cycle; entries only on new closed candles.</summary>
    public int IntervalSeconds { get; init; } = 30;

    public string ReportOutputDirectory { get; init; } = "output/spot-futures-cross-market-testnet-v1";

    public bool CanPlaceTestnetOrders => Enabled && AllowTestnetOrders && !AllowRealOrders;

    public TimeSpan IntervalTimeSpan => ParseInterval(Interval);

    public static SpotFuturesCrossMarketSettings Load(IConfiguration configuration, string contentRootPath)
    {
        var section = configuration.GetSection(SectionName);

        if (section.GetValue("AllowRealOrders", false))
            throw new InvalidOperationException(RealOrdersForbiddenError);

        var symbols = ParseSymbols(section);

        var interval = section.GetValue<string>("Interval") is { Length: > 0 } i ? i : "15m";
        ParseInterval(interval); // throws on unsupported interval
        var regimeInterval = section.GetValue<string>("RegimeInterval") is { Length: > 0 } r ? r : "1h";
        ParseInterval(regimeInterval);

        var reportDir = section.GetValue<string>("ReportOutputDirectory") is { Length: > 0 } dir
            ? dir
            : "output/spot-futures-cross-market-testnet-v1";

        return new SpotFuturesCrossMarketSettings
        {
            Enabled = section.GetValue("Enabled", false),
            AllowTestnetOrders = section.GetValue("AllowTestnetOrders", false),
            Symbols = symbols,
            Symbol = symbols[0],
            Interval = interval,
            RegimeInterval = regimeInterval,
            CandleHistory = Math.Clamp(section.GetValue("CandleHistory", 80), 40, 1000),
            ShortMaPeriod = Math.Max(2, section.GetValue("ShortMaPeriod", 7)),
            LongMaPeriod = Math.Max(5, section.GetValue("LongMaPeriod", 25)),
            MomentumLookbackCandles = Math.Max(1, section.GetValue("MomentumLookbackCandles", 4)),
            MinEntryTrendConfidenceScore = Math.Clamp(section.GetValue("MinEntryTrendConfidenceScore", 45), 0, 100),
            MinExitTrendConfidenceScore = Math.Clamp(section.GetValue("MinExitTrendConfidenceScore", 35), 0, 100),
            RegimeShortMaPeriod = Math.Max(3, section.GetValue("RegimeShortMaPeriod", 20)),
            RegimeLongMaPeriod = Math.Max(10, section.GetValue("RegimeLongMaPeriod", 50)),
            MinRegimeAdx = Math.Clamp(section.GetValue("MinRegimeAdx", 18m), 0m, 100m),
            RsiPeriod = Math.Max(2, section.GetValue("RsiPeriod", 14)),
            LongRsiMin = Math.Clamp(section.GetValue("LongRsiMin", 50m), 0m, 100m),
            LongRsiMax = Math.Clamp(section.GetValue("LongRsiMax", 70m), 0m, 100m),
            ShortRsiMin = Math.Clamp(section.GetValue("ShortRsiMin", 30m), 0m, 100m),
            ShortRsiMax = Math.Clamp(section.GetValue("ShortRsiMax", 50m), 0m, 100m),
            EntryBreakoutLookbackCandles = Math.Max(3, section.GetValue("EntryBreakoutLookbackCandles", 12)),
            EntryPullbackLookbackCandles = Math.Max(2, section.GetValue("EntryPullbackLookbackCandles", 6)),
            MinEntryVolumeRatio = Math.Max(0m, section.GetValue("MinEntryVolumeRatio", 0.80m)),
            MinLongTakerBuyRatio = Math.Clamp(section.GetValue("MinLongTakerBuyRatio", 0.51m), 0m, 1m),
            MaxShortTakerBuyRatio = Math.Clamp(section.GetValue("MaxShortTakerBuyRatio", 0.49m), 0m, 1m),
            MaxEntryExtensionAtr = Math.Max(0.1m, section.GetValue("MaxEntryExtensionAtr", 1.25m)),
            MinRewardRiskRatio = Math.Max(1m, section.GetValue("MinRewardRiskRatio", 2.20m)),
            RequireMicrostructureConfirmation = section.GetValue("RequireMicrostructureConfirmation", true),
            MaxEntrySpreadBps = Math.Max(0m, section.GetValue("MaxEntrySpreadBps", 3m)),
            MinEntryMicrostructureScore = Math.Clamp(section.GetValue("MinEntryMicrostructureScore", 3m), -100m, 100m),
            EnableTestnetEvidenceEntries = section.GetValue("EnableTestnetEvidenceEntries", false),
            EnableEntryQualityFilters = section.GetValue("EnableEntryQualityFilters", false),
            RequireEntryClosedCandleDirectionConfirmation = section.GetValue("RequireEntryClosedCandleDirectionConfirmation", true),
            MinEntrySpotMomentumAbsPercent = Math.Max(0m, section.GetValue("MinEntrySpotMomentumAbsPercent", 0m)),
            EntryExhaustionLookbackCandles = Math.Max(2, section.GetValue("EntryExhaustionLookbackCandles", 8)),
            EntryExhaustionExtremeZonePercent = Math.Clamp(section.GetValue("EntryExhaustionExtremeZonePercent", 15m), 0m, 50m),
            EntryExhaustionMinMovePercent = Math.Max(0m, section.GetValue("EntryExhaustionMinMovePercent", 0.50m)),
            MaxCandleMisalignmentSeconds = Math.Max(0, section.GetValue("MaxCandleMisalignmentSeconds", 5)),
            MaxAbsFundingRateForEntry = Math.Max(0m, section.GetValue("MaxAbsFundingRateForEntry", 0.0008m)),
            MaxAbsBasisPercentForEntry = Math.Max(0m, section.GetValue("MaxAbsBasisPercentForEntry", 1.0m)),
            TestnetEvidenceMaxAbsBasisPercent = Math.Max(0m, section.GetValue("TestnetEvidenceMaxAbsBasisPercent", 1.0m)),
            AtrStopMultiplier = Math.Max(0.1m, section.GetValue("AtrStopMultiplier", 1.6m)),
            AtrTargetMultiplier = Math.Max(0.1m, section.GetValue("AtrTargetMultiplier", 2.4m)),
            MinStopPercent = Math.Max(0.05m, section.GetValue("MinStopPercent", 0.35m)),
            MaxStopPercent = Math.Max(0.1m, section.GetValue("MaxStopPercent", 2.0m)),
            FeeAndSpreadPercent = Math.Max(0m, section.GetValue("FeeAndSpreadPercent", 0.15m)),
            MinNetExpectedMovePercent = Math.Max(0m, section.GetValue("MinNetExpectedMovePercent", 0.10m)),
            UseBalanceBasedSizing = section.GetValue("UseBalanceBasedSizing", true),
            BalanceAllocationPercent = Math.Clamp(section.GetValue("BalanceAllocationPercent", 30m), 0.1m, 100m),
            NotionalUsdt = Math.Max(0m, section.GetValue("NotionalUsdt", 50m)),
            Leverage = Math.Clamp(section.GetValue("Leverage", 10), 1, 20),
            MaxOpenPositions = Math.Max(1, section.GetValue("MaxOpenPositions", symbols.Count)),
            DailyMaxTrades = Math.Max(0, section.GetValue("DailyMaxTrades", 6)),
            MaxConsecutiveLosses = Math.Max(1, section.GetValue("MaxConsecutiveLosses", 4)),
            MaxHoldMinutes = Math.Max(1, section.GetValue("MaxHoldMinutes", 360)),
            ReentryCooldownCandles = Math.Max(0, section.GetValue("ReentryCooldownCandles", 1)),
            IntervalSeconds = Math.Max(10, section.GetValue("IntervalSeconds", 30)),
            ReportOutputDirectory = ResolvePath(contentRootPath, reportDir)
        };
    }

    /// <summary>
    /// Fail-fast safety validation shared with the startup validator.
    /// </summary>
    public void ValidateTestnetSafety(IConfiguration configuration)
    {
        if (AllowRealOrders)
            throw new InvalidOperationException(RealOrdersForbiddenError);

        if (!Enabled)
            return;

        var section = configuration.GetSection("FuturesTestnet");
        var baseUrl = section["BaseUrl"] ?? string.Empty;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            (!string.Equals(uri.Host, "demo-fapi.binance.com", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Host, "testnet.binancefuture.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"{UnsafeTestnetConfigurationError}: FuturesTestnet:BaseUrl must use a recognized Binance Futures Testnet HTTPS host.");
        }

        if (AllowTestnetOrders &&
            (string.IsNullOrWhiteSpace(section["ApiKey"]) || string.IsNullOrWhiteSpace(section["SecretKey"])))
        {
            throw new InvalidOperationException(
                $"{UnsafeTestnetConfigurationError}: FuturesTestnet:ApiKey and FuturesTestnet:SecretKey are required when testnet ordering is enabled.");
        }
    }

    /// <summary>
    /// Reads "Symbols" (array) or falls back to the legacy single "Symbol" key. Duplicates
    /// are removed, order preserved. Throws on unknown symbols so misconfiguration fails fast.
    /// </summary>
    private static IReadOnlyList<TradingSymbol> ParseSymbols(IConfiguration section)
    {
        var raw = section.GetSection("Symbols").Get<string[]>();
        if (raw is null || raw.Length == 0)
            raw = [section.GetValue<string>("Symbol") ?? nameof(TradingSymbol.BNBUSDT)];

        var symbols = new List<TradingSymbol>();
        foreach (var s in raw)
        {
            if (!Enum.TryParse<TradingSymbol>(s, ignoreCase: true, out var symbol))
                throw new InvalidOperationException($"{SectionName}: unknown symbol '{s}' in Symbols.");
            if (!symbols.Contains(symbol))
                symbols.Add(symbol);
        }

        return symbols;
    }

    public static TimeSpan ParseInterval(string interval)
    {
        if (string.IsNullOrWhiteSpace(interval) || interval.Length < 2)
            throw new InvalidOperationException($"{SectionName}: unsupported Interval '{interval}'.");

        var unit = interval[^1];
        if (!int.TryParse(interval[..^1], out var amount) || amount <= 0)
            throw new InvalidOperationException($"{SectionName}: unsupported Interval '{interval}'.");

        return unit switch
        {
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => throw new InvalidOperationException($"{SectionName}: unsupported Interval '{interval}'. Use m/h/d.")
        };
    }

    private static string ResolvePath(string contentRootPath, string configuredPath)
        => Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
}
