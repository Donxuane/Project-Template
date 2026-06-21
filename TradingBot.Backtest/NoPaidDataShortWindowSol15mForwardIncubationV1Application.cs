using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// SOLUSDT 15m cross-symbol freeze proposal forward incubation. Backtest/research only.
/// </summary>
public sealed class NoPaidDataShortWindowSol15mForwardIncubationV1Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly CrossSymbolForwardIncubationRunner.CatalogRefs Catalog =
        new(
            ModeName: "no-paid-short-window-sol-15m-forward-incubation-v1",
            TrackLabel: "SOL15m",
            FrozenProfileName: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenProfileName,
            Symbol: TradingSymbol.SOLUSDT,
            Interval: "15m",
            FrozenComboKey: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenComboKey,
            BuildFrozenActivationConfig: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.BuildFrozenActivationConfig,
            BuildDefaultState: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.BuildDefaultState,
            FrozenStatePath: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenStatePath,
            ForwardHistoryPath: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.ForwardHistoryPath,
            CostScenarios: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.CostScenarios,
            PrimaryCostScenario: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.PrimaryCostScenario,
            ModerateSlippageScenario: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.ModerateSlippageScenario,
            StressPlusScenario: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.StressPlusScenario,
            CoverageSymbols: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.CoverageSymbols,
            CoverageSourceKeys: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.CoverageSourceKeys,
            ResolveVerdict: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.ResolveVerdict,
            MinForwardTrades: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.MinForwardTrades,
            StressPlusCollapseFloorQuote: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.StressPlusCollapseFloorQuote,
            MaxConsecutiveLossesLimit: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.MaxConsecutiveLossesLimit,
            MaxSingleDayProfitShare: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.MaxSingleDayProfitShare,
            MinPositivePeriodRate: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.MinPositivePeriodRate,
            MaxDrawdownToNetRatio: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.MaxDrawdownToNetRatio,
            MinForwardSpanDaysForJudgment: NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.MinForwardSpanDaysForJudgment,
            AnswersSymbolLabel: "SOL 15m",
            AnswersBesideTracksNote: "Fourth incubation track beside frozen BNB 5m, SOL 5m, and BNB 15m; existing tracks hash-protected.");

    public async Task<NoPaidDataShortWindowSol15mForwardIncubationV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var core = await CrossSymbolForwardIncubationRunner.RunAsync(
            settings, Catalog,
            includeBnb5mProtection: true,
            includeSol5mProtection: true,
            includeBnb15mProtection: true,
            includeSol15mProtection: false,
            cancellationToken);

        var result = MapResult(core);
        Directory.CreateDirectory(settings.OutputDirectory);
        await new CrossSymbolForwardIncubationReportWriter(
            settings.OutputDirectory,
            "frozen-sol-15m-candidate-summary",
            "sol-15m-forward-incubation",
            "sol-15m-forward-history").WriteAsync(core, cancellationToken);
        await WriteRunMetadataAsync(core, result, cancellationToken);
        return result;
    }

    private static NoPaidDataShortWindowSol15mForwardIncubationV1RunResult MapResult(CrossSymbolForwardIncubationRunResult core)
        => new(
            core.FrozenSummary,
            core.DataCoverage,
            core.ForwardTrades,
            core.ForwardPeriods,
            core.CostSensitivity,
            core.HealthGates,
            core.History,
            core.Answers,
            core.NoTradeReasonSummary,
            core.Verdict,
            core.FrozenStartUtc,
            core.ForwardWindowEndUtc,
            core.ForwardSpanDays,
            core.ProtectedFrozenFilesByteIdentical,
            core.ProtectedFilesChecked);

    private async Task WriteRunMetadataAsync(
        CrossSymbolForwardIncubationRunResult core,
        NoPaidDataShortWindowSol15mForwardIncubationV1RunResult result,
        CancellationToken cancellationToken)
    {
        var s = core.NoTradeReasonSummary;
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = Catalog.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                frozenProfile = core.FrozenProfileName,
                frozenStatePath = core.FrozenStatePath,
                forwardHistoryPath = core.ForwardHistoryPath,
                forwardHistoryRunCount = core.History.Count,
                frozenStartUtc = core.FrozenStartUtc,
                forwardWindowEndUtc = core.ForwardWindowEndUtc,
                forwardSpanDays = core.ForwardSpanDays,
                forwardTrades = result.FrozenSummary.ForwardTrades,
                forwardNetModerate = result.FrozenSummary.ForwardNetModerate,
                forwardNetLatency002 = s.NetModerateLatency002,
                forwardNetStressPlus = s.NetStressPlus,
                healthGatesPassed = s.HealthGatesPassed,
                healthGatesTotal = s.HealthGatesTotal,
                verdict = core.Verdict,
                reportStatus = s.ReportStatus,
                latestRunStatus = s.LatestRunStatus,
                dataAdvancedSincePreviousRun = s.DataAdvancedSincePreviousRun,
                newTradesSincePreviousRun = s.NewTradesSincePreviousRun,
                newNetModerateSincePreviousRun = s.NewNetModerateSincePreviousRun,
                newNetStressPlusSincePreviousRun = s.NewNetStressPlusSincePreviousRun,
                compactSummaryLine = s.CompactSummaryLine,
                protectedFrozenHashStatus = s.FrozenHashStatus,
                bnbFrozenHashStatus = s.BnbFrozenHashStatus,
                solFrozenHashStatus = s.SolFrozenHashStatus,
                nextAction = s.NextAction,
                protectedFrozenFilesByteIdentical = core.ProtectedFrozenFilesByteIdentical,
                protectedFilesChecked = core.ProtectedFilesChecked,
                acceleratedValidation = new
                {
                    warning = ForwardIncubationAcceleratedValidationSummary.DiagnosticWarning,
                    core.AcceleratedValidation.CompactSummaryLine,
                    core.AcceleratedValidation.TrueForwardNet,
                    core.AcceleratedValidation.PreFreezeReplayNet3d,
                    core.AcceleratedValidation.PreFreezeReplayNet7d,
                    core.AcceleratedValidation.PreFreezeReplayNet14d,
                    core.AcceleratedValidation.MissedWinnersCount,
                    core.AcceleratedValidation.BlockedLosersCount,
                    core.AcceleratedValidation.MainFinding,
                    diagnosticOnly = true,
                    affectsForwardVerdict = false
                },
                bootstrapAttempted = core.BootstrapAttempted,
                downloadOutcomes = core.DownloadOutcomes.Select(o => new { o.Symbol, o.SourceKey, o.Success, o.AddedCount, o.TotalCount, o.Message }),
                fourthIncubationTrackBesideExisting = true,
                noNewOptimization = true,
                frozenRuleOnly = true,
                forwardOnlyJudgment = true,
                backtestOnly = true,
                liveFuturesRecommended = false,
                paidDataUsed = false,
                realOrdersPlaced = false
            }, JsonOptions),
            cancellationToken);
    }
}
