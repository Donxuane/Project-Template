using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// FixedFrequencyForwardIncubationV1 — forward-incubation tracks for the fixed-frequency promoted
/// candidates SOLUSDT 30m Short and ETHUSDT 15m Short. Each track freezes at the current run
/// timestamp (FrozenStartUtc) and only ever judges trades strictly after it. Everything before the
/// freeze is discovery/frequency evidence, not forward proof.
///
/// Diagnostic/research only. Never places orders, never enables testnet/live trading, never modifies
/// the existing frozen BNB 5m, SOL 5m, BNB 15m, or SOL 15m tracks (all hash-protected during the run).
/// </summary>
public static class FixedFrequencyForwardIncubationV1Catalog
{
    public const string FrozenStateSubdir = "frozen";
    public const string PrimaryCostScenario = "futures-moderate";
    public const string ModerateSlippageScenario = "futures-moderate-latency-002";
    public const string StressPlusScenario = "futures-stress-plus";

    // Health-gate thresholds for the fixed-frequency testnet-order-candidate gate (fixed up-front).
    public const int MinForwardTrades = 5;
    public const decimal StressPlusFloorQuote = 0m;          // stress-plus must be strictly positive
    public const int MaxConsecutiveLossesLimit = 3;
    public const decimal MaxSingleDayProfitShare = 0.5m;     // no single day > 50% of total profit
    public const decimal MinForwardSpanDaysForJudgment = 0.5m;

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

    /// <summary>Coarse runner-side verdict; the fixed-frequency engine produces the authoritative verdict.</summary>
    public static string ResolveVerdict(
        decimal forwardSpanDays,
        int checkpointCount,
        int forwardTrades,
        decimal netModerate,
        decimal netLatency002,
        bool allHealthGatesPass)
    {
        if (forwardTrades == 0 || forwardSpanDays < MinForwardSpanDaysForJudgment || checkpointCount == 0)
            return "NotEnoughForwardDataYet";
        if (allHealthGatesPass)
            return "TestnetOrderCandidate";
        if (netModerate > 0m && netLatency002 > 0m)
            return "KeepIncubating";
        return "KeepSecondary";
    }

