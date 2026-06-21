namespace TradingBot.Backtest;

public static class NoPaidDataShortWindowFlowResearchV1Catalog
{
    public const string PrimaryCostScenario = "futures-moderate";
    public const string ModerateSlippageScenario = "futures-moderate-latency-002";

    public const int MinimumExecutedTrades = 20;
    public const int SparseLookbackTradeThreshold = 5;
    /// <summary>Latency-002 net below this counts as "destroyed" even if moderate net was positive.</summary>
    public const decimal NotDestroyedFloorQuote = -10m;

    public const decimal DistanceNearHighMaxPercent = 0.2536m; // Rule01 Q1 upper bound for DistanceFromRecentHighPercent
    public const decimal TakerImbalanceStretchedMin = 0.10m;
    public const decimal LongShortZStretchedMin = 1.0m;
    public const decimal FundingZStretchedMin = 1.0m;

    public static readonly int[] CheckpointFrequencyHours = [4, 24, 72];
    public static readonly int[] LookbackDays = [3, 7, 14, 30];
    public static readonly int[] ActivationPeriodHours = [4, 12, 24, 72, 168];
    public static readonly int[] MinLookbackTrades = [3, 5, 10];
    public static readonly decimal[] ProfitFactorThresholds = [1.1m, 1.2m];
    public static readonly int[] CombinedMinLookbackTrades = [3, 5];

    public static readonly ShortWindowFlowCondition[] FlowConditions =
    [
        ShortWindowFlowCondition.OiRisingPriceNearHigh,
        ShortWindowFlowCondition.TakerImbalanceStretched,
        ShortWindowFlowCondition.LongShortStretched,
        ShortWindowFlowCondition.BtcReturn30mPositive,
        ShortWindowFlowCondition.BtcReturn60mNegative,
        ShortWindowFlowCondition.FundingStretched,
        ShortWindowFlowCondition.FundingNormal
    ];

    public static readonly string[] CostStressScenarios =
    [
        "futures-moderate",
        "futures-moderate-latency-002",
        "futures-stress",
        "futures-stress-latency-002",
        "futures-stress-plus"
    ];

    public static DirectionalRuleV31SimulationProfile BuildProfile(DirectionalRuleDefinition rule)
        => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.BuildProfile(rule);

    /// <summary>
    /// Evaluates a flow confirmation condition against an as-of snapshot.
    /// Returns (available, pass): when the required inputs are missing, available=false and the
    /// checkpoint must not activate (no silent pass — that would hide missing data).
    /// </summary>
    public static (bool Available, bool Pass) EvaluateFlow(ShortWindowFlowCondition condition, ShortWindowFlowSnapshot s)
    {
        switch (condition)
        {
            case ShortWindowFlowCondition.None:
                return (true, true);
            case ShortWindowFlowCondition.OiRisingPriceNearHigh:
            {
                if (!s.OiChange60mPercent.HasValue || !s.DistanceFromRecentHighPercent.HasValue)
                    return (false, false);
                return (true, s.OiChange60mPercent.Value > 0m
                              && s.DistanceFromRecentHighPercent.Value <= DistanceNearHighMaxPercent);
            }
            case ShortWindowFlowCondition.TakerImbalanceStretched:
            {
                var value = s.TakerImbalance1h ?? s.TakerBuySellImbalance;
                if (!value.HasValue)
                    return (false, false);
                return (true, value.Value >= TakerImbalanceStretchedMin);
            }
            case ShortWindowFlowCondition.LongShortStretched:
            {
                if (!s.GlobalLongShortZScore.HasValue && !s.TopLongShortZScore.HasValue)
                    return (false, false);
                return (true, (s.GlobalLongShortZScore ?? decimal.MinValue) >= LongShortZStretchedMin
                              || (s.TopLongShortZScore ?? decimal.MinValue) >= LongShortZStretchedMin);
            }
            case ShortWindowFlowCondition.BtcReturn30mPositive:
                return s.BtcReturn30mPercent.HasValue
                    ? (true, s.BtcReturn30mPercent.Value > 0m)
                    : (false, false);
            case ShortWindowFlowCondition.BtcReturn60mNegative:
                return s.BtcReturn60mPercent.HasValue
                    ? (true, s.BtcReturn60mPercent.Value < 0m)
                    : (false, false);
            case ShortWindowFlowCondition.FundingStretched:
                return s.FundingZScore.HasValue
                    ? (true, Math.Abs(s.FundingZScore.Value) >= FundingZStretchedMin)
                    : (false, false);
            case ShortWindowFlowCondition.FundingNormal:
                return s.FundingZScore.HasValue
                    ? (true, Math.Abs(s.FundingZScore.Value) < FundingZStretchedMin)
                    : (false, false);
            default:
                return (false, false);
        }
    }

