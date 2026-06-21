using Microsoft.Extensions.Configuration;

namespace TradingBot.Backtest;

public sealed record BacktestSettings(
    string AppSettingsPath,
    string DataDirectory,
    string OutputDirectory,
    IReadOnlyList<string> Intervals,
    bool BootstrapMissingData,
    int BootstrapLimit,
    int? BootstrapDays,
    DateTime? BootstrapStartUtc,
    DateTime? BootstrapEndUtc,
    decimal FeeRatePercent,
    decimal EstimatedSpreadPercent,
    decimal SlippagePercent,
    bool ForceCloseAtEnd,
    bool RunRobustness,
    bool RunReachabilityResearch,
    bool RunBroadReachabilityScan,
    bool RunRangeExpansionResearch,
    bool RunRangeExpansionFast,
    bool RunRangeExpansionV2Fast,
    bool RunRangeExpansionV2,
    bool RunRangeExpansionV21Fast,
    bool RunRangeExpansionV22,
    bool RunRangeExpansionV23,
    bool RunRangeExpansionV24,
    bool RunRangeExpansionV2Feasibility,
    bool RunRangeExpansionV2FeasibilityIncludeComparison,
    bool RunRangeExpansionV2IncludeComparison,
    bool RunRangeExpansionTargetFloorExperiment,
    bool RunRangeExpansionFastIncludeComparison,
    bool RunImpulseContinuationV1,
    bool RunImpulseContinuationV1IncludeResearchVariants,
    bool RunMeanReversionRangeBounceV1,
    bool RunMeanReversionRangeBounceV1IncludeResearchVariants,
    bool RunHigherTimeframeMomentumPullbackV1,
    bool RunHigherTimeframeMomentumPullbackV1IncludeResearchVariants,
    bool RunMarketRegimeForwardEdgeStudy,
    bool RunRegimeGatedLongEdgeV1,
    bool RunRegimeGatedLongEdgeV1IncludeResearchVariants,
    bool RunRegimeGatedLongEdgeV1BtcContext,
    bool RunMarketRegimeForwardEdgeStudyWithBtcContext,
    bool RunLongShortFuturesFeasibilityStudyV1,
    bool RunDirectionalRuleFuturesSimulationV1,
    bool RunDirectionalRuleFuturesValidationV2,
    bool RunDirectionalRuleFuturesValidationV3,
    bool RunDirectionalRuleFuturesValidationV3Focused,
    bool RunDirectionalRuleFuturesValidationV31,
    bool RunDirectionalRuleFuturesRegimeDriftV1,
    bool RunDirectionalRuleFuturesRegimeConditionalV2,
    bool RunFuturesDirectionalRuleDiscoveryV2,
    bool RunFuturesMarketDataExpansionV1,
    bool BootstrapFuturesData,
    bool RunNoPaidDataAdaptiveActivationV1,
    bool RunNoPaidShortWindowFlowResearchV1,
    bool RunNoPaidShortWindowForwardIncubationV1,
    bool RunNoPaidShortWindowMultiSymbolResearchV2,
    bool RunNoPaidShortWindowV1CrossSymbol,
    bool RunNoPaidShortWindowSolForwardIncubationV1,
    bool RunNoPaidShortWindowBnb15mForwardIncubationV1,
    bool RunNoPaidShortWindowSol15mForwardIncubationV1,
    bool RunFixedFrequencySol30ForwardIncubationV1,
    bool RunFixedFrequencyEth15ForwardIncubationV1,
    bool RunFuturesTestnetShadowRunner,
    bool RunFrozenProfileBottleneckAudit,
    bool RunCrossSymbolCandidateEngineV2,
    bool RunBnb15LookbackStarvationStudy,
    bool RunCurrentOpportunityScannerV1,
    bool RunEntryNearMissAuditV1,
    bool RunCurrentOpportunityWatchV1,
    bool RunSol30mNearMissConversionHistoryStudy,
    bool RunCrossCandidateExactEntryFrequencyStudyV1,
    bool RunCrossSymbolExactEntryReconciliationAuditV1,
    bool WatchLoop,
    int WatchIntervalMinutes,
    string? CrossSymbolV1InputDirectory,
    string? FrequencyStudyInputDirectory,
    string? OpportunityScannerInputDirectory,
    string? BottleneckAuditDirectory,
    string? ShadowRunnerDirectory,
    string? DirectionalRuleDiscoveryJsonPath,
    bool RunMetaStrategyResearch,
    IReadOnlyList<string> MetaInputDirectories,
    bool MetaIncludeBlockedCandidates,
    int MetaBlockedCandidateCap,
    IReadOnlyList<int> RobustnessWindows,
    DateTime? RobustnessWindowStartUtc,
    DateTime? RobustnessWindowEndUtc,
    bool ShowHelp);

