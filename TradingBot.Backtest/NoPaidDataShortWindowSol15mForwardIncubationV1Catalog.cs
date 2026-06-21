using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// NoPaidDataShortWindowSol15mForwardIncubationV1 — fourth frozen incubation track from the latest
/// cross-symbol freeze proposal (SOLUSDT 15m short T1.75/S1.00). Beside existing BNB 5m, SOL 5m,
/// and BNB 15m tracks; never modifies them. Backtest/research only.
/// </summary>
public static class NoPaidDataShortWindowSol15mForwardIncubationV1Catalog
{
    public const string FrozenProfileName = "Frozen_SOL_NearExtremeShort_15m_T1.75S1.00_FlowLongShortStretchedAgainstDirectionChk24hAct72h_CrossSymbolProposal";
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

    public static readonly TradingSymbol[] CoverageSymbols = [TradingSymbol.SOLUSDT, TradingSymbol.BTCUSDT];

    public static readonly CrossSymbolComboKey FrozenComboKey = new(
        TradingSymbol.SOLUSDT, "15m", LongShortDirection.Short,
        TargetPercent: 1.75m, StopPercent: 1.00m, MaxHoldMinutes: 240);

    public static CrossSymbolActivationConfig BuildFrozenActivationConfig()
        => new(
            "Flow_LongShortStretchedAgainstDirection_Chk24h_Act72h",
            IsAlwaysOn: false,
            CrossSymbolPerfKind.None,
            MultiSymbolActivationGate.LongShortStretchedAgainstDirection,
            CheckpointFrequencyHours: 24,
            ActivationPeriodHours: 72,
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
            Interval: "15m",
            EntryMode: "NextClose",
            TargetPercent: 1.75m,
            StopPercent: 1.00m,
            MaxHoldMinutes: 240,
            CooldownCandles: 4,
            OverlapPolicy: "OneOpenTradePerRuleSymbol",
            ActivationFlowCondition: "LongShortStretchedAgainstDirection (crowd positioning skew confirms short direction)",
            CheckpointFrequencyHours: 24,
            ActivationPeriodHours: 72,
            LookbackDaysInformational: 3,
            DiscoveryWindow: "2026-05-12 -> 2026-06-18 (36.73d)",
            DiscoveryBaselineTrades: 57,
            DiscoveryBaselineNet: -4.8127m,
            DiscoveryCandidateTrades: 20,
            DiscoveryCandidateNet: 3.5052m,
            DiscoveryCandidateProfitFactor: 1.585973m,
            DiscoveryCandidateStressPlusNet: 0.7677m,
            Caveats: "Freeze proposal from cross-symbol V1 (2026-06-18). Selected over SOL 15m T1.00/S0.75 for stronger stress-plus, lower drawdown, lower max consecutive losses, and better positive activated periods. Selection-biased single-window discovery (win rate 55.00%, max drawdown 1.7185, max 3 consecutive losses, 77.78% positive activated periods, latency-002 net +2.8440, no sparse/overfit/single-cluster warnings).");

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