    public static FixedFrequencyTrack Sol30()
    {
        const string profileName =
            "Frozen_SOL_NearExtremeShort_30m_T1.00S0.75_FlowFundingNormalChk4hAct4h_FixedFrequencyV1";
        var comboKey = new CrossSymbolComboKey(
            TradingSymbol.SOLUSDT, "30m", LongShortDirection.Short,
            TargetPercent: 1.00m, StopPercent: 0.75m, MaxHoldMinutes: 240);

        CrossSymbolActivationConfig BuildConfig() => new(
            "Flow_FundingNormal_Chk4h_Act4h",
            IsAlwaysOn: false,
            CrossSymbolPerfKind.None,
            MultiSymbolActivationGate.FundingNormal,
            CheckpointFrequencyHours: 4,
            ActivationPeriodHours: 4,
            LookbackDays: 3,
            MinLookbackTrades: 0,
            ProfitFactorThreshold: null);

        FrozenCandidateState BuildState(DateTime createdAtUtc) => new(
            profileName,
            createdAtUtc,
            FrozenStartUtc: createdAtUtc,
            BaseRule: "NearHighElevatedVol (NearExtremeShort)",
            Symbol: "SOLUSDT",
            Interval: "30m",
            EntryMode: "NextClose",
            TargetPercent: 1.00m,
            StopPercent: 0.75m,
            MaxHoldMinutes: 240,
            CooldownCandles: 3,
            OverlapPolicy: "OneOpenTradePerRuleSymbol",
            ActivationFlowCondition: "FundingNormal (funding z-score not stretched), 4h checkpoint, 4h activation period",
            CheckpointFrequencyHours: 4,
            ActivationPeriodHours: 4,
            LookbackDaysInformational: 3,
            DiscoveryWindow: "Pre-freeze fixed-frequency discovery (cross-candidate exact-entry frequency study). Discovery/frequency evidence only, not forward proof.",
            DiscoveryBaselineTrades: 0,
            DiscoveryBaselineNet: 0m,
            DiscoveryCandidateTrades: 27,
            DiscoveryCandidateNet: 5.18086226m,
            DiscoveryCandidateProfitFactor: 2.004173m,
            DiscoveryCandidateStressPlusNet: 1.78521175m,
            Caveats: "Fixed-frequency forward-incubation V1. FrozenStartUtc is the freeze timestamp; only trades strictly after it are forward proof. Pre-freeze exact-entry frequency (27 entries, net +5.18 moderate, +1.79 stress-plus, win 66.67%, PF 2.00) is discovery/frequency evidence only. Activated but no current entry at freeze.");

        var refs = new CrossSymbolForwardIncubationRunner.CatalogRefs(
            ModeName: "fixed-frequency-sol30-forward-incubation-v1",
            TrackLabel: "SOL30",
            FrozenProfileName: profileName,
            Symbol: TradingSymbol.SOLUSDT,
            Interval: "30m",
            FrozenComboKey: comboKey,
            BuildFrozenActivationConfig: BuildConfig,
            BuildDefaultState: BuildState,
            FrozenStatePath: dataDir => FrozenStatePath(dataDir, profileName),
            ForwardHistoryPath: dataDir => ForwardHistoryPath(dataDir, profileName),
            CostScenarios: CostScenarios,
            PrimaryCostScenario: PrimaryCostScenario,
            ModerateSlippageScenario: ModerateSlippageScenario,
            StressPlusScenario: StressPlusScenario,
            CoverageSymbols: [TradingSymbol.SOLUSDT, TradingSymbol.BTCUSDT],
            CoverageSourceKeys: CoverageSourceKeys,
            ResolveVerdict: ResolveVerdict,
            MinForwardTrades: MinForwardTrades,
            StressPlusCollapseFloorQuote: StressPlusFloorQuote,
            MaxConsecutiveLossesLimit: MaxConsecutiveLossesLimit,
            MaxSingleDayProfitShare: MaxSingleDayProfitShare,
            MinPositivePeriodRate: 0.40m,
            MaxDrawdownToNetRatio: 1.5m,
            MinForwardSpanDaysForJudgment: MinForwardSpanDaysForJudgment,
            AnswersSymbolLabel: "SOL 30m",
            AnswersBesideTracksNote: "Fixed-frequency forward-incubation track beside frozen BNB 5m, SOL 5m, BNB 15m, and SOL 15m; existing tracks hash-protected.");

        return new FixedFrequencyTrack(
            refs.ModeName,
            "fixed-frequency-sol30-forward-incubation-v1",
            "SOLUSDT|30m|Short|T1.00|S0.75|Flow_FundingNormal_Chk4h_Act4h",
            refs);
    }

