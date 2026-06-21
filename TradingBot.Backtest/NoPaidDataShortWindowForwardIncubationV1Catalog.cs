namespace TradingBot.Backtest;

public static class NoPaidDataShortWindowForwardIncubationV1Catalog
{
    public const string FrozenProfileName = "Frozen_BNB_Rule01Short_FundingNormal_Daily24h_V1";
    public const string FrozenStateSubdir = "frozen";
    public const string PrimaryCostScenario = "futures-moderate";
    public const string ModerateSlippageScenario = "futures-moderate-latency-002";
    public const string StressPlusScenario = "futures-stress-plus";

    /// <summary>
    /// End of the data used by NoPaidDataShortWindowFlowResearchV1 to discover the candidate
    /// (studyEndUtc of that run). Everything at or before this timestamp is contaminated by
    /// selection; forward incubation only judges trades strictly after it.
    /// </summary>
    public static readonly DateTime DefaultDiscoveryEndUtc = new(2026, 6, 12, 14, 34, 0, DateTimeKind.Utc);

    // Health gate thresholds (fixed up-front; never tuned against the sample).
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

    /// <summary>Cost scenarios required for the incubation branch (matches the research catalog list).</summary>
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

    /// <summary>
    /// The exact activation logic selected by the previous run. Flow-only: lookback days are
    /// informational (sparse-warning) only and never gate activation. Never tuned here.
    /// </summary>
    public static ShortWindowActivationConfig BuildFrozenActivationConfig()
        => new(
            FrozenProfileName,
            ShortWindowPerfCondition.None,
            ShortWindowFlowCondition.FundingNormal,
            CheckpointFrequencyHours: 24,
            LookbackDays: 3,
            ActivationPeriodHours: 24,
            MinLookbackTrades: 0,
            ProfitFactorThreshold: null,
            "Frozen candidate: Flow_FundingNormal, daily checkpoint, 24h activation period (flow-only; lookback informational)");

    public static FrozenCandidateState BuildDefaultState(DateTime createdAtUtc)
        => new(
            FrozenProfileName,
            createdAtUtc,
            DefaultDiscoveryEndUtc,
            BaseRule: "Rule01_Short_DistHighQ1_AtrQ3",
            Symbol: "BNBUSDT",
            Interval: "5m",
            EntryMode: "NextClose",
            TargetPercent: 1.75m,
            StopPercent: 1.00m,
            MaxHoldMinutes: 240,
            CooldownCandles: 6,
            OverlapPolicy: "OneOpenTradePerRuleSymbol",
            ActivationFlowCondition: "FundingNormal",
            CheckpointFrequencyHours: 24,
            ActivationPeriodHours: 24,
            LookbackDaysInformational: 3,
            DiscoveryWindow: "2026-05-12 -> 2026-06-12 (31d)",
            DiscoveryBaselineTrades: 47,
            DiscoveryBaselineNet: 50.44m,
            DiscoveryCandidateTrades: 31,
            DiscoveryCandidateNet: 89.11m,
            DiscoveryCandidateProfitFactor: 2.24m,
            DiscoveryCandidateStressPlusNet: 58.59m,
            Caveats: "Single 31d sample; only 6/20 activated periods positive; two activation clusters; profits concentrated around 2026-06-02..2026-06-06. Discovered by searching 1981 configs, so discovery-window performance is selection-biased.");

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