public static class BacktestCli
{
    public const string HelpText = """
Usage:
  dotnet run --project TradingBot.Backtest -- [options]

Options:
  --appsettings <path>         Base appsettings path. Default: TradingBot/appsettings.json
  --data-dir <path>            Directory with historical 1m candles.
  --output-dir <path>          Output directory for reports.
  --interval <value>           Single interval: 1m, 3m, 5m, 15m, or 30m.
  --intervals <v1,v2,...>      Multiple intervals: e.g. 1m,3m,5m or 15m,30m.
  --bootstrap <true|false>     Download missing local files from Binance API. Default: false
  --bootstrap-limit <int>      Kline limit for bootstrap call. Default: 1000
  --bootstrap-days <days>      Historical bootstrap window in days (allowed: 7,14,30).
  --bootstrap-start <utc>      UTC start for historical bootstrap (ISO8601).
  --bootstrap-end <utc>        UTC end for historical bootstrap (ISO8601).
  --fee-rate-percent <value>   Fee rate percent for net estimate. Default from appsettings Trading:FeeRatePercent.
  --spread-percent <value>     Estimated spread percent. Default from appsettings Trading:EstimatedSpreadPercent.
  --slippage-percent <value>   Slippage percent applied to fills. Default: 0.00
  --force-close-end <true|false>  Force-close open position at end of data. Default: true
  --robustness <true|false>    Run multi-window robustness backtest for profit-lock candidates. Default: false
  --reachability-research <true|false>  Run reachability-first candidate research mode. Default: false
  --broad-reachability-scan <true|false>  Scan ETH/BNB/SOL/(BTC if data exists) across intervals. Default: false
  --range-expansion-research <true|false>  Run RangeExpansionBreakoutV1 backtest-only research. Default: false
  --range-expansion-fast <true|false>      Run narrowed 1m lock90 fast iteration profiles. Default: false
  --range-expansion-v2-fast <true|false>   Run V2 fast profiles with experimental filters (V1 model). Default: false
  --range-expansion-v2 <true|false>        Run RangeExpansionBreakoutV2 larger-move cost-aware research. Default: false
  --range-expansion-v21-fast <true|false>  Run V2.1 BNB-only fast matrix (filters/exit variants). Default: false
  --range-expansion-v22 <true|false>       Run V2.2 BNB separator-filter experiments. Default: false
  --range-expansion-v23 <true|false>       Run V2.3 failed-breakout threshold sweep. Default: false
  --range-expansion-v24 <true|false>       Run V2.4 body80 exit-policy experiments. Default: false
  --range-expansion-v2-feasibility <true|false>  Run V2 lower-fee/Futures-sim feasibility research. Default: false
  --range-expansion-v2-feasibility-comparison <true|false>  Include ETH/SOL in feasibility run. Default: false
  --range-expansion-v2-comparison <true|false>  Include ETH+BNB+SOL V2 comparison profile. Default: true
  --range-expansion-target-floor-experiment <true|false>  Compare current/relaxed/cost-aware target floors. Default: false
  --range-expansion-fast-comparison <true|false>  Include ETH+BNB+SOL fast comparison profile. Default: true
  --impulse-continuation-v1 <true|false>  Run ImpulseContinuationV1 backtest-only research. Default: false
  --impulse-continuation-v1-research-variants <true|false>  Include net/hold/target/stop research variants (1m). Default: false
  --mean-reversion-range-v1 <true|false>  Run MeanReversionRangeBounceV1 backtest-only research. Default: false
  --mean-reversion-range-v1-research-variants <true|false>  Include lookback/net/hold/target/stop research variants. Default: false
  --htf-momentum-pullback-v1 <true|false>  Run HigherTimeframeMomentumPullbackV1 backtest-only research (15m/30m). Default: false
  --htf-momentum-pullback-v1-research-variants <true|false>  Include net/hold/target/lock research variants. Default: false
  --market-regime-forward-edge-study <true|false>  Run market-regime forward-edge discovery study (backtest-only). Default: false
  --regime-gated-long-edge-v1 <true|false>  Run RegimeGatedLongEdgeV1 simulated-trade prototype (backtest-only). Default: false
  --regime-gated-long-edge-v1-research-variants <true|false>  Include hold/confirmation/SOL/ETH research variants. Default: false
  --regime-gated-long-edge-v1-btc-context <true|false>  Run BNB 30m regime gates with BTC-favorable context (backtest-only). Default: false
  --market-regime-forward-edge-study-with-btc-context <true|false>  Run regime forward-edge study with BTC context bootstrap. Default: false
  --long-short-futures-feasibility-v1 <true|false>  Run long/short Futures-sim feasibility study (backtest-only). Default: false
  --directional-rule-futures-simulation-v1 <true|false>  Run directional-rule Futures trade simulation (backtest-only). Default: false
  --directional-rule-futures-validation-v2 <true|false>  Run narrow directional-rule Futures validation (backtest-only). Default: false
  --directional-rule-futures-validation-v3 <true|false>  Run focused BNB Rule01 short validation (backtest-only). Default: false
  --directional-rule-futures-validation-v3-focused <true|false>  Use small focused variant matrix (default true when v3 enabled). Default: true
  --directional-rule-futures-validation-v3-full-matrix <true|false>  Run full 144-profile matrix instead of focused set. Default: false
  --directional-rule-futures-validation-v31 <true|false>  Run V31 long-history + cross-symbol generalization (backtest-only). Default: false
  --directional-rule-futures-regime-drift-v1 <true|false>  Run BNB Rule01 short regime-drift diagnostic (backtest-only). Default: false
  --directional-rule-futures-regime-conditional-v2 <true|false>  Run BNB Rule01 short regime-conditional activation matrix (backtest-only). Default: false
  --futures-directional-rule-discovery-v2 <true|false>  Run long-history directional rule discovery with train/validation/holdout (backtest-only). Default: false
  --futures-market-data-expansion-v1 <true|false>  Investigate richer futures data (funding/OI/taker/long-short/mark-index) + flow-feature edge study (backtest/data-research only). Default: false
  --bootstrap-futures-data <true|false>  Download futures market data into data/futures/ before the expansion study (opt-in, non-fatal). Default: false
  --no-paid-data-adaptive-activation-v1 <true|false>  Walk-forward adaptive activation study for Rule01 short BNB (backtest-only, no paid data). Default: false
  --no-paid-short-window-flow-research-v1 <true|false>  Short-window free-flow-data adaptive activation research for Rule01 short BNB (backtest/data-research only, no paid data). Combine with --bootstrap-futures-data true to refresh/accumulate free flow data. Default: false
  --no-paid-short-window-forward-incubation-v1 <true|false>  Forward incubation of the frozen candidate Frozen_BNB_Rule01Short_FundingNormal_Daily24h_V1 (backtest-only, no optimization, forward-only judgment). Combine with --bootstrap-futures-data true to keep merging free flow data. Default: false
  --no-paid-short-window-multisymbol-v2 <true|false>  Multi-symbol/multi-family short-window research (BTC/ETH/BNB/SOL, 5m/15m/30m, long+short) with split validation and walk-forward activation; never touches the frozen BNB incubation track. Default: false
  --no-paid-short-window-v1-cross-symbol <true|false>  V1-style short-window flow activation research generalized to BTC/ETH/BNB/SOL across 5m/15m/30m, long+short, and a fixed trade geometry grid; never touches the frozen BNB incubation track. Default: false
  --no-paid-short-window-sol-forward-incubation-v1 <true|false>  Forward incubation of the frozen SOL candidate Frozen_SOL_NearExtremeShort_5m_T1.75S1.00_FlowBtcContext60mAgreesChk24hAct24h_CrossSymbolV1 (backtest-only, no optimization, forward-only judgment; second track beside BNB). Combine with --bootstrap-futures-data true to keep merging free flow data. Default: false
  --no-paid-short-window-bnb-15m-forward-incubation-v1 <true|false>  Forward incubation of the frozen BNB 15m cross-symbol proposal Frozen_BNB_NearExtremeShort_15m_T1.75S1.00_PerfRecentNetPositiveChk4hAct72hLB3d_CrossSymbolProposal (backtest-only, third track beside BNB 5m and SOL 5m). Default: false
  --no-paid-short-window-sol-15m-forward-incubation-v1 <true|false>  Forward incubation of the frozen SOL 15m cross-symbol proposal Frozen_SOL_NearExtremeShort_15m_T1.75S1.00_FlowLongShortStretchedAgainstDirectionChk24hAct72h_CrossSymbolProposal (backtest-only, fourth track beside existing frozen tracks). Default: false
  --fixed-frequency-sol30-forward-incubation-v1 <true|false>  Forward incubation of the fixed-frequency promoted SOLUSDT 30m Short T1.00/S0.75 Flow_FundingNormal_Chk4h_Act4h candidate (Frozen_SOL_NearExtremeShort_30m_T1.00S0.75_FlowFundingNormalChk4hAct4h_FixedFrequencyV1). Freezes at the current run timestamp; forward-only judgment. Diagnostic/research only — no orders, testnet/live disabled, existing frozen tracks hash-protected. Default: false
  --fixed-frequency-eth15-forward-incubation-v1 <true|false>  Forward incubation of the fixed-frequency promoted ETHUSDT 15m Short T1.25/S0.75 Perf_RecentNetPositive_Chk24h_Act12h_LB14d candidate (Frozen_ETH_NearExtremeShort_15m_T1.25S0.75_PerfRecentNetPositiveChk24hAct12hLB14d_FixedFrequencyV1). Freezes at the current run timestamp; forward-only judgment. Diagnostic/research only — no orders, testnet/live disabled, existing frozen tracks hash-protected. Default: false
  --futures-testnet-shadow-runner <true|false>  Evaluate all four frozen incubation profiles at latest data and emit would-place-order shadow records (dry-run/testnet-shadow only; never places real orders by default). Combine with --bootstrap-futures-data true to refresh candles/flow cache. Default: false
  --frozen-profile-bottleneck-audit <true|false>  Diagnostic-only audit explaining why each frozen profile is or is not producing actionable trades. Writes frozen-profile-bottleneck-audit.{json,csv,txt}. Does not change strategy logic, thresholds, frozen profiles, health gates, or verdicts. Default: false
  --cross-symbol-candidate-engine-v2 <true|false>  Shadow/research candidate factory reading cross-symbol V1 outputs. Writes cross-symbol-candidate-engine-v2-* reports. Does not place orders or modify frozen profiles. Default: false
  --bnb15-lookback-starvation-study <true|false>  Diagnostic root-cause study for BNBUSDT 15m lookback starvation (does not modify frozen profile). Combine with --bootstrap-futures-data true to refresh data. Default: false
  --current-opportunity-scanner-v1 <true|false>  Diagnostic/shadow-only scan of cross-symbol V1/V2 candidates for current actionable shadow opportunities. Never places orders. Combine with --bootstrap-futures-data true to refresh candles/flow cache. Default: false
  --entry-near-miss-audit-v1 <true|false>  Diagnostic/shadow-only audit measuring how close activation-passed candidates are to base entry signals. Never places orders or changes strategy logic. Default: false
  --current-opportunity-watch-v1 <true|false>  Diagnostic/shadow-only watcher that refreshes scanner + near-miss audit, records history, and flags exact entry signals. Never places orders. Default: false
  --sol30m-near-miss-conversion-history <true|false>  Diagnostic historical study of SOLUSDT 30m Short near-miss conversion into exact entry signals. Never places orders or changes strategy logic. Default: false
  --cross-candidate-exact-entry-frequency-v1 <true|false>  Diagnostic study ranking all cross-symbol candidates by historical exact-entry frequency under activation. Never places orders or changes strategy logic. Default: false
  --cross-symbol-exact-entry-reconciliation-v1 <true|false>  Diagnostic reconciliation audit comparing V1 discovery trades vs exact-entry frequency study output. Never places orders or changes strategy logic. Default: false
  --watch-loop <true|false>  When used with --current-opportunity-watch-v1, repeat evaluation until interrupted. Default: false
  --watch-interval-minutes <int>  Loop interval for --current-opportunity-watch-v1 when --watch-loop true. Default: 5
  --opportunity-scanner-input-dir <path>          Directory containing current-opportunity-scanner-v1-candidates.json for entry near-miss audit. Default: sibling current-opportunity-scanner-v1 under output root.
  --cross-symbol-v1-input-dir <path>              Directory containing cross-symbol-v1-*.json inputs for candidate engine V2. Default: sibling no-paid-short-window-v1-cross-symbol under output root.
  --frequency-study-input-dir <path>              Directory containing cross-candidate-exact-entry-frequency-v1-*.json for reconciliation audit. Default: sibling cross-candidate-exact-entry-frequency-v1 under output root.
  --bottleneck-audit-dir <path>                   Optional frozen-profile-bottleneck-audit directory for candidate engine V2 integration.
  --shadow-runner-dir <path>                      Optional futures-testnet-shadow-runner directory for candidate engine V2 forward reality overlay.
  --directional-rule-discovery-json <path>  Optional path to long-short-entry-time-rule-discovery.json. Default: feasibility v1 run output.
  --meta-strategy-research <true|false>  Aggregate completed family outputs into unified meta-research reports. Default: false
  --meta-input-dirs <path1,path2,...>  Input directories with family trade JSON outputs. Default: scan TradingBot.Backtest/output.
  --meta-include-blocked <true|false>  Import blocked candidates (large files may be capped). Default: false
  --meta-blocked-cap <int>  Max blocked candidates per source when importing. Default: 50000
  --robustness-windows <days>  Rolling replay windows in days (e.g. 30,60,90). Default: 30,60,90
  --robustness-window-start <utc>  Optional fixed replay window start (ISO8601).
  --robustness-window-end <utc>    Optional fixed replay window end (ISO8601).
  --help                       Show this help.
""";

