namespace TradingBot.Backtest;

/// <summary>
/// Cross-symbol candidate engine V2 — research/shadow only. Reads V1 discovery outputs;
/// does not place orders or modify frozen profiles.
/// </summary>
public static class CrossSymbolCandidateEngineV2Catalog
{
    public const string ModeName = "cross-symbol-candidate-engine-v2";
    public const string DefaultOutputSubdir = "cross-symbol-candidate-engine-v2";
    public const string DefaultV1InputSubdir = "no-paid-short-window-v1-cross-symbol";
    public const string DefaultBottleneckAuditSubdir = "frozen-profile-bottleneck-audit";
    public const string DefaultShadowRunnerSubdir = "futures-testnet-shadow-run";

    public const bool DefaultOneCandidatePerSymbol = true;
    public const int DefaultMaxShadowCandidates = 3;
    public const decimal DefaultMaxTotalShadowNotionalUsdt = 100m;
    public const decimal DefaultMaxPerCandidateNotionalUsdt = 25m;
    public const int DefaultLeverageForMarginEstimate = 3;

    public const int MinTradeCountForShadow = 20;
    public const decimal MinPositivePeriodRate = 0.40m;
    public const int MaxConsecutiveLossesForShadow = 4;
    public const decimal MaxDrawdownToNetRatio = 1.5m;
    public const decimal MinUsableCoverageDays = 14m;
    public const decimal MinNormalizedNetPer100Usdt = 0m;

    public const string PrimaryCostScenario = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario;
    public const string ModerateLatencyScenario = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.ModerateLatencyScenario;
    public const string StressPlusScenario = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.StressPlusScenario;

    /// <summary>Reference 1-unit notional (USD) for normalizing 1-qty simulated trades to fixed USDT sizing.</summary>
    public static decimal ReferenceUnitNotionalUsd(string symbol) => symbol.ToUpperInvariant() switch
    {
        "BTCUSDT" => 100_000m,
        "ETHUSDT" => 3_500m,
        "BNBUSDT" => 600m,
        "SOLUSDT" => 150m,
        _ => 1_000m
    };

    public static string CandidateKey(
        string symbol, string interval, string direction,
        decimal targetPercent, decimal stopPercent, string activationRule)
        => $"{symbol}|{interval}|{direction}|T{targetPercent:0.00}|S{stopPercent:0.00}|{activationRule}";
}