    public static IReadOnlyList<ShortWindowActivationConfig> BuildActivationConfigs()
    {
        var configs = new List<ShortWindowActivationConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(ShortWindowActivationConfig config)
        {
            if (seen.Add(config.ActivationRuleName))
                configs.Add(config);
        }

        Add(new ShortWindowActivationConfig(
            "Baseline_AlwaysOn", ShortWindowPerfCondition.AlwaysOn, ShortWindowFlowCondition.None,
            4, 0, 0, 0, null,
            "All Rule01 short trades inside the study window (no activation gate)"));

        foreach (var chk in CheckpointFrequencyHours)
        foreach (var lookback in LookbackDays)
        foreach (var fwd in ActivationPeriodHours)
        {
            // 1) Recent strategy performance only.
            foreach (var minTrades in MinLookbackTrades)
            {
                Add(new ShortWindowActivationConfig(
                    $"Perf_Net_Chk{chk}h_LB{lookback}d_Fwd{fwd}h_Min{minTrades}",
                    ShortWindowPerfCondition.RecentNetPositive, ShortWindowFlowCondition.None,
                    chk, lookback, fwd, minTrades, null,
                    $"Lookback net > 0 with >= {minTrades} trades"));

                foreach (var pf in ProfitFactorThresholds)
                {
                    Add(new ShortWindowActivationConfig(
                        $"Perf_PF{pf:0.0}_Chk{chk}h_LB{lookback}d_Fwd{fwd}h_Min{minTrades}",
                        ShortWindowPerfCondition.RecentProfitFactor, ShortWindowFlowCondition.None,
                        chk, lookback, fwd, minTrades, pf,
                        $"Lookback profit factor > {pf:0.0} with >= {minTrades} trades"));
                }

                Add(new ShortWindowActivationConfig(
                    $"Perf_WinNet_Chk{chk}h_LB{lookback}d_Fwd{fwd}h_Min{minTrades}",
                    ShortWindowPerfCondition.RecentWinRateAndNet, ShortWindowFlowCondition.None,
                    chk, lookback, fwd, minTrades, null,
                    $"Lookback win rate > 50% and net > 0 with >= {minTrades} trades"));
            }

            // 2) Flow confirmation only (sparse lookback trades flagged, not blocking).
            foreach (var flow in FlowConditions)
            {
                Add(new ShortWindowActivationConfig(
                    $"Flow_{flow}_Chk{chk}h_LB{lookback}d_Fwd{fwd}h",
                    ShortWindowPerfCondition.None, flow,
                    chk, lookback, fwd, 0, null,
                    $"Flow confirms ({flow}) regardless of lookback trade count; sparse lookback flagged"));
            }

            // 3) Combined: recent performance positive AND flow confirms.
            foreach (var flow in FlowConditions)
            foreach (var minTrades in CombinedMinLookbackTrades)
            {
                Add(new ShortWindowActivationConfig(
                    $"Comb_Net_{flow}_Chk{chk}h_LB{lookback}d_Fwd{fwd}h_Min{minTrades}",
                    ShortWindowPerfCondition.RecentNetPositive, flow,
                    chk, lookback, fwd, minTrades, null,
                    $"Lookback net > 0 (>= {minTrades} trades) AND flow confirms ({flow})"));
            }
        }

        return configs;
    }
}
