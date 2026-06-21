using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// NoPaidDataShortWindowFlowResearchV1CrossSymbol configuration. Generalizes the V1 short-window
/// flow activation research across BTC/ETH/BNB/SOL, 5m/15m/30m, long+short, and a fixed trade
/// geometry grid. Base entries use the Rule01-style near-extreme + elevated-volatility family
/// (NearHighElevatedVol for shorts, NearLowElevatedVol for longs) with the same fixed thresholds
/// as the multi-symbol V2 catalog — no thresholds are tuned here. This branch is separate
/// research and never reads or modifies the frozen BNB incubation candidate.
/// </summary>
public static class NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog
{
    public const string PrimaryCostScenario = "futures-moderate";
    public const string ModerateLatencyScenario = "futures-moderate-latency-002";
    public const string StressPlusScenario = "futures-stress-plus";

    public static readonly string[] CostScenarios =
    [
        "futures-moderate",
        "futures-moderate-latency-002",
        "futures-stress",
        "futures-stress-latency-002",
        "futures-stress-plus"
    ];

    public static readonly TradingSymbol[] Symbols =
    [
        TradingSymbol.BTCUSDT,
        TradingSymbol.ETHUSDT,
        TradingSymbol.BNBUSDT,
        TradingSymbol.SOLUSDT
    ];

    public static readonly string[] Intervals = ["5m", "15m", "30m"];

    public const int HoldMinutes = 240;

    /// <summary>Fixed geometry grid (target%, stop%); hold is 4h for all.</summary>
    public static readonly (decimal Target, decimal Stop)[] GeometryGrid =
    [
        (0.75m, 0.50m),
        (1.00m, 0.75m),
        (1.25m, 0.75m),
        (1.50m, 1.00m),
        (1.75m, 1.00m)
    ];

    /// <summary>Cooldown candles per interval (same fixed values as the V2 branch).</summary>
    public static int CooldownFor(string interval) => interval switch
    {
        "5m" => 6,
        "15m" => 4,
        "30m" => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(interval), interval, "Unsupported interval.")
    };

    // Activation grid dimensions (per spec).
    public static readonly int[] CheckpointHours = [4, 24];
    public static readonly int[] LookbackDays = [3, 7, 14, 30];
    public static readonly int[] ActivationPeriodHours = [4, 12, 24, 72];
    public const int MinLookbackTrades = 5;
    public const decimal ProfitFactorThreshold = 1.1m;

    /// <summary>Flow gates reused from the V2 catalog (direction-aware, fixed thresholds).</summary>
    public static readonly MultiSymbolActivationGate[] FlowGates =
    [
        MultiSymbolActivationGate.FundingNormal,
        MultiSymbolActivationGate.BtcContext60mAgrees,
        MultiSymbolActivationGate.LongShortStretchedAgainstDirection,
        MultiSymbolActivationGate.OiChangeConfirms
    ];

    // Success criteria (fixed up-front).
    public const int MinTradeCount = 20;
    public const decimal StressPlusCollapseFloorQuote = -10m;
    public const decimal MinPositivePeriodRate = 0.40m;
    public const int MaxConsecutiveLossesLimit = 5;
    public const decimal MaxSingleDayProfitShare = 0.5m;
    public const decimal FailedNetFloorQuote = -10m;
    public const int MinActivatedPeriodsForConfidence = 8;

    public static MultiSymbolRuleFamily BaseFamilyFor(LongShortDirection direction)
        => direction == LongShortDirection.Short
            ? MultiSymbolRuleFamily.NearHighElevatedVol
            : MultiSymbolRuleFamily.NearLowElevatedVol;

    public static IReadOnlyList<CrossSymbolActivationConfig> BuildActivationConfigs()
    {
        var configs = new List<CrossSymbolActivationConfig>
        {
            new("Baseline_AlwaysOn", true, CrossSymbolPerfKind.None, MultiSymbolActivationGate.None, 24, 24, 0, 0, null)
        };

        // Flow-only (lookback is irrelevant; fixed at 3d for reporting).
        foreach (var gate in FlowGates)
        foreach (var chk in CheckpointHours)
        foreach (var act in ActivationPeriodHours)
        {
            configs.Add(new CrossSymbolActivationConfig(
                $"Flow_{gate}_Chk{chk}h_Act{act}h",
                false, CrossSymbolPerfKind.None, gate, chk, act, 3, 0, null));
        }

        // Performance-only.
        foreach (var perf in new[] { CrossSymbolPerfKind.RecentNetPositive, CrossSymbolPerfKind.RecentProfitFactor })
        foreach (var chk in CheckpointHours)
        foreach (var act in ActivationPeriodHours)
        foreach (var lb in LookbackDays)
        {
            configs.Add(new CrossSymbolActivationConfig(
                $"Perf_{perf}_Chk{chk}h_Act{act}h_LB{lb}d",
                false, perf, MultiSymbolActivationGate.None, chk, act, lb, MinLookbackTrades,
                perf == CrossSymbolPerfKind.RecentProfitFactor ? ProfitFactorThreshold : null));
        }

        // Combined recent performance + flow confirmation.
        foreach (var gate in FlowGates)
        foreach (var chk in CheckpointHours)
        foreach (var act in ActivationPeriodHours)
        foreach (var lb in LookbackDays)
        {
            configs.Add(new CrossSymbolActivationConfig(
                $"Combo_NetPositive_{gate}_Chk{chk}h_Act{act}h_LB{lb}d",
                false, CrossSymbolPerfKind.RecentNetPositive, gate, chk, act, lb, MinLookbackTrades, null));
        }

        return configs;
    }

    public static string ResolveRecommendation(
        bool allCriteriaPass, bool overfitWarning, int tradeCount, decimal netModerate, decimal netLatency)
    {
        if (allCriteriaPass && !overfitWarning)
            return "FreezeForForwardIncubation";
        if (allCriteriaPass)
            return "Watchlist";
        if (netModerate > 0m && netLatency > 0m)
            return tradeCount < MinTradeCount ? "NeedsMoreData" : "Watchlist";
        if (netModerate <= FailedNetFloorQuote || (netModerate <= 0m && netLatency <= FailedNetFloorQuote))
            return "CandidateFailed";
        return "Park";
    }

    public static string SuggestFrozenProfileName(CrossSymbolComboKey key, string activationRule)
    {
        var symbolShort = key.Symbol.ToString().Replace("USDT", "", StringComparison.Ordinal);
        var ruleTag = activationRule.Replace("_", "", StringComparison.Ordinal);
        return $"Frozen_{symbolShort}_NearExtreme{key.Direction}_{key.Interval}_T{key.TargetPercent:0.00}S{key.StopPercent:0.00}_{ruleTag}_CrossSymbolProposal";
    }
}
