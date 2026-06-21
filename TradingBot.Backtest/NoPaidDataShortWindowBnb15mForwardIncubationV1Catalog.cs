using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// NoPaidDataShortWindowBnb15mForwardIncubationV1 — third frozen incubation track from the latest
/// cross-symbol freeze proposal (BNBUSDT 15m short). Beside existing BNB 5m and SOL 5m tracks;
/// never modifies them. Backtest/research only.
/// </summary>
public static class NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog
{
    public const string FrozenProfileName = "Frozen_BNB_NearExtremeShort_15m_T1.75S1.00_PerfRecentNetPositiveChk4hAct72hLB3d_CrossSymbolProposal";
    public const string FrozenStateSubdir = "frozen";
    public const string PrimaryCostScenario = "futures-moderate";
    public const string ModerateSlippageScenario = "futures-moderate-latency-002";
    public const string StressPlusScenario = "futures-stress-plus";

    /// <summary>studyEndUtc of the 2026-06-18 cross-symbol V1 run that proposed this candidate.</summary>
    public static readonly DateTime DefaultDiscoveryEndUtc = new(2026, 6, 18, 8, 12, 0, DateTimeKind.Utc);

    public const int MinForwardTrades = 20;
    public const decimal StressPlusCollapseFloorQuote = -10m;
    public const int MaxConsecutiveLossesLimit = 5;
    public const decimal MaxSingleDayProfitShare = 0.5m;
    public const decimal MinPositivePeriodRate = 0.40m;
    public const decimal MaxDrawdownToNetRatio = 1.5m;
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

    public static readonly TradingSymbol[] CoverageSymbols = [TradingSymbol.BNBUSDT, TradingSymbol.BTCUSDT];

    public static readonly CrossSymbolComboKey FrozenComboKey = new(
        TradingSymbol.BNBUSDT, "15m", LongShortDirection.Short,
        TargetPercent: 1.75m, StopPercent: 1.00m, MaxHoldMinutes: 240);

    public static CrossSymbolActivationConfig BuildFrozenActivationConfig()
        => new(
            "Perf_RecentNetPositive_Chk4h_Act72h_LB3d",
            IsAlwaysOn: false,
            CrossSymbolPerfKind.RecentNetPositive,
            MultiSymbolActivationGate.None,
            CheckpointFrequencyHours: 4,
            ActivationPeriodHours: 72,
            LookbackDays: 3,
            MinLookbackTrades: NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.MinLookbackTrades,
            ProfitFactorThreshold: null);

    public static FrozenCandidateState BuildDefaultState(DateTime createdAtUtc)
        => new(
            FrozenProfileName,
            createdAtUtc,
            DefaultDiscoveryEndUtc,
            BaseRule: "NearHighElevatedVol (NearExtremeShort)",
            Symbol: "BNBUSDT",
            Interval: "15m",
            EntryMode: "NextClose",
            TargetPercent: 1.75m,
            StopPercent: 1.00m,
            MaxHoldMinutes: 240,
            CooldownCandles: 4,
            OverlapPolicy: "OneOpenTradePerRuleSymbol",
            ActivationFlowCondition: "None (performance-only: recent net positive over 3d lookback, min 5 trades)",
            CheckpointFrequencyHours: 4,
            ActivationPeriodHours: 72,
            LookbackDaysInformational: 3,
            DiscoveryWindow: "2026-05-12 -> 2026-06-18 (36.73d)",
            DiscoveryBaselineTrades: 51,
            DiscoveryBaselineNet: 17.4724m,
            DiscoveryCandidateTrades: 23,
            DiscoveryCandidateNet: 37.5189m,
            DiscoveryCandidateProfitFactor: 2.224552m,
            DiscoveryCandidateStressPlusNet: 17.1535m,
            Caveats: "Freeze proposal from cross-symbol V1 (2026-06-18). Selected as best of 225 activation configs on a single ~37d window, so discovery performance is selection-biased even though it passed all criteria (win rate 65.22%, max drawdown 9.8276, max 2 consecutive losses, 73.17% positive activated periods, latency-002 net +31.9081, no sparse/overfit/single-cluster warnings).");

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