    public static BacktestSettings Parse(string[] args)
    {
        var repoRoot = Directory.GetCurrentDirectory();
        var defaultAppSettings = Path.Combine(repoRoot, "TradingBot", "appsettings.json");
        var defaultDataDir = Path.Combine(repoRoot, "TradingBot.Backtest", "data");
        var defaultOutputRoot = Path.Combine(repoRoot, "TradingBot.Backtest", "output");
        var defaultOutputDir = Path.Combine(defaultOutputRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));

        string appSettingsPath = defaultAppSettings;
        string dataDir = defaultDataDir;
        string outputDir = defaultOutputDir;
        IReadOnlyList<string>? intervals = null;
        var bootstrap = false;
        var bootstrapLimit = 1000;
        int? bootstrapDays = null;
        DateTime? bootstrapStartUtc = null;
        DateTime? bootstrapEndUtc = null;
        decimal? feeRatePercent = null;
        decimal? spreadPercent = null;
        var slippagePercent = 0m;
        var forceCloseAtEnd = true;
        var runRobustness = false;
        var runReachabilityResearch = false;
        var runBroadReachabilityScan = false;
        var runRangeExpansionResearch = false;
        var runRangeExpansionFast = false;
        var runRangeExpansionV2Fast = false;
        var runRangeExpansionV2 = false;
        var runRangeExpansionV21Fast = false;
        var runRangeExpansionV22 = false;
        var runRangeExpansionV23 = false;
        var runRangeExpansionV24 = false;
        var runRangeExpansionV2Feasibility = false;
        var runRangeExpansionV2FeasibilityIncludeComparison = false;
        var runRangeExpansionV2IncludeComparison = true;
        var runRangeExpansionTargetFloorExperiment = false;
        var runRangeExpansionFastIncludeComparison = true;
        var runImpulseContinuationV1 = false;
        var runImpulseContinuationV1IncludeResearchVariants = false;
        var runMeanReversionRangeBounceV1 = false;
        var runMeanReversionRangeBounceV1IncludeResearchVariants = false;
        var runHigherTimeframeMomentumPullbackV1 = false;
        var runHigherTimeframeMomentumPullbackV1IncludeResearchVariants = false;
        var runMarketRegimeForwardEdgeStudy = false;
        var runRegimeGatedLongEdgeV1 = false;
        var runRegimeGatedLongEdgeV1IncludeResearchVariants = false;
        var runRegimeGatedLongEdgeV1BtcContext = false;
        var runMarketRegimeForwardEdgeStudyWithBtcContext = false;
        var runLongShortFuturesFeasibilityStudyV1 = false;
        var runDirectionalRuleFuturesSimulationV1 = false;
        var runDirectionalRuleFuturesValidationV2 = false;
        var runDirectionalRuleFuturesValidationV3 = false;
        var runDirectionalRuleFuturesValidationV31 = false;
        var runDirectionalRuleFuturesRegimeDriftV1 = false;
        var runDirectionalRuleFuturesRegimeConditionalV2 = false;
        var runFuturesDirectionalRuleDiscoveryV2 = false;
        var runFuturesMarketDataExpansionV1 = false;
        var bootstrapFuturesData = false;
        var runNoPaidDataAdaptiveActivationV1 = false;
        var runNoPaidShortWindowFlowResearchV1 = false;
        var runNoPaidShortWindowForwardIncubationV1 = false;
        var runNoPaidShortWindowMultiSymbolResearchV2 = false;
        var runNoPaidShortWindowV1CrossSymbol = false;
        var runNoPaidShortWindowSolForwardIncubationV1 = false;
        var runNoPaidShortWindowBnb15mForwardIncubationV1 = false;
        var runNoPaidShortWindowSol15mForwardIncubationV1 = false;
        var runFixedFrequencySol30ForwardIncubationV1 = false;
        var runFixedFrequencyEth15ForwardIncubationV1 = false;
        var runFuturesTestnetShadowRunner = false;
        var runFrozenProfileBottleneckAudit = false;
        var runCrossSymbolCandidateEngineV2 = false;
        var runBnb15LookbackStarvationStudy = false;
        var runCurrentOpportunityScannerV1 = false;
        var runEntryNearMissAuditV1 = false;
        var runCurrentOpportunityWatchV1 = false;
        var runSol30mNearMissConversionHistoryStudy = false;
        var runCrossCandidateExactEntryFrequencyStudyV1 = false;
        var runCrossSymbolExactEntryReconciliationAuditV1 = false;
        var watchLoop = false;
        var watchIntervalMinutes = CurrentOpportunityWatchV1Catalog.DefaultWatchIntervalMinutes;
        string? crossSymbolV1InputDirectory = null;
        string? frequencyStudyInputDirectory = null;
        string? opportunityScannerInputDirectory = null;
        string? bottleneckAuditDirectory = null;
        string? shadowRunnerDirectory = null;
        var runDirectionalRuleFuturesValidationV3Focused = true;
        var runDirectionalRuleFuturesValidationV3FullMatrix = false;
        string? directionalRuleDiscoveryJsonPath = null;
        var runMetaStrategyResearch = false;
        IReadOnlyList<string>? metaInputDirectories = null;
        var metaIncludeBlockedCandidates = false;
        var metaBlockedCandidateCap = 50_000;
        IReadOnlyList<int>? robustnessWindows = null;
        DateTime? robustnessWindowStartUtc = null;
        DateTime? robustnessWindowEndUtc = null;
        var showHelp = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i].Trim();
            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase))
            {
                showHelp = true;
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
                continue;

            if (i + 1 >= args.Length)
                throw new ArgumentException($"Missing value for argument '{arg}'.");

            var value = args[++i].Trim();
            switch (arg)
            {
                case "--appsettings":
                    appSettingsPath = value;
                    break;
                case "--data-dir":
                    dataDir = value;
                    break;
                case "--output-dir":
                    outputDir = value;
                    break;
                case "--bootstrap":
                    bootstrap = ParseBool(value, arg);
                    break;
                case "--interval":
                    intervals = [NormalizeInterval(value)];
                    break;
                case "--intervals":
                    intervals = ParseIntervals(value);
                    break;
                case "--bootstrap-limit":
                    bootstrapLimit = Math.Clamp(ParseInt(value, arg), 1, 1500);
                    break;
                case "--bootstrap-days":
                    bootstrapDays = ParseBootstrapDays(value);
                    break;
                case "--bootstrap-start":
                    bootstrapStartUtc = ParseUtcDateTime(value, arg);
                    break;
                case "--bootstrap-end":
                    bootstrapEndUtc = ParseUtcDateTime(value, arg);
                    break;
                case "--fee-rate-percent":
                    feeRatePercent = ParseDecimal(value, arg);
                    break;
                case "--spread-percent":
                    spreadPercent = ParseDecimal(value, arg);
                    break;
                case "--slippage-percent":
                    slippagePercent = ParseDecimal(value, arg);
                    break;
                case "--force-close-end":
                    forceCloseAtEnd = ParseBool(value, arg);
                    break;
                case "--robustness":
                    runRobustness = ParseBool(value, arg);
                    break;
                case "--reachability-research":
                    runReachabilityResearch = ParseBool(value, arg);
                    break;
                case "--broad-reachability-scan":
                    runBroadReachabilityScan = ParseBool(value, arg);
                    break;
                case "--range-expansion-research":
                    runRangeExpansionResearch = ParseBool(value, arg);
                    break;
                case "--range-expansion-fast":
                    runRangeExpansionFast = ParseBool(value, arg);
                    break;
                case "--range-expansion-v2-fast":
                    runRangeExpansionV2Fast = ParseBool(value, arg);
                    break;
                case "--range-expansion-v2":
                    runRangeExpansionV2 = ParseBool(value, arg);
                    break;
                case "--range-expansion-v21-fast":
                    runRangeExpansionV21Fast = ParseBool(value, arg);
                    break;
                case "--range-expansion-v22":
                    runRangeExpansionV22 = ParseBool(value, arg);
                    break;
                case "--range-expansion-v23":
                    runRangeExpansionV23 = ParseBool(value, arg);
                    break;
                case "--range-expansion-v24":
                    runRangeExpansionV24 = ParseBool(value, arg);
                    break;
                case "--range-expansion-v2-feasibility":
                    runRangeExpansionV2Feasibility = ParseBool(value, arg);
                    break;
                case "--range-expansion-v2-feasibility-comparison":
                    runRangeExpansionV2FeasibilityIncludeComparison = ParseBool(value, arg);
                    break;
                case "--range-expansion-v2-comparison":
                    runRangeExpansionV2IncludeComparison = ParseBool(value, arg);
                    break;
                case "--range-expansion-target-floor-experiment":
                    runRangeExpansionTargetFloorExperiment = ParseBool(value, arg);
                    break;
                case "--range-expansion-fast-comparison":
                    runRangeExpansionFastIncludeComparison = ParseBool(value, arg);
                    break;
                case "--impulse-continuation-v1":
                    runImpulseContinuationV1 = ParseBool(value, arg);
                    break;
                case "--impulse-continuation-v1-research-variants":
                    runImpulseContinuationV1IncludeResearchVariants = ParseBool(value, arg);
                    break;
                case "--mean-reversion-range-v1":
                    runMeanReversionRangeBounceV1 = ParseBool(value, arg);
                    break;
                case "--mean-reversion-range-v1-research-variants":
                    runMeanReversionRangeBounceV1IncludeResearchVariants = ParseBool(value, arg);
                    break;
                case "--htf-momentum-pullback-v1":
                    runHigherTimeframeMomentumPullbackV1 = ParseBool(value, arg);
                    break;
                case "--htf-momentum-pullback-v1-research-variants":
                    runHigherTimeframeMomentumPullbackV1IncludeResearchVariants = ParseBool(value, arg);
                    break;
                case "--market-regime-forward-edge-study":
                    runMarketRegimeForwardEdgeStudy = ParseBool(value, arg);
                    break;
                case "--regime-gated-long-edge-v1":
                    runRegimeGatedLongEdgeV1 = ParseBool(value, arg);
                    break;
                case "--regime-gated-long-edge-v1-research-variants":
                    runRegimeGatedLongEdgeV1IncludeResearchVariants = ParseBool(value, arg);
                    break;
                case "--regime-gated-long-edge-v1-btc-context":
                    runRegimeGatedLongEdgeV1BtcContext = ParseBool(value, arg);
                    break;
                case "--market-regime-forward-edge-study-with-btc-context":
                    runMarketRegimeForwardEdgeStudyWithBtcContext = ParseBool(value, arg);
                    break;
                case "--long-short-futures-feasibility-v1":
                    runLongShortFuturesFeasibilityStudyV1 = ParseBool(value, arg);
                    break;
                case "--directional-rule-futures-simulation-v1":
                    runDirectionalRuleFuturesSimulationV1 = ParseBool(value, arg);
                    break;
                case "--directional-rule-futures-validation-v2":
                    runDirectionalRuleFuturesValidationV2 = ParseBool(value, arg);
                    break;
                case "--directional-rule-futures-validation-v3":
                    runDirectionalRuleFuturesValidationV3 = ParseBool(value, arg);
                    break;
                case "--directional-rule-futures-validation-v3-focused":
                    runDirectionalRuleFuturesValidationV3Focused = ParseBool(value, arg);
                    break;
                case "--directional-rule-futures-validation-v3-full-matrix":
                    runDirectionalRuleFuturesValidationV3FullMatrix = ParseBool(value, arg);
                    if (runDirectionalRuleFuturesValidationV3FullMatrix)
                        runDirectionalRuleFuturesValidationV3Focused = false;
                    break;
                case "--directional-rule-futures-validation-v31":
                    runDirectionalRuleFuturesValidationV31 = ParseBool(value, arg);
                    break;
                case "--directional-rule-futures-regime-drift-v1":
                    runDirectionalRuleFuturesRegimeDriftV1 = ParseBool(value, arg);
                    break;
                case "--directional-rule-futures-regime-conditional-v2":
                    runDirectionalRuleFuturesRegimeConditionalV2 = ParseBool(value, arg);
                    break;
                case "--futures-directional-rule-discovery-v2":
                    runFuturesDirectionalRuleDiscoveryV2 = ParseBool(value, arg);
                    break;
                case "--futures-market-data-expansion-v1":
                    runFuturesMarketDataExpansionV1 = ParseBool(value, arg);
                    break;
                case "--bootstrap-futures-data":
                    bootstrapFuturesData = ParseBool(value, arg);
                    break;
                case "--no-paid-data-adaptive-activation-v1":
                    runNoPaidDataAdaptiveActivationV1 = ParseBool(value, arg);
                    break;
                case "--no-paid-short-window-flow-research-v1":
                    runNoPaidShortWindowFlowResearchV1 = ParseBool(value, arg);
                    break;
                case "--no-paid-short-window-forward-incubation-v1":
                    runNoPaidShortWindowForwardIncubationV1 = ParseBool(value, arg);
                    break;
                case "--no-paid-short-window-multisymbol-v2":
                    runNoPaidShortWindowMultiSymbolResearchV2 = ParseBool(value, arg);
                    break;
                case "--no-paid-short-window-v1-cross-symbol":
                    runNoPaidShortWindowV1CrossSymbol = ParseBool(value, arg);
                    break;
                case "--no-paid-short-window-sol-forward-incubation-v1":
                    runNoPaidShortWindowSolForwardIncubationV1 = ParseBool(value, arg);
                    break;
                case "--no-paid-short-window-bnb-15m-forward-incubation-v1":
                    runNoPaidShortWindowBnb15mForwardIncubationV1 = ParseBool(value, arg);
                    break;
                case "--no-paid-short-window-sol-15m-forward-incubation-v1":
                    runNoPaidShortWindowSol15mForwardIncubationV1 = ParseBool(value, arg);
                    break;
                case "--fixed-frequency-sol30-forward-incubation-v1":
                    runFixedFrequencySol30ForwardIncubationV1 = ParseBool(value, arg);
                    break;
                case "--fixed-frequency-eth15-forward-incubation-v1":
                    runFixedFrequencyEth15ForwardIncubationV1 = ParseBool(value, arg);
                    break;
                case "--futures-testnet-shadow-runner":
                    runFuturesTestnetShadowRunner = ParseBool(value, arg);
                    break;
                case "--frozen-profile-bottleneck-audit":
                    runFrozenProfileBottleneckAudit = ParseBool(value, arg);
                    break;
                case "--cross-symbol-candidate-engine-v2":
                    runCrossSymbolCandidateEngineV2 = ParseBool(value, arg);
                    break;
                case "--bnb15-lookback-starvation-study":
                    runBnb15LookbackStarvationStudy = ParseBool(value, arg);
                    break;
                case "--current-opportunity-scanner-v1":
                    runCurrentOpportunityScannerV1 = ParseBool(value, arg);
                    break;
                case "--entry-near-miss-audit-v1":
                    runEntryNearMissAuditV1 = ParseBool(value, arg);
                    break;
                case "--current-opportunity-watch-v1":
                    runCurrentOpportunityWatchV1 = ParseBool(value, arg);
                    break;
                case "--sol30m-near-miss-conversion-history":
                    runSol30mNearMissConversionHistoryStudy = ParseBool(value, arg);
                    break;
                case "--cross-candidate-exact-entry-frequency-v1":
                    runCrossCandidateExactEntryFrequencyStudyV1 = ParseBool(value, arg);
                    break;
                case "--cross-symbol-exact-entry-reconciliation-v1":
                    runCrossSymbolExactEntryReconciliationAuditV1 = ParseBool(value, arg);
                    break;
                case "--watch-loop":
                    watchLoop = ParseBool(value, arg);
                    break;
                case "--watch-interval-minutes":
                    watchIntervalMinutes = Math.Max(1, ParseInt(value, arg));
                    break;
                case "--opportunity-scanner-input-dir":
                    opportunityScannerInputDirectory = value;
                    break;
                case "--cross-symbol-v1-input-dir":
                    crossSymbolV1InputDirectory = value;
                    break;
                case "--frequency-study-input-dir":
                    frequencyStudyInputDirectory = value;
                    break;
                case "--bottleneck-audit-dir":
                    bottleneckAuditDirectory = value;
                    break;
                case "--shadow-runner-dir":
                    shadowRunnerDirectory = value;
                    break;
                case "--directional-rule-discovery-json":
                    directionalRuleDiscoveryJsonPath = value;
                    break;
                case "--meta-strategy-research":
                    runMetaStrategyResearch = ParseBool(value, arg);
                    break;
                case "--meta-input-dirs":
                    metaInputDirectories = ParseCsvPaths(value);
                    break;
                case "--meta-include-blocked":
                    metaIncludeBlockedCandidates = ParseBool(value, arg);
                    break;
                case "--meta-blocked-cap":
                    metaBlockedCandidateCap = Math.Max(0, ParseInt(value, arg));
                    break;
                case "--robustness-windows":
                    robustnessWindows = ParseRobustnessWindows(value);
                    break;
                case "--robustness-window-start":
                    robustnessWindowStartUtc = ParseUtcDateTime(value, arg);
                    break;
                case "--robustness-window-end":
                    robustnessWindowEndUtc = ParseUtcDateTime(value, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{arg}'.");
            }
        }

        if (!File.Exists(appSettingsPath) && !showHelp)
            throw new FileNotFoundException($"Appsettings file not found: {appSettingsPath}");
        intervals ??= runBroadReachabilityScan || runReachabilityResearch || runRangeExpansionResearch || runRangeExpansionFast || runRangeExpansionV2Fast || runRangeExpansionV2 || runRangeExpansionV21Fast || runRangeExpansionV22 || runRangeExpansionV23 || runRangeExpansionV24 || runRangeExpansionV2Feasibility || runRangeExpansionTargetFloorExperiment || runImpulseContinuationV1 || runMeanReversionRangeBounceV1 || runHigherTimeframeMomentumPullbackV1 || runMarketRegimeForwardEdgeStudy || runMarketRegimeForwardEdgeStudyWithBtcContext || runLongShortFuturesFeasibilityStudyV1 || runDirectionalRuleFuturesSimulationV1 || runDirectionalRuleFuturesValidationV2 || runDirectionalRuleFuturesValidationV3 || runDirectionalRuleFuturesValidationV31 || runDirectionalRuleFuturesRegimeDriftV1 || runDirectionalRuleFuturesRegimeConditionalV2 || runFuturesDirectionalRuleDiscoveryV2 || runFuturesMarketDataExpansionV1 || runNoPaidDataAdaptiveActivationV1 || runNoPaidShortWindowFlowResearchV1 || runNoPaidShortWindowForwardIncubationV1 || runNoPaidShortWindowMultiSymbolResearchV2 || runNoPaidShortWindowV1CrossSymbol || runNoPaidShortWindowSolForwardIncubationV1 || runNoPaidShortWindowBnb15mForwardIncubationV1 || runNoPaidShortWindowSol15mForwardIncubationV1 || runFixedFrequencySol30ForwardIncubationV1 || runFixedFrequencyEth15ForwardIncubationV1 || runFuturesTestnetShadowRunner || runFrozenProfileBottleneckAudit || runCrossSymbolCandidateEngineV2 || runBnb15LookbackStarvationStudy || runCurrentOpportunityScannerV1 || runEntryNearMissAuditV1 || runCurrentOpportunityWatchV1 || runSol30mNearMissConversionHistoryStudy || runCrossCandidateExactEntryFrequencyStudyV1 || runCrossSymbolExactEntryReconciliationAuditV1 || runRegimeGatedLongEdgeV1 || runRegimeGatedLongEdgeV1BtcContext || runMetaStrategyResearch
            ? runHigherTimeframeMomentumPullbackV1 || runRegimeGatedLongEdgeV1 || runRegimeGatedLongEdgeV1BtcContext ? ["15m", "30m"]
            : runFuturesTestnetShadowRunner || runFrozenProfileBottleneckAudit || runCrossSymbolCandidateEngineV2 || runBnb15LookbackStarvationStudy || runCurrentOpportunityScannerV1 || runEntryNearMissAuditV1 || runCurrentOpportunityWatchV1 || runSol30mNearMissConversionHistoryStudy || runCrossCandidateExactEntryFrequencyStudyV1 || runCrossSymbolExactEntryReconciliationAuditV1 ? ["5m", "15m", "30m"]
            : runNoPaidDataAdaptiveActivationV1 || runNoPaidShortWindowFlowResearchV1 || runNoPaidShortWindowForwardIncubationV1 || runNoPaidShortWindowSolForwardIncubationV1 ? ["5m"]
            : runNoPaidShortWindowBnb15mForwardIncubationV1 || runNoPaidShortWindowSol15mForwardIncubationV1 || runFixedFrequencyEth15ForwardIncubationV1 ? ["15m"]
            : runFixedFrequencySol30ForwardIncubationV1 ? ["30m"]
            : runNoPaidShortWindowMultiSymbolResearchV2 || runNoPaidShortWindowV1CrossSymbol ? ["5m", "15m", "30m"]
            : runFuturesMarketDataExpansionV1 ? ["30m"]
            : runFuturesDirectionalRuleDiscoveryV2 ? ["5m", "15m", "30m"]
            : runDirectionalRuleFuturesRegimeDriftV1 || runDirectionalRuleFuturesRegimeConditionalV2 ? ["5m"]
            : runDirectionalRuleFuturesValidationV31 ? ["5m", "15m", "30m"]
            : runDirectionalRuleFuturesValidationV3 ? ["5m"]
            : runLongShortFuturesFeasibilityStudyV1 || runDirectionalRuleFuturesSimulationV1 || runDirectionalRuleFuturesValidationV2 ? ["5m", "15m", "30m"]
            : runMarketRegimeForwardEdgeStudy || runMarketRegimeForwardEdgeStudyWithBtcContext ? ["1m", "3m", "5m", "15m", "30m"]
            : runMetaStrategyResearch || runRangeExpansionFast || runRangeExpansionV2Fast || runRangeExpansionV2 || runRangeExpansionV21Fast || runRangeExpansionV22 || runRangeExpansionV23 || runRangeExpansionV24 || runRangeExpansionV2Feasibility || runRangeExpansionTargetFloorExperiment ? ["1m"] : ["1m", "3m", "5m"]
            : runRobustness ? ["1m", "3m", "5m"] : ["1m"];
        if (runReachabilityResearch && runRobustness)
            throw new ArgumentException("Use either --reachability-research or --robustness, not both.");
        var rangeExpansionModes = new[] { runRangeExpansionResearch, runRangeExpansionFast, runRangeExpansionV2Fast, runRangeExpansionV2, runRangeExpansionV21Fast, runRangeExpansionV22, runRangeExpansionV23, runRangeExpansionV24, runRangeExpansionV2Feasibility, runRangeExpansionTargetFloorExperiment, runImpulseContinuationV1, runMeanReversionRangeBounceV1, runHigherTimeframeMomentumPullbackV1, runMarketRegimeForwardEdgeStudy, runMarketRegimeForwardEdgeStudyWithBtcContext, runLongShortFuturesFeasibilityStudyV1, runDirectionalRuleFuturesSimulationV1, runDirectionalRuleFuturesValidationV2, runDirectionalRuleFuturesValidationV3, runDirectionalRuleFuturesValidationV31, runDirectionalRuleFuturesRegimeDriftV1, runDirectionalRuleFuturesRegimeConditionalV2, runFuturesDirectionalRuleDiscoveryV2, runFuturesMarketDataExpansionV1, runNoPaidDataAdaptiveActivationV1, runNoPaidShortWindowFlowResearchV1, runNoPaidShortWindowForwardIncubationV1, runNoPaidShortWindowMultiSymbolResearchV2, runNoPaidShortWindowV1CrossSymbol, runNoPaidShortWindowSolForwardIncubationV1, runNoPaidShortWindowBnb15mForwardIncubationV1, runNoPaidShortWindowSol15mForwardIncubationV1, runFixedFrequencySol30ForwardIncubationV1, runFixedFrequencyEth15ForwardIncubationV1, runFuturesTestnetShadowRunner, runFrozenProfileBottleneckAudit, runCrossSymbolCandidateEngineV2, runBnb15LookbackStarvationStudy, runCurrentOpportunityScannerV1, runEntryNearMissAuditV1, runCurrentOpportunityWatchV1, runSol30mNearMissConversionHistoryStudy, runCrossCandidateExactEntryFrequencyStudyV1, runCrossSymbolExactEntryReconciliationAuditV1, runRegimeGatedLongEdgeV1, runRegimeGatedLongEdgeV1BtcContext, runMetaStrategyResearch }.Count(x => x);
        if (runBroadReachabilityScan && (runRobustness || runReachabilityResearch || rangeExpansionModes > 0))
            throw new ArgumentException("Use only one research/backtest mode at a time.");
        if (rangeExpansionModes > 1)
            throw new ArgumentException("Use only one of --range-expansion-research, --range-expansion-fast, --range-expansion-v2-fast, --range-expansion-v2, --range-expansion-v21-fast, --range-expansion-v22, --range-expansion-v23, --range-expansion-v24, --range-expansion-v2-feasibility, --range-expansion-target-floor-experiment, --impulse-continuation-v1, --mean-reversion-range-v1, --htf-momentum-pullback-v1, --market-regime-forward-edge-study, --market-regime-forward-edge-study-with-btc-context, --long-short-futures-feasibility-v1, --directional-rule-futures-simulation-v1, --directional-rule-futures-validation-v2, --directional-rule-futures-validation-v3, --directional-rule-futures-validation-v31, --directional-rule-futures-regime-drift-v1, --directional-rule-futures-regime-conditional-v2, --futures-directional-rule-discovery-v2, --futures-market-data-expansion-v1, --no-paid-data-adaptive-activation-v1, --no-paid-short-window-flow-research-v1, --no-paid-short-window-forward-incubation-v1, --no-paid-short-window-multisymbol-v2, --no-paid-short-window-v1-cross-symbol, --no-paid-short-window-sol-forward-incubation-v1, --no-paid-short-window-bnb-15m-forward-incubation-v1, --no-paid-short-window-sol-15m-forward-incubation-v1, --fixed-frequency-sol30-forward-incubation-v1, --fixed-frequency-eth15-forward-incubation-v1, --futures-testnet-shadow-runner, --regime-gated-long-edge-v1, --regime-gated-long-edge-v1-btc-context, or --meta-strategy-research.");
        if (runRegimeGatedLongEdgeV1 && runRegimeGatedLongEdgeV1BtcContext)
            throw new ArgumentException("Use either --regime-gated-long-edge-v1 or --regime-gated-long-edge-v1-btc-context, not both.");
        if (runMarketRegimeForwardEdgeStudy && runMarketRegimeForwardEdgeStudyWithBtcContext)
            throw new ArgumentException("Use either --market-regime-forward-edge-study or --market-regime-forward-edge-study-with-btc-context, not both.");
        if (runRegimeGatedLongEdgeV1BtcContext)
            runRegimeGatedLongEdgeV1 = true;
        if (runRangeExpansionResearch && (runRobustness || runReachabilityResearch))
            throw new ArgumentException("Use only one of --range-expansion-research, --reachability-research, or --robustness.");
        ValidateBootstrapWindow(bootstrapDays, bootstrapStartUtc, bootstrapEndUtc);
        ValidateRobustnessWindow(robustnessWindowStartUtc, robustnessWindowEndUtc);
        if (runFuturesTestnetShadowRunner && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, FuturesTestnetShadowCatalog.DefaultOutputSubdir);
        if (runFrozenProfileBottleneckAudit && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, FrozenProfileBottleneckAuditApplication.DefaultOutputSubdir);
        if (runCrossSymbolCandidateEngineV2 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, CrossSymbolCandidateEngineV2Catalog.DefaultOutputSubdir);
        if (runBnb15LookbackStarvationStudy && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, Bnb15LookbackStarvationStudyApplication.DefaultOutputSubdir);
        if (runCurrentOpportunityScannerV1 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, CurrentOpportunityScannerV1Catalog.DefaultOutputSubdir);
        if (runEntryNearMissAuditV1 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, EntryNearMissAuditV1Catalog.DefaultOutputSubdir);
        if (runCurrentOpportunityWatchV1 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, CurrentOpportunityWatchV1Catalog.DefaultOutputSubdir);
        if (runSol30mNearMissConversionHistoryStudy && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, Sol30mNearMissConversionHistoryStudyCatalog.DefaultOutputSubdir);
        if (runCrossCandidateExactEntryFrequencyStudyV1 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, CrossCandidateExactEntryFrequencyStudyV1Catalog.DefaultOutputSubdir);
        if (runCrossSymbolExactEntryReconciliationAuditV1 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, CrossSymbolExactEntryReconciliationAuditV1Catalog.DefaultOutputSubdir);
        if (runFixedFrequencySol30ForwardIncubationV1 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, "fixed-frequency-sol30-forward-incubation-v1");
        if (runFixedFrequencyEth15ForwardIncubationV1 && !args.Any(a => string.Equals(a, "--output-dir", StringComparison.OrdinalIgnoreCase)))
            outputDir = Path.Combine(defaultOutputRoot, "fixed-frequency-eth15-forward-incubation-v1");
        robustnessWindows ??= runDirectionalRuleFuturesValidationV31
            ? [30, 60, 90, 120, 180, 270, 365]
            : runDirectionalRuleFuturesValidationV3
                ? runDirectionalRuleFuturesValidationV3Focused ? [30, 60, 90] : [30, 60, 90, 120, 180]
                : [30, 60, 90];

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(outputDir);

        var baseFee = 0.1m;
        var baseSpread = 0.05m;
        if (File.Exists(appSettingsPath))
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath, optional: false)
                .Build();
            baseFee = Math.Max(0m, config.GetValue<decimal?>("Trading:FeeRatePercent") ?? baseFee);
            baseSpread = Math.Max(0m, config.GetValue<decimal?>("Trading:EstimatedSpreadPercent") ?? baseSpread);
        }

        return new BacktestSettings(
            appSettingsPath,
            dataDir,
            outputDir,
            intervals,
            bootstrap,
            bootstrapLimit,
            bootstrapDays,
            bootstrapStartUtc,
            bootstrapEndUtc,
            Math.Max(0m, feeRatePercent ?? baseFee),
            Math.Max(0m, spreadPercent ?? baseSpread),
            Math.Max(0m, slippagePercent),
            forceCloseAtEnd,
            runRobustness,
            runReachabilityResearch,
            runBroadReachabilityScan,
            runRangeExpansionResearch,
            runRangeExpansionFast,
            runRangeExpansionV2Fast,
            runRangeExpansionV2,
            runRangeExpansionV21Fast,
            runRangeExpansionV22,
            runRangeExpansionV23,
            runRangeExpansionV24,
            runRangeExpansionV2Feasibility,
            runRangeExpansionV2FeasibilityIncludeComparison,
            runRangeExpansionV2IncludeComparison,
            runRangeExpansionTargetFloorExperiment,
            runRangeExpansionFastIncludeComparison,
            runImpulseContinuationV1,
            runImpulseContinuationV1IncludeResearchVariants,
            runMeanReversionRangeBounceV1,
            runMeanReversionRangeBounceV1IncludeResearchVariants,
            runHigherTimeframeMomentumPullbackV1,
            runHigherTimeframeMomentumPullbackV1IncludeResearchVariants,
            runMarketRegimeForwardEdgeStudy,
            runRegimeGatedLongEdgeV1,
            runRegimeGatedLongEdgeV1IncludeResearchVariants,
            runRegimeGatedLongEdgeV1BtcContext,
            runMarketRegimeForwardEdgeStudyWithBtcContext,
            runLongShortFuturesFeasibilityStudyV1,
            runDirectionalRuleFuturesSimulationV1,
            runDirectionalRuleFuturesValidationV2,
            runDirectionalRuleFuturesValidationV3,
            runDirectionalRuleFuturesValidationV3Focused,
            runDirectionalRuleFuturesValidationV31,
            runDirectionalRuleFuturesRegimeDriftV1,
            runDirectionalRuleFuturesRegimeConditionalV2,
            runFuturesDirectionalRuleDiscoveryV2,
            runFuturesMarketDataExpansionV1,
            bootstrapFuturesData,
            runNoPaidDataAdaptiveActivationV1,
            runNoPaidShortWindowFlowResearchV1,
            runNoPaidShortWindowForwardIncubationV1,
            runNoPaidShortWindowMultiSymbolResearchV2,
            runNoPaidShortWindowV1CrossSymbol,
            runNoPaidShortWindowSolForwardIncubationV1,
            runNoPaidShortWindowBnb15mForwardIncubationV1,
            runNoPaidShortWindowSol15mForwardIncubationV1,
            runFixedFrequencySol30ForwardIncubationV1,
            runFixedFrequencyEth15ForwardIncubationV1,
            runFuturesTestnetShadowRunner,
            runFrozenProfileBottleneckAudit,
            runCrossSymbolCandidateEngineV2,
            runBnb15LookbackStarvationStudy,
            runCurrentOpportunityScannerV1,
            runEntryNearMissAuditV1,
            runCurrentOpportunityWatchV1,
            runSol30mNearMissConversionHistoryStudy,
            runCrossCandidateExactEntryFrequencyStudyV1,
            runCrossSymbolExactEntryReconciliationAuditV1,
            watchLoop,
            watchIntervalMinutes,
            crossSymbolV1InputDirectory,
            frequencyStudyInputDirectory,
            opportunityScannerInputDirectory,
            bottleneckAuditDirectory,
            shadowRunnerDirectory,
            directionalRuleDiscoveryJsonPath,
            runMetaStrategyResearch,
            metaInputDirectories ?? [],
            metaIncludeBlockedCandidates,
            metaBlockedCandidateCap,
            robustnessWindows,
            robustnessWindowStartUtc,
            robustnessWindowEndUtc,
            showHelp);
    }

    private static IReadOnlyList<int> ParseRobustnessWindows(string value)
    {
        var parsed = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => ParseInt(x, "--robustness-windows"))
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        if (parsed.Length == 0)
            throw new ArgumentException("At least one robustness window day value must be provided.");
        return parsed;
    }

    private static void ValidateRobustnessWindow(DateTime? startUtc, DateTime? endUtc)
    {
        if (startUtc.HasValue ^ endUtc.HasValue)
            throw new ArgumentException("Both --robustness-window-start and --robustness-window-end must be provided together.");

        if (startUtc.HasValue && endUtc.HasValue && endUtc.Value <= startUtc.Value)
            throw new ArgumentException("robustness-window-end must be greater than robustness-window-start.");
    }

    private static bool ParseBool(string value, string argName)
    {
        if (bool.TryParse(value, out var parsed))
            return parsed;
        throw new ArgumentException($"Invalid bool for {argName}: '{value}'.");
    }

    private static int ParseInt(string value, string argName)
    {
        if (int.TryParse(value, out var parsed))
            return parsed;
        throw new ArgumentException($"Invalid integer for {argName}: '{value}'.");
    }

    private static decimal ParseDecimal(string value, string argName)
    {
        if (decimal.TryParse(value, out var parsed))
            return parsed;
        throw new ArgumentException($"Invalid decimal for {argName}: '{value}'.");
    }

    private static IReadOnlyList<string> ParseIntervals(string value)
    {
        var parsed = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeInterval)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parsed.Length == 0)
            throw new ArgumentException("At least one interval must be provided.");
        return parsed;
    }

    private static IReadOnlyList<string> ParseCsvPaths(string value)
    {
        var parsed = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parsed.Length == 0)
            throw new ArgumentException("At least one meta input directory must be provided.");
        return parsed;
    }

    private static string NormalizeInterval(string raw)
    {
        var value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "1m" => "1m",
            "3m" => "3m",
            "5m" => "5m",
            "15m" => "15m",
            "30m" => "30m",
            _ => throw new ArgumentException($"Unsupported interval '{raw}'. Allowed: 1m, 3m, 5m, 15m, 30m.")
        };
    }

    private static int ParseBootstrapDays(string value)
    {
        if (!int.TryParse(value, out var parsed))
            throw new ArgumentException($"Invalid bootstrap days value '{value}'.");
        if (parsed is not (7 or 14 or 30 or 60 or 90 or 120 or 180 or 270 or 365))
            throw new ArgumentException("bootstrap-days must be one of: 7, 14, 30, 60, 90, 120, 180, 270, 365.");
        return parsed;
    }

    private static DateTime ParseUtcDateTime(string value, string argName)
    {
        if (!DateTimeOffset.TryParse(value, out var dto))
            throw new ArgumentException($"Invalid UTC datetime for {argName}: '{value}'.");
        return dto.UtcDateTime;
    }

    private static void ValidateBootstrapWindow(int? bootstrapDays, DateTime? bootstrapStartUtc, DateTime? bootstrapEndUtc)
    {
        if (bootstrapDays.HasValue && (bootstrapStartUtc.HasValue || bootstrapEndUtc.HasValue))
            throw new ArgumentException("Use either --bootstrap-days OR --bootstrap-start/--bootstrap-end, not both.");

        if (bootstrapStartUtc.HasValue ^ bootstrapEndUtc.HasValue)
            throw new ArgumentException("Both --bootstrap-start and --bootstrap-end must be provided together.");

        if (bootstrapStartUtc.HasValue && bootstrapEndUtc.HasValue && bootstrapEndUtc.Value <= bootstrapStartUtc.Value)
            throw new ArgumentException("bootstrap-end must be greater than bootstrap-start.");
    }
}