    public static FixedFrequencyTrack Eth15()
    {
        const string profileName =
            "Frozen_ETH_NearExtremeShort_15m_T1.25S0.75_PerfRecentNetPositiveChk24hAct12hLB14d_FixedFrequencyV1";
        var comboKey = new CrossSymbolComboKey(
            TradingSymbol.ETHUSDT, "15m", LongShortDirection.Short,
            TargetPercent: 1.25m, StopPercent: 0.75m, MaxHoldMinutes: 240);

        CrossSymbolActivationConfig BuildConfig() => new(
            "Perf_RecentNetPositive_Chk24h_Act12h_LB14d",
            IsAlwaysOn: false,
            CrossSymbolPerfKind.RecentNetPositive,
            MultiSymbolActivationGate.None,
            CheckpointFrequencyHours: 24,
            ActivationPeriodHours: 12,
            LookbackDays: 14,
            MinLookbackTrades: NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.MinLookbackTrades,
            ProfitFactorThreshold: null);

        FrozenCandidateState BuildState(DateTime createdAtUtc) => new(
            profileName,
            createdAtUtc,
            FrozenStartUtc: createdAtUtc,
            BaseRule: "NearHighElevatedVol (NearExtremeShort)",
            Symbol: "ETHUSDT",
            Interval: "15m",
            EntryMode: "NextClose",
            TargetPercent: 1.25m,
            StopPercent: 0.75m,
            MaxHoldMinutes: 240,
            CooldownCandles: 4,
            OverlapPolicy: "OneOpenTradePerRuleSymbol",
            ActivationFlowCondition: "None (performance-only: recent net positive over 14d lookback, min 5 trades), 24h checkpoint, 12h activation period",
            CheckpointFrequencyHours: 24,
            ActivationPeriodHours: 12,
            LookbackDaysInformational: 14,
            DiscoveryWindow: "Pre-freeze fixed-frequency discovery (cross-candidate exact-entry frequency study). Discovery/frequency evidence only, not forward proof.",
            DiscoveryBaselineTrades: 0,
            DiscoveryBaselineNet: 0m,
            DiscoveryCandidateTrades: 22,
            DiscoveryCandidateNet: 169.08485477m,
            DiscoveryCandidateProfitFactor: 3.261388m,
            DiscoveryCandidateStressPlusNet: 69.92477523m,
            Caveats: "Fixed-frequency forward-incubation V1. FrozenStartUtc is the freeze timestamp; only trades strictly after it are forward proof. Pre-freeze exact-entry frequency (22 entries, net +169.08 moderate, +69.92 stress-plus, win 68.18%, PF 3.26) is discovery/frequency evidence only. Activated but no current entry at freeze.");

        var refs = new CrossSymbolForwardIncubationRunner.CatalogRefs(
            ModeName: "fixed-frequency-eth15-forward-incubation-v1",
            TrackLabel: "ETH15",
            FrozenProfileName: profileName,
            Symbol: TradingSymbol.ETHUSDT,
            Interval: "15m",
            FrozenComboKey: comboKey,
            BuildFrozenActivationConfig: BuildConfig,
            BuildDefaultState: BuildState,
            FrozenStatePath: dataDir => FrozenStatePath(dataDir, profileName),
            ForwardHistoryPath: dataDir => ForwardHistoryPath(dataDir, profileName),
            CostScenarios: CostScenarios,
            PrimaryCostScenario: PrimaryCostScenario,
            ModerateSlippageScenario: ModerateSlippageScenario,
            StressPlusScenario: StressPlusScenario,
            CoverageSymbols: [TradingSymbol.ETHUSDT, TradingSymbol.BTCUSDT],
            CoverageSourceKeys: CoverageSourceKeys,
            ResolveVerdict: ResolveVerdict,
            MinForwardTrades: MinForwardTrades,
            StressPlusCollapseFloorQuote: StressPlusFloorQuote,
            MaxConsecutiveLossesLimit: MaxConsecutiveLossesLimit,
            MaxSingleDayProfitShare: MaxSingleDayProfitShare,
            MinPositivePeriodRate: 0.40m,
            MaxDrawdownToNetRatio: 1.5m,
            MinForwardSpanDaysForJudgment: MinForwardSpanDaysForJudgment,
            AnswersSymbolLabel: "ETH 15m",
            AnswersBesideTracksNote: "Fixed-frequency forward-incubation track beside frozen BNB 5m, SOL 5m, BNB 15m, and SOL 15m; existing tracks hash-protected.");

        return new FixedFrequencyTrack(
            refs.ModeName,
            "fixed-frequency-eth15-forward-incubation-v1",
            "ETHUSDT|15m|Short|T1.25|S0.75|Perf_RecentNetPositive_Chk24h_Act12h_LB14d",
            refs);
    }

    private static string FrozenStatePath(string dataDirectory, string profileName)
        => Path.Combine(dataDirectory, FrozenStateSubdir, profileName + ".json");

    private static string ForwardHistoryPath(string dataDirectory, string profileName)
        => Path.Combine(dataDirectory, FrozenStateSubdir, profileName + "-forward-history.json");
}

/// <summary>Bundles a fixed-frequency forward-incubation track definition for the shared runner.</summary>
public sealed record FixedFrequencyTrack
{
    internal FixedFrequencyTrack(
        string modeName,
        string outputSubdir,
        string watchlistCandidateKey,
        CrossSymbolForwardIncubationRunner.CatalogRefs catalogRefs)
    {
        ModeName = modeName;
        OutputSubdir = outputSubdir;
        WatchlistCandidateKey = watchlistCandidateKey;
        CatalogRefs = catalogRefs;
    }

    public string ModeName { get; }
    public string OutputSubdir { get; }
    public string WatchlistCandidateKey { get; }
    internal CrossSymbolForwardIncubationRunner.CatalogRefs CatalogRefs { get; }
}
