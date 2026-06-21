using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// NoPaidDataShortWindowSolForwardIncubationV1 — second frozen incubation track, beside (not
/// replacing) the BNB track. Freezes the SOLUSDT 5m short cross-symbol proposal and judges it on
/// strictly-forward data only. No threshold tuning, no grid search, no rule reselection. The BNB
/// frozen candidate, its history, and its reports are never read for logic nor written.
/// </summary>
public static class NoPaidDataShortWindowSolForwardIncubationV1Catalog
{
    public const string FrozenProfileName = "Frozen_SOL_NearExtremeShort_5m_T1.75S1.00_FlowBtcContext60mAgreesChk24hAct24h_CrossSymbolV1";
    public const string FrozenStateSubdir = "frozen";
    public const string PrimaryCostScenario = "futures-moderate";
    public const string ModerateSlippageScenario = "futures-moderate-latency-002";
    public const string StressPlusScenario = "futures-stress-plus";

    /// <summary>
    /// End of the data used by NoPaidDataShortWindowFlowResearchV1CrossSymbol to discover this
    /// candidate (studyEndUtc of that run). Everything at or before this timestamp is contaminated
    /// by selection; forward incubation only judges trades strictly after it.
    /// </summary>
    public static readonly DateTime DefaultDiscoveryEndUtc = new(2026, 6, 12, 15, 36, 0, DateTimeKind.Utc);

    // Health gate thresholds (fixed up-front; identical to the BNB track; never tuned).
    public const int MinForwardTrades = 20;
    public const decimal StressPlusCollapseFloorQuote = -10m;
    public const int MaxConsecutiveLossesLimit = 5;
    public const decimal MaxSingleDayProfitShare = 0.5m;
    public const decimal MinPositivePeriodRate = 0.40m;
    public const decimal MaxDrawdownToNetRatio = 1.5m;

    // Verdict thresholds.
    public const decimal MinForwardSpanDaysForJudgment = 1m;
    public const int MinTradesForEarlyJudgment = 10;
    public const decimal FailedNetFloorQuote = -25m;

    public static readonly string[] CostScenarios =
    [
        "futures-moderate",
        "futures-moderate-latency-002",
        "futures-stress",
        "futures-stress-latency-002",
        "futures-stress-plus"
    ];

    /// <summary>Flow sources whose local span must be reported each run.</summary>
    public static readonly string[] CoverageSourceKeys =
    [
        "openInterestHist5m",
        "takerLongShortRatio5m",
        "globalLongShortAccountRatio5m",
        "topLongShortPositionRatio5m",
        "funding",
        "markPriceKlines",
        "indexPriceKlines"
    ];

    /// <summary>Symbols whose data backs the frozen rule (SOL flow + BTC context).</summary>
    public static readonly TradingSymbol[] CoverageSymbols = [TradingSymbol.SOLUSDT, TradingSymbol.BTCUSDT];

    public static readonly CrossSymbolComboKey FrozenComboKey = new(
        TradingSymbol.SOLUSDT, "5m", LongShortDirection.Short,
        TargetPercent: 1.75m, StopPercent: 1.00m, MaxHoldMinutes: 240);

    /// <summary>
    /// The exact activation rule selected by the cross-symbol run: flow-only BtcContext60mAgrees
    /// (BTC 60m falling for shorts), daily checkpoint, 24h activation period. Never tuned here.
    /// </summary>
    public static CrossSymbolActivationConfig BuildFrozenActivationConfig()
        => new(
            "Flow_BtcContext60mAgrees_Chk24h_Act24h",
            IsAlwaysOn: false,
            CrossSymbolPerfKind.None,
            MultiSymbolActivationGate.BtcContext60mAgrees,
            CheckpointFrequencyHours: 24,
            ActivationPeriodHours: 24,
            LookbackDays: 3,
            MinLookbackTrades: 0,
            ProfitFactorThreshold: null);

    public static FrozenCandidateState BuildDefaultState(DateTime createdAtUtc)
        => new(
            FrozenProfileName,
            createdAtUtc,
            DefaultDiscoveryEndUtc,
            BaseRule: "NearHighElevatedVol (NearExtremeShort)",
            Symbol: "SOLUSDT",
            Interval: "5m",
            EntryMode: "NextClose",
            TargetPercent: 1.75m,
            StopPercent: 1.00m,
            MaxHoldMinutes: 240,
            CooldownCandles: 6,
            OverlapPolicy: "OneOpenTradePerRuleSymbol",
            ActivationFlowCondition: "BtcContext60mAgrees (BTC 60m return < 0 for shorts)",
            CheckpointFrequencyHours: 24,
            ActivationPeriodHours: 24,
            LookbackDaysInformational: 3,
            DiscoveryWindow: "2026-05-12 -> 2026-06-12 (31d)",
            DiscoveryBaselineTrades: 58,
            DiscoveryBaselineNet: -0.8905m,
            DiscoveryCandidateTrades: 31,
            DiscoveryCandidateNet: 5.7776m,
            DiscoveryCandidateProfitFactor: 1.605m,
            DiscoveryCandidateStressPlusNet: 1.9111m,
            Caveats: "Selected as best of 225 activation configs on a single 31d window, so discovery performance is selection-biased even though it passed all criteria (win rate 54.84%, max drawdown 1.9002, max 2 consecutive losses, 52.94% positive activated periods, latency-002 net +4.8299, both window halves independently positive, no sparse/overfit/single-cluster warnings).");

    public static string FrozenStatePath(string dataDirectory)
        => Path.Combine(dataDirectory, FrozenStateSubdir, FrozenProfileName + ".json");

    public static string ForwardHistoryPath(string dataDirectory)
        => Path.Combine(dataDirectory, FrozenStateSubdir, FrozenProfileName + "-forward-history.json");

    public static string ResolveVerdict(
        decimal forwardSpanDays,
        int checkpointCount,
        int forwardTrades,
        decimal netModerate,
        decimal netLatency002,
        bool allHealthGatesPass)
    {
        if (forwardSpanDays < MinForwardSpanDaysForJudgment || checkpointCount == 0)
            return "NotEnoughForwardDataYet";
        if (allHealthGatesPass)
            return "CandidateEligibleForPaperLater";

        if (forwardTrades < MinForwardTrades)
        {
            if (forwardTrades >= MinTradesForEarlyJudgment && netModerate <= FailedNetFloorQuote)
                return "CandidateDeteriorating";
            if (forwardTrades >= MinTradesForEarlyJudgment && netModerate > 0m && netLatency002 > 0m)
                return "CandidateImproving";
            return "KeepIncubating";
        }

        if (netModerate <= FailedNetFloorQuote || (netModerate <= 0m && netLatency002 <= FailedNetFloorQuote))
            return "CandidateFailed";
        if (netModerate > 0m && netLatency002 > 0m)
            return "CandidateImproving";
        return "CandidateDeteriorating";
    }
}
