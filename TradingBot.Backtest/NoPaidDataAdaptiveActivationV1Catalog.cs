using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class NoPaidDataAdaptiveActivationV1Catalog
{
    public const string PrimaryCostScenario = "futures-moderate";
    public const int MinimumExecutedTrades = 30;
    public const int MinimumOlderTrades = 15;
    public const int MinimumActivationPeriods = 6;
    public const decimal NearBreakevenFull365 = -25m;
    public const decimal MinimumOlderImprovementQuote = 200m;
    public const decimal MinimumRecentRetentionRatio = 0.70m;

    public static readonly int[] CheckpointFrequencies = [1, 3, 7];
    public static readonly int[] LookbackDays = [14, 30, 60, 90];
    public static readonly int[] ActivationPeriodDays = [1, 3, 7, 14];
    public static readonly int[] MinLookbackTrades = [5, 10, 15];
    public static readonly decimal[] ProfitFactorThresholds = [1.1m, 1.2m, 1.3m];
    public static readonly decimal[] MaxDrawdownThresholds = [75m, 100m, 150m];
    public static readonly int[] ConsecutiveLossLimits = [3, 5, 7];
    public static readonly int[] CooldownDays = [1, 3, 7];

    public static readonly string[] CostStressScenarios =
    [
        "futures-moderate",
        "futures-stress",
        "futures-stress-plus",
        "futures-moderate-latency-002",
        "futures-moderate-latency-005",
        "futures-stress-latency-002"
    ];

    public static DirectionalRuleV31SimulationProfile BuildProfile(DirectionalRuleDefinition rule)
        => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.BuildProfile(rule);

    public static IReadOnlyList<AdaptiveActivationRuleConfig> BuildActivationRules(decimal btc30Q3Lower)
    {
        var rules = new List<AdaptiveActivationRuleConfig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(AdaptiveActivationRuleConfig rule)
        {
            if (seen.Add(rule.ActivationRuleName))
                rules.Add(rule);
        }

        Add(new AdaptiveActivationRuleConfig(
            "Baseline_AlwaysOn", AdaptiveActivationConditionType.AlwaysOn,
            1, 0, int.MaxValue, 0, null, null, null, 0, AdaptiveRegimeFilterKind.None,
            "All Rule01 short trades (no walk-forward activation gate)"));

        foreach (var checkpoint in CheckpointFrequencies)
        foreach (var lookback in LookbackDays)
        foreach (var forward in ActivationPeriodDays)
        foreach (var minTrades in MinLookbackTrades)
        {
            Add(new AdaptiveActivationRuleConfig(
                $"NetPos_Chk{checkpoint}d_LB{lookback}d_Fwd{forward}d_Min{minTrades}",
                AdaptiveActivationConditionType.RecentNetPositive,
                checkpoint, lookback, forward, minTrades, null, null, null, 0, AdaptiveRegimeFilterKind.None,
                $"Activate if lookback net > 0 with >= {minTrades} trades"));

            foreach (var pf in ProfitFactorThresholds)
            {
                Add(new AdaptiveActivationRuleConfig(
                    $"PF{pf:0.0}_Chk{checkpoint}d_LB{lookback}d_Fwd{forward}d_Min{minTrades}",
                    AdaptiveActivationConditionType.RecentProfitFactor,
                    checkpoint, lookback, forward, minTrades, pf, null, null, 0, AdaptiveRegimeFilterKind.None,
                    $"Activate if lookback profit factor > {pf:0.0} with >= {minTrades} trades"));
            }

            Add(new AdaptiveActivationRuleConfig(
                $"WinRateNet_Chk{checkpoint}d_LB{lookback}d_Fwd{forward}d_Min{minTrades}",
                AdaptiveActivationConditionType.RecentWinRateAndNet,
                checkpoint, lookback, forward, minTrades, null, null, null, 0, AdaptiveRegimeFilterKind.None,
                $"Activate if lookback win rate > 50% and net > 0 with >= {minTrades} trades"));
        }

        // Drawdown guard: controlled subset on primary structural grid.
        foreach (var checkpoint in CheckpointFrequencies)
        foreach (var lookback in new[] { 14, 30, 60 })
        foreach (var forward in new[] { 3, 7, 14 })
        foreach (var maxDd in MaxDrawdownThresholds)
        foreach (var lossLimit in ConsecutiveLossLimits)
        foreach (var cooldown in CooldownDays)
        {
            Add(new AdaptiveActivationRuleConfig(
                $"DDGuard_Chk{checkpoint}d_LB{lookback}d_Fwd{forward}d_DD{maxDd:0}_L{lossLimit}_C{cooldown}",
                AdaptiveActivationConditionType.DrawdownGuard,
                checkpoint, lookback, forward, 10, null, maxDd, lossLimit, cooldown, AdaptiveRegimeFilterKind.None,
                $"Activate if lookback net > 0 (min 10 trades); deactivate on DD>{maxDd} or {lossLimit} losses; cooldown {cooldown}d"));
        }

        // Regime + recent performance on primary grid.
        foreach (var checkpoint in CheckpointFrequencies)
        foreach (var lookback in LookbackDays)
        foreach (var forward in ActivationPeriodDays)
        foreach (var minTrades in MinLookbackTrades)
        foreach (var regime in new[]
                 {
                     AdaptiveRegimeFilterKind.BtcReturn30mPositive,
                     AdaptiveRegimeFilterKind.BtcReturn60mPositive,
                     AdaptiveRegimeFilterKind.VolatilityNormal,
                     AdaptiveRegimeFilterKind.Btc30Q3VolNormal
                 })
        {
            var regimeTag = regime switch
            {
                AdaptiveRegimeFilterKind.BtcReturn30mPositive => "Btc30Pos",
                AdaptiveRegimeFilterKind.BtcReturn60mPositive => "Btc60Pos",
                AdaptiveRegimeFilterKind.VolatilityNormal => "VolNormal",
                AdaptiveRegimeFilterKind.Btc30Q3VolNormal => "Btc30Q3VolNorm",
                _ => "None"
            };
            Add(new AdaptiveActivationRuleConfig(
                $"Regime_{regimeTag}_Chk{checkpoint}d_LB{lookback}d_Fwd{forward}d_Min{minTrades}",
                AdaptiveActivationConditionType.RegimeRecentPerformance,
                checkpoint, lookback, forward, minTrades, null, null, null, 0, regime,
                $"Activate if lookback net > 0 (>= {minTrades} trades) AND {regimeTag} at checkpoint"));
        }

        return rules;
    }

    public static decimal ComputeBtc30Q3Lower(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        var values = trades.Select(t => t.BtcReturn30mPercent).Where(v => v.HasValue).Select(v => v!.Value).ToArray();
        return DirectionalRuleFuturesRegimeConditionalV2Catalog.Percentile(values, 2m / 3m);
    }
}
