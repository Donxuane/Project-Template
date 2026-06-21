using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class Sol30mNearMissConversionHistoryStudyCatalog
{
    public const string ModeName = "sol30m-near-miss-conversion-history";
    public const string DefaultOutputSubdir = "sol30m-near-miss-conversion-history";
    public const string OutputPrefix = "sol30m-near-miss-conversion-history";
    public const string ActivationRuleName = "Flow_FundingNormal_Chk4h_Act4h";

    public static readonly CrossSymbolComboKey TargetKey = new(
        TradingSymbol.SOLUSDT,
        "30m",
        LongShortDirection.Short,
        1.00m,
        0.75m,
        NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes);

    public static readonly string CandidateKey =
        "SOLUSDT|30m|Short|T1.00|S0.75|Flow_FundingNormal_Chk4h_Act4h";

    public static readonly int[] ConversionCandleWindows = [1, 2, 4, 8];
    public const int ConversionHoursWindow = 24;

    public static readonly string[] DistanceBucketLabels =
    [
        "0.00% to 0.25%",
        "0.25% to 0.50%",
        "0.50% to 0.75%",
        "0.75% to 1.00%",
        "> 1.00%"
    ];

    public const decimal HighConversionRateThreshold = 0.30m;
    public const decimal LowConversionRateThreshold = 0.15m;

    public static CrossSymbolActivationConfig ResolveActivationConfig()
        => NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.BuildActivationConfigs()
            .First(c => string.Equals(c.ActivationRuleName, ActivationRuleName, StringComparison.Ordinal));

    public static string DistanceBucket(decimal distanceToEntryPercent)
    {
        if (distanceToEntryPercent <= 0.25m) return DistanceBucketLabels[0];
        if (distanceToEntryPercent <= 0.50m) return DistanceBucketLabels[1];
        if (distanceToEntryPercent <= 0.75m) return DistanceBucketLabels[2];
        if (distanceToEntryPercent <= 1.00m) return DistanceBucketLabels[3];
        return DistanceBucketLabels[4];
    }
}
