using System.Text.Json;
using Microsoft.Extensions.Configuration;

using TradingBot.Backtest;



var settings = BacktestCli.Parse(args);

if (settings.ShowHelp)

{

    Console.WriteLine(BacktestCli.HelpText);

    return 0;

}



var startedAtUtc = DateTime.UtcNow;

Directory.CreateDirectory(settings.OutputDirectory);



if (settings.RunLongShortFuturesFeasibilityStudyV1)
{
    var longShortApp = new LongShortFuturesFeasibilityStudyV1Application(settings);
    var longShortResult = await longShortApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"LongShortFuturesFeasibilityStudyV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Symbols: {string.Join(", ", longShortResult.SymbolsScanned)}, Intervals: {string.Join(", ", longShortResult.IntervalsScanned)}");
    Console.WriteLine($"Observations: {longShortResult.Observations.Count}, Entry-time rules: {longShortResult.EntryTimeRules.Count}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "long-short-feasibility-summary.json")}");
    Console.WriteLine($"Symbol/interval ranking: {Path.Combine(settings.OutputDirectory, "long-short-symbol-interval-ranking.json")}");
    Console.WriteLine($"Regime ranking: {Path.Combine(settings.OutputDirectory, "long-short-regime-ranking.json")}");
    Console.WriteLine($"Target/stop matrix: {Path.Combine(settings.OutputDirectory, "long-short-target-stop-matrix.json")}");
    Console.WriteLine($"Cost sensitivity: {Path.Combine(settings.OutputDirectory, "long-short-cost-sensitivity.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "long-short-research-answers.json")}");
    return 0;
}

if (settings.RunNoPaidDataAdaptiveActivationV1)
{
    var adaptiveApp = new NoPaidDataAdaptiveActivationV1Application(settings);
    var adaptiveResult = await adaptiveApp.RunAsync(CancellationToken.None);
    var baseline = adaptiveResult.Summary.FirstOrDefault(s => s.ConditionType == nameof(AdaptiveActivationConditionType.AlwaysOn));
    var qualifying = adaptiveResult.Summary.Count(s => s.PassesSuccessCriteria);
    var best = adaptiveResult.Summary
        .Where(s => s.ConditionType != nameof(AdaptiveActivationConditionType.AlwaysOn))
        .OrderByDescending(s => s.Full365NetPnl)
        .FirstOrDefault();

    Console.WriteLine($"NoPaidDataAdaptiveActivationV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Baseline Rule01 short: {baseline?.TotalTrades ?? 0} trades, full365={baseline?.Full365NetPnl:F2}, recent90d={baseline?.Recent90dNetPnl:F2}.");
    Console.WriteLine($"Activation rules evaluated: {adaptiveResult.Summary.Count}. Qualifying: {qualifying}.");
    if (best is not null)
        Console.WriteLine($"Best adaptive (by full365): {best.ActivationRuleName} full365={best.Full365NetPnl:F2} (delta {best.Full365Delta:F2}), trades={best.TotalTrades}.");
    Console.WriteLine(qualifying == 0
        ? "Walk-forward activation did not salvage Rule01 short. Park candidate."
        : "Some rules passed criteria — review reports before any paper/sandbox step.");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "adaptive-activation-research-answers.json")}");
    return 0;
}

if (settings.RunNoPaidShortWindowFlowResearchV1)
{
    var shortWindowApp = new NoPaidDataShortWindowFlowResearchV1Application(settings);
    var shortWindowResult = await shortWindowApp.RunAsync(CancellationToken.None);
    var swQualifying = shortWindowResult.Summary.Count(s => s.PassesSuccessCriteria);
    var swBest = shortWindowResult.Summary
        .Where(s => s.PerfCondition != nameof(ShortWindowPerfCondition.AlwaysOn))
        .OrderByDescending(s => s.NetPnlQuote)
        .FirstOrDefault();

    Console.WriteLine($"NoPaidDataShortWindowFlowResearchV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Study window: {shortWindowResult.StudyStartUtc:yyyy-MM-dd} -> {shortWindowResult.StudyEndUtc:yyyy-MM-dd} (flow coverage from {shortWindowResult.FlowCoverageStartUtc:yyyy-MM-dd}).");
    Console.WriteLine($"Baseline Rule01 short in study window: {shortWindowResult.BaselineTradeCount} trades, net={shortWindowResult.BaselineNetPnl:F2} (futures-moderate).");
    Console.WriteLine($"Activation configs evaluated: {shortWindowResult.Summary.Count}. Qualifying (all success criteria): {swQualifying}.");
    if (swBest is not null)
        Console.WriteLine($"Best activation rule by net: {swBest.ActivationRuleName} net={swBest.NetPnlQuote:F2} (delta {swBest.Delta:F2}), trades={swBest.TotalTrades}, verdict={swBest.Verdict}.");
    Console.WriteLine(swQualifying == 0
        ? "No rule met all short-window success criteria. Keep collecting free flow data; no paper/sandbox/live action."
        : "Some rules met criteria on this single short window — research only; re-validate after more free data is collected. No live recommendation.");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "no-paid-short-window-research-answers.json")}");
    return 0;
}

if (settings.RunFuturesTestnetShadowRunner)
{
    var shadowApp = new FuturesTestnetShadowApplication(settings);
    var shadowResult = await shadowApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"FuturesTestnetShadowRunner completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(shadowResult.Summary.CompactSummaryLine);
    Console.WriteLine($"Key safety: {shadowResult.KeySafetyStatus}");
    Console.WriteLine($"Profiles: {shadowResult.Summary.ProfilesEvaluated}, activation passed: {shadowResult.Summary.ActivationPassedCount}, entry signals: {shadowResult.Summary.EntrySignalCount}, would-place: {shadowResult.Summary.WouldPlaceOrderCount}");
    Console.WriteLine("backtestOnly=true, testnetShadowOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "futures-testnet-shadow-summary.json")}");
    Console.WriteLine($"Decisions: {Path.Combine(settings.OutputDirectory, "futures-testnet-shadow-decisions.json")}");
    Console.WriteLine($"Risk: {Path.Combine(settings.OutputDirectory, "futures-testnet-shadow-risk.json")}");
    return 0;
}

if (settings.RunFrozenProfileBottleneckAudit)
{
    var auditApp = new FrozenProfileBottleneckAuditApplication(settings);
    var auditResult = await auditApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"FrozenProfileBottleneckAudit completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(auditResult.CompactSummaryLine);
    Console.WriteLine("diagnosticOnly=true, strategyLogicChanged=false, healthGatesAffected=false, verdictsAffected=false");
    Console.WriteLine($"Audit: {Path.Combine(settings.OutputDirectory, "frozen-profile-bottleneck-audit.json")}");
    foreach (var profile in auditResult.Profiles)
    {
        Console.WriteLine($"  {profile.ProfileName}: {profile.BottleneckClassification} -> {profile.Recommendation} (trades={profile.ForwardTrades}, net={profile.NetModerate:F2}, stress={profile.NetStressPlus:F2})");
    }
    return 0;
}

if (settings.RunCurrentOpportunityScannerV1)
{
    var scannerApp = new CurrentOpportunityScannerV1Application(settings);
    var scannerResult = await scannerApp.RunAsync(settings.CrossSymbolV1InputDirectory, v2InputDirectory: null, CancellationToken.None);

    Console.WriteLine($"CurrentOpportunityScannerV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(scannerResult.Summary.CompactSummaryLine);
    Console.WriteLine(scannerResult.Summary.ActionableShadowCount == 0
        ? "No current opportunity"
        : "Shadow opportunity exists");
    Console.WriteLine("diagnosticOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false, wouldPlaceOrder=false");
    Console.WriteLine(
        $"Evaluated={scannerResult.Summary.EvaluatedCandidateCount} activationPassed={scannerResult.Summary.ActivationPassedCount} entryPresent={scannerResult.Summary.BaseEntrySignalPresentCount} actionable={scannerResult.Summary.ActionableShadowCount}");
    if (scannerResult.Summary.TopBlockers.Count > 0)
        Console.WriteLine($"Top blockers: {string.Join("; ", scannerResult.Summary.TopBlockers)}");
    foreach (var c in scannerResult.Summary.TopActionableCandidates.Take(5))
        Console.WriteLine($"  ACTIONABLE {c.Symbol} {c.Interval} {c.Direction} key={c.CandidateKey} score={c.ResearchScore:F4}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "current-opportunity-scanner-v1-summary.json")}");
    return 0;
}

if (settings.RunEntryNearMissAuditV1)
{
    var auditApp = new EntryNearMissAuditV1Application(settings);
    var auditResult = await auditApp.RunAsync(
        settings.OpportunityScannerInputDirectory,
        settings.CrossSymbolV1InputDirectory,
        v2InputDirectory: null,
        CancellationToken.None);

    Console.WriteLine($"EntryNearMissAuditV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(auditResult.Summary.CompactSummaryLine);
    Console.WriteLine($"EntryRarityVerdict: {auditResult.Summary.EntryRarityVerdict}");
    Console.WriteLine("diagnosticOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false, nearMissNotForwardProof=true");
    Console.WriteLine(
        $"ActivationPassed={auditResult.Summary.EvaluatedActivationPassedCount} topNearMiss={auditResult.Summary.TopNearMissCount} farFromEntry={auditResult.Summary.FarFromEntryCount}");
    if (!string.IsNullOrEmpty(auditResult.Summary.TopNearMissCandidate))
        Console.WriteLine($"TopNearMiss: {auditResult.Summary.TopNearMissCandidate} ({auditResult.Summary.TopNearMissReason})");
    Console.WriteLine(auditResult.Summary.PlainEnglish.ShouldWaitForActualEntrySignal);
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "entry-near-miss-audit-v1-summary.json")}");
    return 0;
}

if (settings.RunCurrentOpportunityWatchV1)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var watchApp = new CurrentOpportunityWatchV1Application(settings);
    var watchResult = await watchApp.RunAsync(cts.Token);

    Console.WriteLine($"CurrentOpportunityWatchV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(watchResult.Status.CompactSummaryLine);
    Console.WriteLine($"WatchStatus: {watchResult.Status.WatchStatus}");
    Console.WriteLine("diagnosticOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false, wouldPlaceOrder=false, usesConfirmedClosedCandlesOnly=true");
    Console.WriteLine(watchResult.Status.PlainEnglish.ShouldWeTrade);
    if (!string.IsNullOrEmpty(watchResult.Status.ExactEntryAppearedNote))
        Console.WriteLine(watchResult.Status.ExactEntryAppearedNote);
    Console.WriteLine($"Status: {Path.Combine(settings.OutputDirectory, "current-opportunity-watch-v1-status.json")}");
    Console.WriteLine($"History: {Path.Combine(settings.OutputDirectory, "current-opportunity-watch-v1-history.json")}");
    return 0;
}

if (settings.RunBnb15LookbackStarvationStudy)
{
    var studyApp = new Bnb15LookbackStarvationStudyApplication(settings);
    var studyResult = await studyApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"Bnb15LookbackStarvationStudy completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(studyResult.Summary.CompactSummaryLine);
    Console.WriteLine("diagnosticOnly=true, frozenProfileUnchanged=true, realOrdersPlaced=false, liveFuturesRecommended=false");
    Console.WriteLine($"PrimaryRootCause: {studyResult.Summary.PrimaryRootCause}");
    Console.WriteLine($"OverallRecommendation: {studyResult.Summary.PlainEnglish.OverallStudyRecommendation}");
    Console.WriteLine(studyResult.Summary.PlainEnglish.ShouldCurrentBnb15StayParked);
    Console.WriteLine($"Study: {Path.Combine(settings.OutputDirectory, "bnb15-lookback-starvation-study.json")}");
    return 0;
}

if (settings.RunSol30mNearMissConversionHistoryStudy)
{
    var studyApp = new Sol30mNearMissConversionHistoryStudyApplication(settings);
    var studyResult = await studyApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"Sol30mNearMissConversionHistoryStudy completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(studyResult.Summary.CompactSummaryLine);
    Console.WriteLine("diagnosticOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false, nearMissConversionIsNotForwardProof=true");
    Console.WriteLine($"Recommendation: {studyResult.Summary.Recommendation}");
    Console.WriteLine(studyResult.Summary.PlainEnglish.ShouldTradeNearMissBeforeExactEntry);
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "sol30m-near-miss-conversion-history-summary.txt")}");
    return 0;
}

if (settings.RunCrossCandidateExactEntryFrequencyStudyV1)
{
    var studyApp = new CrossCandidateExactEntryFrequencyStudyV1Application(settings);
    var studyResult = await studyApp.RunAsync(
        settings.CrossSymbolV1InputDirectory,
        v2InputDirectory: null,
        settings.OpportunityScannerInputDirectory,
        CancellationToken.None);

    Console.WriteLine($"CrossCandidateExactEntryFrequencyStudyV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(studyResult.Summary.CompactSummaryLine);
    Console.WriteLine("diagnosticOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false, nearMissNotUsed=true");
    Console.WriteLine($"PromoteToExactEntryWatcher: {studyResult.Summary.PromoteToExactEntryWatcherCount}");
    Console.WriteLine(studyResult.Summary.PlainEnglish.WorthMovingTowardTestnetOrderPreparation);
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "cross-candidate-exact-entry-frequency-v1-summary.txt")}");
    return 0;
}

if (settings.RunCrossSymbolExactEntryReconciliationAuditV1)
{
    var auditApp = new CrossSymbolExactEntryReconciliationAuditV1Application(settings);
    var auditResult = await auditApp.RunAsync(
        settings.CrossSymbolV1InputDirectory,
        v2InputDirectory: null,
        settings.FrequencyStudyInputDirectory,
        CancellationToken.None);

    Console.WriteLine($"CrossSymbolExactEntryReconciliationAuditV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(auditResult.Summary.CompactSummaryLine);
    Console.WriteLine("diagnosticOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false, reconciliationOnly=true");
    Console.WriteLine($"PrimaryRootCause: {auditResult.Summary.PrimaryRootCause}");
    Console.WriteLine(auditResult.Summary.PlainEnglish.IsZeroExactEntryResultValid);
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "cross-symbol-exact-entry-reconciliation-v1-summary.txt")}");
    return 0;
}

if (settings.RunCrossSymbolCandidateEngineV2)
{
    var engineSettings = CrossSymbolCandidateEngineV2Application.ResolveDefaultSettings(settings.DataDirectory, settings.OutputDirectory);
    if (!string.IsNullOrWhiteSpace(settings.CrossSymbolV1InputDirectory))
        engineSettings = engineSettings with { V1InputDirectory = settings.CrossSymbolV1InputDirectory };
    if (!string.IsNullOrWhiteSpace(settings.BottleneckAuditDirectory))
        engineSettings = engineSettings with { BottleneckAuditDirectory = settings.BottleneckAuditDirectory };
    if (!string.IsNullOrWhiteSpace(settings.ShadowRunnerDirectory))
        engineSettings = engineSettings with { ShadowRunnerDirectory = settings.ShadowRunnerDirectory };

    var engineApp = new CrossSymbolCandidateEngineV2Application(settings);
    var engineResult = await engineApp.RunAsync(engineSettings, CancellationToken.None);

    Console.WriteLine($"CrossSymbolCandidateEngineV2 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(engineResult.Summary.CompactSummaryLine);
    Console.WriteLine("backtestOnly=true, shadowDryRunOnly=true, realOrdersPlaced=false, liveFuturesRecommended=false");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "cross-symbol-candidate-engine-v2-summary.json")}");
    Console.WriteLine($"Research promoted={engineResult.Summary.ResearchPromotedCount}, execution-ready portfolio={engineResult.Summary.ExecutionReadyPortfolioCandidateCount}, canEnterTestnet={engineResult.Summary.CanEnterTestnetOrderModeCount}, blockedByLookback={engineResult.Summary.BlockedByLookbackStarvationCount}");
    foreach (var c in engineResult.Candidates.Where(c => c.ResearchPromotionStatus == "PromoteToShadow"))
    {
        Console.WriteLine($"  RESEARCH {c.Symbol} {c.Interval} {c.Direction}: readiness={c.CurrentExecutionReadiness}, canEnterTestnet={c.CanEnterTestnetOrderMode}, forwardTrades={c.CurrentForwardTrades}, bottleneck={c.CurrentBottleneckClassification}/{c.CurrentBottleneckRecommendation}");
    }
    return 0;
}

if (settings.RunNoPaidShortWindowForwardIncubationV1)
{
    var incubationApp = new NoPaidDataShortWindowForwardIncubationV1Application(settings);
    var incubationResult = await incubationApp.RunAsync(CancellationToken.None);
    var gatesPassed = incubationResult.HealthGates.Count(g => g.Pass);

    Console.WriteLine($"NoPaidDataShortWindowForwardIncubationV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(incubationResult.NoTradeReasonSummary.CompactSummaryLine);
    Console.WriteLine($"Latest run status: {incubationResult.NoTradeReasonSummary.LatestRunStatus} | New trades this run: {incubationResult.NoTradeReasonSummary.NewTradesSincePreviousRun} | Data advanced: {incubationResult.NoTradeReasonSummary.DataAdvancedSincePreviousRun}");
    Console.WriteLine($"Frozen profile: {incubationResult.FrozenSummary.ProfileName} (frozen start {incubationResult.FrozenStartUtc:yyyy-MM-dd HH:mm}Z, no reselection/retuning).");
    Console.WriteLine($"Forward window: {incubationResult.FrozenStartUtc:yyyy-MM-dd HH:mm}Z -> {incubationResult.ForwardWindowEndUtc:yyyy-MM-dd HH:mm}Z ({incubationResult.ForwardSpanDays:F2} days).");
    Console.WriteLine($"Forward trades: {incubationResult.FrozenSummary.ForwardTrades}, net (futures-moderate): {incubationResult.FrozenSummary.ForwardNetModerate:F2}.");
    Console.WriteLine($"Health gates passed: {gatesPassed}/{incubationResult.HealthGates.Count}. Verdict: {incubationResult.Verdict}.");
    Console.WriteLine($"Forward-history runs recorded: {incubationResult.History.Count}.");
    Console.WriteLine("Backtest/research only. No live recommendation; no real orders; no paid data; no optimization performed.");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "forward-incubation-research-answers.json")}");
    return 0;
}

if (settings.RunNoPaidShortWindowMultiSymbolResearchV2)
{
    var multiApp = new NoPaidDataShortWindowMultiSymbolResearchV2Application(settings);
    var multiResult = await multiApp.RunAsync(CancellationToken.None);
    var freezeProposals = multiResult.Leaderboard.Count(r => r.Recommendation == "FreezeForForwardIncubation");
    var watchlist = multiResult.Leaderboard.Count(r => r.Recommendation == "Watchlist");
    var failed = multiResult.Leaderboard.Count(r => r.Recommendation == "CandidateFailed");

    Console.WriteLine($"NoPaidDataShortWindowMultiSymbolResearchV2 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Study window: {multiResult.StudyStartUtc:yyyy-MM-dd HH:mm}Z -> {multiResult.StudyEndUtc:yyyy-MM-dd HH:mm}Z.");
    Console.WriteLine($"Base combos scanned: {multiResult.BaseRuleSummary.Count}. Discovery-selected candidates on leaderboard: {multiResult.Leaderboard.Count}.");
    Console.WriteLine($"Recommendations: FreezeForForwardIncubation={freezeProposals}, Watchlist={watchlist}, CandidateFailed={failed}, other={multiResult.Leaderboard.Count - freezeProposals - watchlist - failed}.");
    foreach (var row in multiResult.Leaderboard.Take(5))
        Console.WriteLine($"  {row.Symbol} {row.Interval} {row.Direction} {row.RuleFamily} + {row.ActivationRule}: disc={row.DiscoveryNet:F2} val={row.ValidationNet:F2} hold={row.HoldoutNet:F2} -> {row.Recommendation}");
    Console.WriteLine($"Watchlist candidates: {multiResult.Watchlist.Count}" + (multiResult.Watchlist.Count > 0
        ? $" ({string.Join("; ", multiResult.Watchlist.Select(w => $"{w.Symbol} {w.Interval} {w.Direction} {w.RuleFamily} -> {w.Recommendation}, missing {w.MissingTradeCount} trades"))})"
        : string.Empty));
    Console.WriteLine("Frozen BNB incubation track untouched. Research only — no live trading recommendation from this run.");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "multisymbol-research-answers.json")}");
    return 0;
}

if (settings.RunNoPaidShortWindowSolForwardIncubationV1)
{
    var solIncApp = new NoPaidDataShortWindowSolForwardIncubationV1Application(settings);
    var solIncResult = await solIncApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"NoPaidDataShortWindowSolForwardIncubationV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(solIncResult.NoTradeReasonSummary.CompactSummaryLine);
    Console.WriteLine($"Latest run status: {solIncResult.NoTradeReasonSummary.LatestRunStatus} | New trades this run: {solIncResult.NoTradeReasonSummary.NewTradesSincePreviousRun} | Data advanced: {solIncResult.NoTradeReasonSummary.DataAdvancedSincePreviousRun}");
    Console.WriteLine($"BNB frozen hash: {solIncResult.NoTradeReasonSummary.BnbFrozenHashStatus} | SOL frozen hash: {solIncResult.NoTradeReasonSummary.SolFrozenHashStatus}");
    Console.WriteLine($"Frozen profile: {solIncResult.FrozenSummary.ProfileName}");
    Console.WriteLine($"Frozen start: {solIncResult.FrozenStartUtc:yyyy-MM-dd HH:mm}Z; forward window end: {solIncResult.ForwardWindowEndUtc:yyyy-MM-dd HH:mm}Z ({solIncResult.ForwardSpanDays:F2} day(s)).");
    Console.WriteLine($"Forward trades: {solIncResult.ForwardTrades.Count}; health gates passed: {solIncResult.HealthGates.Count(g => g.Pass)}/{solIncResult.HealthGates.Count}.");
    Console.WriteLine($"Verdict: {solIncResult.Verdict}. Forward history entries: {solIncResult.History.Count}.");
    Console.WriteLine($"BNB frozen files byte-identical before/after run: {solIncResult.BnbFrozenFilesByteIdentical} ({solIncResult.BnbFilesChecked.Count} file(s) hash-checked).");
    Console.WriteLine("Second incubation track beside BNB — research only; no live trading recommendation from this run.");
    return 0;
}

if (settings.RunNoPaidShortWindowBnb15mForwardIncubationV1)
{
    var bnb15IncApp = new NoPaidDataShortWindowBnb15mForwardIncubationV1Application(settings);
    var bnb15IncResult = await bnb15IncApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"NoPaidDataShortWindowBnb15mForwardIncubationV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(bnb15IncResult.NoTradeReasonSummary.CompactSummaryLine);
    Console.WriteLine($"Latest run status: {bnb15IncResult.NoTradeReasonSummary.LatestRunStatus} | New trades: {bnb15IncResult.NoTradeReasonSummary.NewTradesSincePreviousRun} | Data advanced: {bnb15IncResult.NoTradeReasonSummary.DataAdvancedSincePreviousRun}");
    Console.WriteLine($"Frozen profile: {bnb15IncResult.FrozenSummary.ProfileName}");
    Console.WriteLine($"Forward window: {bnb15IncResult.FrozenStartUtc:yyyy-MM-dd HH:mm}Z -> {bnb15IncResult.ForwardWindowEndUtc:yyyy-MM-dd HH:mm}Z ({bnb15IncResult.ForwardSpanDays:F2} days).");
    Console.WriteLine($"Forward trades: {bnb15IncResult.ForwardTrades.Count}; health gates: {bnb15IncResult.HealthGates.Count(g => g.Pass)}/{bnb15IncResult.HealthGates.Count}. Verdict: {bnb15IncResult.Verdict}.");
    Console.WriteLine($"Protected frozen tracks byte-identical: {bnb15IncResult.ProtectedFrozenFilesByteIdentical} ({bnb15IncResult.ProtectedFilesChecked.Count} files hash-checked).");
    Console.WriteLine("Backtest/research only. liveFuturesRecommended=false; no real orders.");
    Console.WriteLine(ForwardIncubationAcceleratedValidationSummary.DiagnosticWarning);
    return 0;
}

if (settings.RunNoPaidShortWindowSol15mForwardIncubationV1)
{
    var sol15IncApp = new NoPaidDataShortWindowSol15mForwardIncubationV1Application(settings);
    var sol15IncResult = await sol15IncApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"NoPaidDataShortWindowSol15mForwardIncubationV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine(sol15IncResult.NoTradeReasonSummary.CompactSummaryLine);
    Console.WriteLine($"Latest run status: {sol15IncResult.NoTradeReasonSummary.LatestRunStatus} | New trades: {sol15IncResult.NoTradeReasonSummary.NewTradesSincePreviousRun} | Data advanced: {sol15IncResult.NoTradeReasonSummary.DataAdvancedSincePreviousRun}");
    Console.WriteLine($"Frozen profile: {sol15IncResult.FrozenSummary.ProfileName}");
    Console.WriteLine($"Forward window: {sol15IncResult.FrozenStartUtc:yyyy-MM-dd HH:mm}Z -> {sol15IncResult.ForwardWindowEndUtc:yyyy-MM-dd HH:mm}Z ({sol15IncResult.ForwardSpanDays:F2} days).");
    Console.WriteLine($"Forward trades: {sol15IncResult.ForwardTrades.Count}; health gates: {sol15IncResult.HealthGates.Count(g => g.Pass)}/{sol15IncResult.HealthGates.Count}. Verdict: {sol15IncResult.Verdict}.");
    Console.WriteLine($"Protected frozen tracks byte-identical: {sol15IncResult.ProtectedFrozenFilesByteIdentical} ({sol15IncResult.ProtectedFilesChecked.Count} files hash-checked).");
    Console.WriteLine("Backtest/research only. liveFuturesRecommended=false; no real orders.");
    Console.WriteLine(ForwardIncubationAcceleratedValidationSummary.DiagnosticWarning);
    return 0;
}

if (settings.RunFixedFrequencySol30ForwardIncubationV1 || settings.RunFixedFrequencyEth15ForwardIncubationV1)
{
    var track = settings.RunFixedFrequencySol30ForwardIncubationV1
        ? FixedFrequencyForwardIncubationV1Catalog.Sol30()
        : FixedFrequencyForwardIncubationV1Catalog.Eth15();
    var ffApp = new FixedFrequencyForwardIncubationV1Application(settings, track);
    var ffResult = await ffApp.RunAsync(CancellationToken.None);
    var ffSummary = ffResult.Summary;

    Console.WriteLine($"FixedFrequencyForwardIncubationV1 ({track.ModeName}) completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Frozen profile: {ffSummary.ProfileName}");
    Console.WriteLine($"Frozen start (freeze timestamp): {ffSummary.FrozenStartUtc:yyyy-MM-dd HH:mm}Z");
    Console.WriteLine($"Forward window: {ffSummary.ForwardWindowStartUtc:yyyy-MM-dd HH:mm}Z -> {ffSummary.ForwardWindowEndUtc:yyyy-MM-dd HH:mm}Z ({ffSummary.ForwardSpanDays} day(s)).");
    Console.WriteLine($"Forward trades: {ffSummary.ForwardTrades} (new since previous run: {ffSummary.NewTradesSincePreviousRun}); NetModerate={ffSummary.NetModerate}, NetStressPlus={ffSummary.NetStressPlus}.");
    Console.WriteLine($"Checkpoints: activated={ffSummary.ActivatedCheckpointCount}/{ffSummary.ActivationCheckpointCount}, activatedButNoEntry={ffSummary.ActivatedButNoEntryCount}; currentExactEntryPresent={ffSummary.CurrentExactEntryPresent}.");
    Console.WriteLine($"HealthScore: {ffSummary.HealthScore}/100; FailedHealthGates: {ffSummary.FailedHealthGates}. Verdict: {ffSummary.Verdict}.");
    Console.WriteLine($"NextAction: {ffSummary.NextAction}");
    Console.WriteLine($"Protected frozen tracks byte-identical: {ffResult.ProtectedFrozenFilesByteIdentical} ({ffResult.ProtectedFilesChecked.Count} files hash-checked).");
    Console.WriteLine("Forward-incubation only. Discovery/frequency history is not forward proof. No orders; testnet/live disabled; existing frozen tracks unchanged.");
    return 0;
}

if (settings.RunNoPaidShortWindowV1CrossSymbol)
{
    var crossApp = new NoPaidDataShortWindowFlowResearchV1CrossSymbolApplication(settings);
    var crossResult = await crossApp.RunAsync(CancellationToken.None);
    var crossFreeze = crossResult.Leaderboard.Count(r => r.Recommendation == "FreezeForForwardIncubation");
    var crossWatch = crossResult.Leaderboard.Count(r => r.Recommendation == "Watchlist");
    var crossFailed = crossResult.Leaderboard.Count(r => r.Recommendation == "CandidateFailed");

    Console.WriteLine($"NoPaidDataShortWindowFlowResearchV1CrossSymbol completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Study window: {crossResult.StudyStartUtc:yyyy-MM-dd HH:mm}Z -> {crossResult.StudyEndUtc:yyyy-MM-dd HH:mm}Z.");
    Console.WriteLine($"Combos: {crossResult.Summary.Count}. Recommendations: Freeze={crossFreeze}, Watchlist={crossWatch}, Failed={crossFailed}, other={crossResult.Leaderboard.Count - crossFreeze - crossWatch - crossFailed}.");
    foreach (var row in crossResult.Leaderboard.Take(5))
        Console.WriteLine($"  {row.Symbol} {row.Interval} {row.Direction} T{row.TargetPercent:0.00}/S{row.StopPercent:0.00} + {row.ActivationRule}: net={row.NetPnl:F2}, trades={row.TradeCount} -> {row.Recommendation}{(row.OverfitWarning ? " [overfit]" : "")}");
    Console.WriteLine("Frozen BNB incubation track untouched. Research only — no live trading recommendation from this run.");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "cross-symbol-v1-research-answers.json")}");
    return 0;
}

if (settings.RunFuturesMarketDataExpansionV1)
{
    var expansionApp = new FuturesMarketDataExpansionV1Application(settings);
    var expansionResult = await expansionApp.RunAsync(CancellationToken.None);
    var fullHistory = expansionResult.Availability.Where(a => a.Supports365dStudy).Select(a => a.SourceKey).Distinct().ToArray();
    var allSplitSurvivors = expansionResult.RuleCandidates.Count(c => c.AllSplitsPositive);

    Console.WriteLine($"FuturesMarketDataExpansionV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Bootstrap attempted: {expansionResult.BootstrapAttempted}. 365d-capable sources: {(fullHistory.Length > 0 ? string.Join(", ", fullHistory) : "none")}.");
    if (expansionResult.StudyRan)
        Console.WriteLine($"Flow edge study ran. All-split survivors: {allSplitSurvivors}. {(allSplitSurvivors == 0 ? "Flow features did not yield a robust edge." : "Review survivors before any further step.")}");
    else
        Console.WriteLine($"Flow edge study skipped: {expansionResult.StudySkipReason}");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "futures-flow-research-answers.json")}");
    return 0;
}

if (settings.RunFuturesDirectionalRuleDiscoveryV2)
{
    var discoveryApp = new FuturesDirectionalRuleDiscoveryV2Application(settings);
    var discoveryResult = await discoveryApp.RunAsync(CancellationToken.None);
    var survivors = discoveryResult.Candidates.Where(c => c.AllSplitsPositive && c.FullHistoryPositive).ToArray();

    Console.WriteLine($"FuturesDirectionalRuleDiscoveryV2 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Symbols={discoveryResult.SymbolsScanned}, combos={discoveryResult.CombosScanned}, train-qualified={discoveryResult.CandidateCount}, validation-survivors={discoveryResult.ValidationSurvivorCount}, holdout-survivors={discoveryResult.HoldoutSurvivorCount}.");
    if (survivors.Length > 0)
        Console.WriteLine($"All-split survivors: {survivors.Length}. Top: {survivors[0].RuleName} ({survivors[0].RuleDescription}) full365={survivors[0].FullHistoryNet:F2}.");
    else
        Console.WriteLine("No rule survived train/validation/holdout. Recommend pausing candle-rule discovery and acquiring richer data.");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "futures-directional-v2-research-answers.json")}");
    return 0;
}

if (settings.RunDirectionalRuleFuturesRegimeConditionalV2)
{
    var conditionalApp = new DirectionalRuleFuturesRegimeConditionalV2Application(settings);
    var conditionalResult = await conditionalApp.RunAsync(CancellationToken.None);
    var baseline = conditionalResult.Summary.FirstOrDefault(s =>
        string.Equals(s.FilterName, "Baseline", StringComparison.OrdinalIgnoreCase));
    var bestFilter = conditionalResult.Summary.FirstOrDefault(s => s.PassesAllCriteria);

    Console.WriteLine($"DirectionalRuleFuturesRegimeConditionalV2 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Trades analyzed: {conditionalResult.TotalTrades}, baseline full365 net={baseline?.Full365NetPnl:F2} (older={baseline?.OlderNetPnl:F2}, recent90d={baseline?.Recent90dNetPnl:F2}).");
    if (bestFilter is not null)
        Console.WriteLine($"Qualifying activation filter: {bestFilter.FilterName} ({bestFilter.FilterDescription}) older={bestFilter.OlderNetPnl:F2}, recent90d={bestFilter.Recent90dNetPnl:F2}, full365={bestFilter.Full365NetPnl:F2}.");
    else
        Console.WriteLine("No activation filter passed split validation. Rule01 short marked recent-regime-only (park).");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "directional-rule-v32-research-answers.json")}");
    return 0;
}

if (settings.RunDirectionalRuleFuturesRegimeDriftV1)
{
    var driftApp = new DirectionalRuleFuturesRegimeDriftAnalysisV1Application(settings);
    var driftResult = await driftApp.RunAsync(CancellationToken.None);
    var recent90 = driftResult.Summary.FirstOrDefault(s =>
        s.PeriodLabel == "90d"
        && string.Equals(s.CostScenarioLabel, DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario, StringComparison.OrdinalIgnoreCase));
    var older = driftResult.Summary.FirstOrDefault(s =>
        s.PeriodLabel == "older"
        && string.Equals(s.CostScenarioLabel, DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.PrimaryCostScenario, StringComparison.OrdinalIgnoreCase));
    var dualFilter = driftResult.OutcomeRules.FirstOrDefault(r => r.SurvivesBothPeriods && r.FilteredTrades > 0);

    Console.WriteLine($"DirectionalRuleFuturesRegimeDriftAnalysisV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Trades analyzed: {driftResult.TotalTrades}, recent90d net={recent90?.NetPnlQuote:F2}, older net={older?.NetPnlQuote:F2}");
    if (dualFilter is not null)
        Console.WriteLine($"Dual-period filter: {dualFilter.RuleDescription} (train={dualFilter.FilteredNetPnlQuote:F2}, test={dualFilter.FilteredTestNetPnlQuote:F2})");
    else
        Console.WriteLine("No dual-period entry-time filter survived with sufficient samples.");
    Console.WriteLine($"Answers: {Path.Combine(settings.OutputDirectory, "directional-rule-v31-regime-drift-answers.json")}");
    return 0;
}

if (settings.RunDirectionalRuleFuturesValidationV31)
{
    var v31App = new DirectionalRuleFuturesValidationV31Application(settings);
    var v31Result = await v31App.RunAsync(CancellationToken.None);
    var bestBnb = v31Result.WindowRobustness.FirstOrDefault(r =>
        r.IsBestBnbCandidate
        && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase));

    Console.WriteLine($"DirectionalRuleFuturesValidationV31 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Executed trades (expanded): {v31Result.ExecutedTradeCount}, Skipped signals: {v31Result.SkippedSignalCount}");
    if (bestBnb is not null)
    {
        Console.WriteLine($"Best BNB: trades={DirectionalRuleFuturesValidationV31Aggregator.ResolveReferenceTradeCount(bestBnb)}, aggregate={bestBnb.AggregateNetPnl:F2}, 180d={bestBnb.Window180dNetPnl:F2}, holdout={bestBnb.Holdout30dNetPnl:F2}, generalizes={DirectionalRuleFuturesValidationV31Aggregator.SameRuleGeneralizesAcrossSymbols(v31Result.WindowRobustness)}");
    }
    Console.WriteLine($"Best BNB summary: {Path.Combine(settings.OutputDirectory, "directional-rule-v31-best-bnb-long-history-summary.json")}");
    Console.WriteLine($"Cross-symbol summary: {Path.Combine(settings.OutputDirectory, "directional-rule-v31-cross-symbol-summary.json")}");
    Console.WriteLine($"Generalization answers: {Path.Combine(settings.OutputDirectory, "directional-rule-v31-generalization-answers.json")}");
    return 0;
}

if (settings.RunDirectionalRuleFuturesValidationV3)
{
    var v3App = new DirectionalRuleFuturesValidationV3Application(settings);
    var v3Result = await v3App.RunAsync(CancellationToken.None);
    var primaryModerate = v3Result.VariantComparison
        .Where(v => v.IsPrimaryCandidate)
        .OrderByDescending(v => v.AggregateNetPnl)
        .FirstOrDefault();

    Console.WriteLine($"DirectionalRuleFuturesValidationV3 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Executed trades (expanded): {v3Result.ExecutedTradeCount}, Skipped signals: {v3Result.SkippedSignalCount}");
    if (primaryModerate is not null)
    {
        Console.WriteLine($"Primary candidate: {primaryModerate.VariantLabel}, trades={primaryModerate.ExecutedTrades}, net={primaryModerate.AggregateNetPnl:F2}, holdout={primaryModerate.Holdout30dNetPnl:F2}, allWindows={primaryModerate.AllRollingWindowsPositive}");
    }
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-focused-summary.json")}");
    Console.WriteLine($"Trades: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-trades.json")}");
    Console.WriteLine($"Window robustness: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-window-robustness.json")}");
    Console.WriteLine($"Cost sensitivity: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-cost-sensitivity.json")}");
    Console.WriteLine($"Drawdown: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-drawdown.json")}");
    Console.WriteLine($"Variant comparison: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-variant-comparison.json")}");
    Console.WriteLine($"Report consistency: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-report-consistency.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "directional-rule-v3-research-answers.json")}");
    return 0;
}

if (settings.RunDirectionalRuleFuturesValidationV2)
{
    var validationApp = new DirectionalRuleFuturesValidationV2Application(settings);
    var validationResult = await validationApp.RunAsync(CancellationToken.None);
    var moderatePositive = validationResult.WindowRobustness.Count(r =>
        string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)
        && r.AggregateNetPositive
        && r.Window30dTrades + r.Window60dTrades + r.Window90dTrades >= DirectionalRuleFuturesValidationV2Aggregator.MinimumMeaningfulTrades);

    Console.WriteLine($"DirectionalRuleFuturesValidationV2 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Executed trades (expanded cost scenarios): {validationResult.ExecutedTradeCount}, Skipped signals: {validationResult.SkippedSignalCount}");
    Console.WriteLine($"Moderate aggregate-positive profiles (>=50 executed): {moderatePositive}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "directional-rule-v2-summary.json")}");
    Console.WriteLine($"Trades: {Path.Combine(settings.OutputDirectory, "directional-rule-v2-trades.json")}");
    Console.WriteLine($"Window robustness: {Path.Combine(settings.OutputDirectory, "directional-rule-v2-window-robustness.json")}");
    Console.WriteLine($"Cost sensitivity: {Path.Combine(settings.OutputDirectory, "directional-rule-v2-cost-sensitivity.json")}");
    Console.WriteLine($"Drawdown: {Path.Combine(settings.OutputDirectory, "directional-rule-v2-drawdown.json")}");
    Console.WriteLine($"Overlap analysis: {Path.Combine(settings.OutputDirectory, "directional-rule-v2-overlap-analysis.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "directional-rule-v2-research-answers.json")}");
    return 0;
}

if (settings.RunDirectionalRuleFuturesSimulationV1)
{
    var directionalApp = new DirectionalRuleFuturesSimulationV1Application(settings);
    var directionalResult = await directionalApp.RunAsync(CancellationToken.None);
    Console.WriteLine($"DirectionalRuleFuturesSimulationV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Rules: {directionalResult.Rules.Count}, Symbols: {string.Join(", ", directionalResult.SymbolsScanned)}, Intervals: {string.Join(", ", directionalResult.IntervalsScanned)}");
    Console.WriteLine($"Trades: {directionalResult.ExpandedTradeCount} expanded ({directionalResult.BaseTradeCount} base, futures-moderate: {directionalResult.ModerateTradeCount}), Net PnL (moderate): {directionalResult.ModerateNetPnlQuote:F8}");
    Console.WriteLine($"Positive futures-moderate configs (>=50 trades): {directionalResult.PositiveModerateConfigs}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "directional-rule-futures-summary.json")}");
    Console.WriteLine($"Trades: {Path.Combine(settings.OutputDirectory, "directional-rule-futures-trades.json")}");
    Console.WriteLine($"Rule performance: {Path.Combine(settings.OutputDirectory, "directional-rule-futures-rule-performance.json")}");
    Console.WriteLine($"Window robustness: {Path.Combine(settings.OutputDirectory, "directional-rule-futures-window-robustness.json")}");
    Console.WriteLine($"Cost sensitivity: {Path.Combine(settings.OutputDirectory, "directional-rule-futures-cost-sensitivity.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "directional-rule-futures-research-answers.json")}");
    return 0;
}

if (settings.RunMarketRegimeForwardEdgeStudyWithBtcContext)
{
    var btcStudyApp = new MarketRegimeForwardEdgeStudyWithBtcContextApplication(settings);
    var btcStudyResult = await btcStudyApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"MarketRegimeForwardEdgeStudyWithBtcContext completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Symbols: {string.Join(", ", btcStudyResult.SymbolsScanned)}, Intervals: {string.Join(", ", btcStudyResult.IntervalsScanned)}");
    Console.WriteLine($"Observations: {btcStudyResult.Observations.Count}, Entry-time rules: {btcStudyResult.EntryTimeRules.Count}");
    Console.WriteLine($"BTC context ranking: {Path.Combine(settings.OutputDirectory, "market-regime-btc-context-ranking.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "market-regime-research-answers.json")}");
    return 0;
}

if (settings.RunRegimeGatedLongEdgeV1)
{
    var regimeGatedApp = new RegimeGatedLongEdgeV1Application(settings);
    var regimeGatedResult = await regimeGatedApp.RunAsync(CancellationToken.None);
    var completedTrades = regimeGatedResult.Trades.Where(t => !string.IsNullOrWhiteSpace(t.ExitReason)).ToArray();

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode = settings.RunRegimeGatedLongEdgeV1BtcContext ? "regime-gated-long-edge-v1-btc-context" : "regime-gated-long-edge-v1",
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.Intervals,
        settings.RobustnessWindows,
        includeResearchVariants = settings.RunRegimeGatedLongEdgeV1IncludeResearchVariants,
        btcContextEnabled = settings.RunRegimeGatedLongEdgeV1BtcContext,
        profileCount = regimeGatedResult.ProfileCount,
        tradeCount = completedTrades.Length,
        blockedSignalCount = regimeGatedResult.BlockedSignals.Count,
        netWinnerCount = completedTrades.Count(t => t.NetPnlQuote > 0m),
        netPnlQuote = completedTrades.Sum(t => t.NetPnlQuote)
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"RegimeGatedLongEdgeV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Profiles: {regimeGatedResult.ProfileCount}, Trades: {completedTrades.Length}, Blocked signals: {regimeGatedResult.BlockedSignals.Count}");
    Console.WriteLine($"Net winners: {completedTrades.Count(t => t.NetPnlQuote > 0m)}, Net PnL: {completedTrades.Sum(t => t.NetPnlQuote):F8}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "regime-gated-v1-summary.json")}");
    Console.WriteLine($"Trades: {Path.Combine(settings.OutputDirectory, "regime-gated-v1-trades.json")}");
    Console.WriteLine($"Blocked: {Path.Combine(settings.OutputDirectory, "regime-gated-v1-blocked-signals.json")}");
    Console.WriteLine($"Rule performance: {Path.Combine(settings.OutputDirectory, "regime-gated-v1-rule-performance.json")}");
    Console.WriteLine($"Window robustness: {Path.Combine(settings.OutputDirectory, "regime-gated-v1-window-robustness.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "regime-gated-v1-research-answers.json")}");
    return 0;
}

if (settings.RunMarketRegimeForwardEdgeStudy)
{
    var regimeApp = new MarketRegimeForwardEdgeStudyApplication(settings);
    var regimeResult = await regimeApp.RunAsync(CancellationToken.None);

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode = "market-regime-forward-edge-study",
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.Intervals,
        settings.RobustnessWindows,
        symbols = regimeResult.SymbolsScanned.Select(s => s.ToString()).ToArray(),
        observationCount = regimeResult.Observations.Count,
        entryTimeRuleCount = regimeResult.EntryTimeRules.Count,
        positiveRegimeBuckets = regimeResult.RegimeBucketRanking.Count(r => r.MedianExpectedNetAfterCostPercent >= 0m),
        holdoutSurvivingRules = regimeResult.EntryTimeRules.Count(r => r.HoldoutMedianExpectedNetPercent >= 0m)
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"MarketRegimeForwardEdgeStudy completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Symbols: {string.Join(", ", regimeResult.SymbolsScanned)}, Intervals: {string.Join(", ", regimeResult.IntervalsScanned)}");
    Console.WriteLine($"Observations: {regimeResult.Observations.Count}, Entry-time rules: {regimeResult.EntryTimeRules.Count}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "market-regime-forward-edge-summary.json")}");
    Console.WriteLine($"Symbol/interval ranking: {Path.Combine(settings.OutputDirectory, "symbol-interval-edge-ranking.json")}");
    Console.WriteLine($"Regime buckets: {Path.Combine(settings.OutputDirectory, "regime-bucket-edge-ranking.json")}");
    Console.WriteLine($"Session ranking: {Path.Combine(settings.OutputDirectory, "session-edge-ranking.json")}");
    Console.WriteLine($"Target-before-stop matrix: {Path.Combine(settings.OutputDirectory, "target-before-stop-matrix.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "market-regime-research-answers.json")}");
    return 0;
}

if (settings.RunMetaStrategyResearch)
{
    var metaApp = new MetaStrategyResearchApplication(settings);
    var metaResult = await metaApp.RunAsync(CancellationToken.None);
    var executed = metaResult.Records.Where(r => r.CandidateWasExecuted).ToArray();

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode = "meta-strategy-research",
        settings.OutputDirectory,
        inputDirectories = metaResult.ImportReport.InputDirectories,
        includeBlockedCandidates = metaResult.ImportReport.IncludedBlockedCandidates,
        recordCount = metaResult.Records.Count,
        executedCount = executed.Length,
        netWinnerCount = executed.Count(r => r.IsNetWinner == true),
        netPnlQuote = executed.Sum(r => r.NetPnlQuote ?? 0m)
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"Meta strategy research completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Records: {metaResult.Records.Count}, Executed: {executed.Length}");
    Console.WriteLine($"Net winners: {executed.Count(r => r.IsNetWinner == true)}, Net PnL: {executed.Sum(r => r.NetPnlQuote ?? 0m):F8}");
    Console.WriteLine($"Family summary: {Path.Combine(settings.OutputDirectory, "meta-strategy-family-summary.json")}");
    Console.WriteLine($"Symbol/interval summary: {Path.Combine(settings.OutputDirectory, "meta-symbol-interval-summary.json")}");
    Console.WriteLine($"Feature buckets: {Path.Combine(settings.OutputDirectory, "meta-feature-bucket-summary.json")}");
    Console.WriteLine($"Best subsets: {Path.Combine(settings.OutputDirectory, "meta-best-subsets.json")}");
    Console.WriteLine($"Entry-time rules: {Path.Combine(settings.OutputDirectory, "meta-entry-time-rule-discovery.json")}");
    Console.WriteLine($"Outcome diagnostic rules: {Path.Combine(settings.OutputDirectory, "meta-outcome-diagnostic-rule-discovery.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "meta-research-answers.json")}");
    return 0;
}

if (settings.RunBroadReachabilityScan)
{
    var scannerApp = new BroadReachabilityScannerApplication(settings);
    var scanResult = await scannerApp.RunAsync(CancellationToken.None);

    Console.WriteLine($"Broad reachability scan completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Symbols: {string.Join(", ", scanResult.SymbolsScanned)}, Intervals: {string.Join(", ", scanResult.IntervalsScanned)}");
    Console.WriteLine($"Candidates: {scanResult.Candidates.Count}, Rankings: {scanResult.Rankings.Count}");
    Console.WriteLine($"Ranking JSON: {Path.Combine(settings.OutputDirectory, "symbol-interval-reachability-ranking.json")}");
    Console.WriteLine($"Discovery answers JSON: {Path.Combine(settings.OutputDirectory, "broad-reachability-discovery-answers.json")}");
    return 0;
}

if (settings.RunHigherTimeframeMomentumPullbackV1)
{
    var htfApp = new HigherTimeframeMomentumPullbackV1Application(settings);
    var htfResult = await htfApp.RunAsync(CancellationToken.None);

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode = "htf-momentum-pullback-v1",
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.Intervals,
        settings.RobustnessWindows,
        includeResearchVariants = settings.RunHigherTimeframeMomentumPullbackV1IncludeResearchVariants,
        profileCount = htfResult.ProfileCount,
        candidateCount = htfResult.Candidates.Count,
        tradeCount = htfResult.Trades.Count,
        netWinnerCount = htfResult.Trades.Count(t => t.NetPnlQuote > 0m),
        netPnlQuote = htfResult.Trades.Sum(t => t.NetPnlQuote)
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"HigherTimeframeMomentumPullbackV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Profiles: {htfResult.ProfileCount}, Candidates: {htfResult.Candidates.Count}, Trades: {htfResult.Trades.Count}");
    Console.WriteLine($"Net winners: {htfResult.Trades.Count(t => t.NetPnlQuote > 0m)}, Net PnL: {htfResult.Trades.Sum(t => t.NetPnlQuote):F8}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "htf-momentum-summary.json")}");
    Console.WriteLine($"Trades: {Path.Combine(settings.OutputDirectory, "htf-momentum-trades.json")}");
    Console.WriteLine($"Blocked: {Path.Combine(settings.OutputDirectory, "htf-momentum-blocked-candidates.json")}");
    Console.WriteLine($"Exit breakdown: {Path.Combine(settings.OutputDirectory, "htf-momentum-exit-breakdown.json")}");
    Console.WriteLine($"Window robustness: {Path.Combine(settings.OutputDirectory, "htf-momentum-window-robustness.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "htf-momentum-research-answers.json")}");
    return 0;
}

if (settings.RunMeanReversionRangeBounceV1)
{
    var rangeBounceApp = new MeanReversionRangeBounceV1Application(settings);
    var rangeBounceResult = await rangeBounceApp.RunAsync(CancellationToken.None);

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode = "mean-reversion-range-v1",
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.Intervals,
        settings.RobustnessWindows,
        includeResearchVariants = settings.RunMeanReversionRangeBounceV1IncludeResearchVariants,
        profileCount = rangeBounceResult.ProfileCount,
        candidateCount = rangeBounceResult.Candidates.Count,
        tradeCount = rangeBounceResult.Trades.Count,
        netWinnerCount = rangeBounceResult.Trades.Count(t => t.NetPnlQuote > 0m),
        netPnlQuote = rangeBounceResult.Trades.Sum(t => t.NetPnlQuote)
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"MeanReversionRangeBounceV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Profiles: {rangeBounceResult.ProfileCount}, Candidates: {rangeBounceResult.Candidates.Count}, Trades: {rangeBounceResult.Trades.Count}");
    Console.WriteLine($"Net winners: {rangeBounceResult.Trades.Count(t => t.NetPnlQuote > 0m)}, Net PnL: {rangeBounceResult.Trades.Sum(t => t.NetPnlQuote):F8}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "mean-reversion-range-summary.json")}");
    Console.WriteLine($"Trades: {Path.Combine(settings.OutputDirectory, "mean-reversion-range-trades.json")}");
    Console.WriteLine($"Blocked: {Path.Combine(settings.OutputDirectory, "mean-reversion-range-blocked-candidates.json")}");
    Console.WriteLine($"Reachability: {Path.Combine(settings.OutputDirectory, "mean-reversion-range-reachability.json")}");
    Console.WriteLine($"Exit breakdown: {Path.Combine(settings.OutputDirectory, "mean-reversion-range-exit-breakdown.json")}");
    Console.WriteLine($"Window robustness: {Path.Combine(settings.OutputDirectory, "mean-reversion-range-window-robustness.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "mean-reversion-range-research-answers.json")}");
    return 0;
}

if (settings.RunImpulseContinuationV1)
{
    var impulseApp = new ImpulseContinuationV1Application(settings);
    var impulseResult = await impulseApp.RunAsync(CancellationToken.None);

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode = "impulse-continuation-v1",
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.Intervals,
        settings.RobustnessWindows,
        includeResearchVariants = settings.RunImpulseContinuationV1IncludeResearchVariants,
        profileCount = impulseResult.ProfileCount,
        candidateCount = impulseResult.Candidates.Count,
        tradeCount = impulseResult.Trades.Count,
        netWinnerCount = impulseResult.Trades.Count(t => t.NetPnlQuote > 0m),
        netPnlQuote = impulseResult.Trades.Sum(t => t.NetPnlQuote)
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"ImpulseContinuationV1 completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Profiles: {impulseResult.ProfileCount}, Candidates: {impulseResult.Candidates.Count}, Trades: {impulseResult.Trades.Count}");
    Console.WriteLine($"Net winners: {impulseResult.Trades.Count(t => t.NetPnlQuote > 0m)}, Net PnL: {impulseResult.Trades.Sum(t => t.NetPnlQuote):F8}");
    Console.WriteLine($"Summary: {Path.Combine(settings.OutputDirectory, "impulse-continuation-summary.json")}");
    Console.WriteLine($"Trades: {Path.Combine(settings.OutputDirectory, "impulse-continuation-trades.json")}");
    Console.WriteLine($"Blocked: {Path.Combine(settings.OutputDirectory, "impulse-continuation-blocked-candidates.json")}");
    Console.WriteLine($"Reachability: {Path.Combine(settings.OutputDirectory, "impulse-continuation-reachability.json")}");
    Console.WriteLine($"Exit breakdown: {Path.Combine(settings.OutputDirectory, "impulse-continuation-exit-breakdown.json")}");
    Console.WriteLine($"Window robustness: {Path.Combine(settings.OutputDirectory, "impulse-continuation-window-robustness.json")}");
    Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "impulse-continuation-research-answers.json")}");
    return 0;
}

if (settings.RunRangeExpansionV2 || settings.RunRangeExpansionV21Fast || settings.RunRangeExpansionV22 || settings.RunRangeExpansionV23 || settings.RunRangeExpansionV24 || settings.RunRangeExpansionV2Feasibility)
{
    var v2App = new RangeExpansionBreakoutV2Application(settings);
    var v2Result = await v2App.RunAsync(CancellationToken.None);
    var mode = settings.RunRangeExpansionV2Feasibility
        ? "range-expansion-v2-feasibility"
        : settings.RunRangeExpansionV24
            ? "range-expansion-v24"
            : settings.RunRangeExpansionV23
                ? "range-expansion-v23"
                : settings.RunRangeExpansionV22
                    ? "range-expansion-v22"
                    : settings.RunRangeExpansionV21Fast
                        ? "range-expansion-v21-fast"
                        : "range-expansion-v2";

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode,
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.Intervals,
        settings.RobustnessWindows,
        profileCount = v2Result.ProfileCount,
        candidateCount = v2Result.Candidates.Count,
        tradeCount = v2Result.Trades.Count,
        netWinnerCount = v2Result.Trades.Count(t => t.NetPnlQuote > 0m),
        netPnlQuote = v2Result.Trades.Sum(t => t.NetPnlQuote)
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    var modeLabel = settings.RunRangeExpansionV2Feasibility
        ? "V2 feasibility"
        : settings.RunRangeExpansionV24
            ? "V2.4"
            : settings.RunRangeExpansionV23
                ? "V2.3"
                : settings.RunRangeExpansionV22
                    ? "V2.2"
                    : settings.RunRangeExpansionV21Fast
                        ? "V2.1 fast"
                        : "V2";
    Console.WriteLine($"Range expansion {modeLabel} completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Profiles: {v2Result.ProfileCount}, Candidates: {v2Result.Candidates.Count}, Trades: {v2Result.Trades.Count}");
    Console.WriteLine($"Net winners: {v2Result.Trades.Count(t => t.NetPnlQuote > 0m)}, Net PnL: {v2Result.Trades.Sum(t => t.NetPnlQuote):F8}");
    if (settings.RunRangeExpansionV2Feasibility)
    {
        Console.WriteLine($"V2 feasibility summary: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-feasibility-summary.json")}");
        Console.WriteLine($"V2 cost surface: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-cost-surface.json")}");
        Console.WriteLine($"V2 break-even cost analysis: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-break-even-cost-analysis.json")}");
        Console.WriteLine($"V2 feasibility answers: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-feasibility-answers.json")}");
    }
    else if (settings.RunRangeExpansionV24)
    {
        Console.WriteLine($"V2.4 fast summary: {Path.Combine(settings.OutputDirectory, "range-expansion-v24-fast-summary.json")}");
        Console.WriteLine($"V2.4 exit policy impact: {Path.Combine(settings.OutputDirectory, "range-expansion-v24-exit-policy-impact.json")}");
        Console.WriteLine($"V2.4 window robustness: {Path.Combine(settings.OutputDirectory, "range-expansion-v24-window-robustness.json")}");
        Console.WriteLine($"V2.4 cost sensitivity: {Path.Combine(settings.OutputDirectory, "range-expansion-v24-cost-sensitivity.json")}");
        Console.WriteLine($"V2.4 research answers: {Path.Combine(settings.OutputDirectory, "range-expansion-v24-research-answers.json")}");
    }
    else if (settings.RunRangeExpansionV23)
    {
        Console.WriteLine($"V2.3 fast summary: {Path.Combine(settings.OutputDirectory, "range-expansion-v23-fast-summary.json")}");
        Console.WriteLine($"V2.3 filter impact: {Path.Combine(settings.OutputDirectory, "range-expansion-v23-filter-impact.json")}");
        Console.WriteLine($"V2.3 window robustness: {Path.Combine(settings.OutputDirectory, "range-expansion-v23-window-robustness.json")}");
        Console.WriteLine($"V2.3 cost sensitivity: {Path.Combine(settings.OutputDirectory, "range-expansion-v23-cost-sensitivity.json")}");
        Console.WriteLine($"V2.3 research answers: {Path.Combine(settings.OutputDirectory, "range-expansion-v23-research-answers.json")}");
    }
    else if (settings.RunRangeExpansionV22)
    {
        Console.WriteLine($"V2.2 fast summary: {Path.Combine(settings.OutputDirectory, "range-expansion-v22-fast-summary.json")}");
        Console.WriteLine($"V2.2 filter impact: {Path.Combine(settings.OutputDirectory, "range-expansion-v22-filter-impact.json")}");
        Console.WriteLine($"V2.2 research answers: {Path.Combine(settings.OutputDirectory, "range-expansion-v22-research-answers.json")}");
    }
    else
    {
        Console.WriteLine($"Exit outcome comparison: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-exit-outcome-comparison.json")}");
        Console.WriteLine($"Failure timing: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-failure-timing.json")}");
        Console.WriteLine($"Symbol exit breakdown: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-symbol-exit-breakdown.json")}");
        if (settings.RunRangeExpansionV21Fast)
        {
            Console.WriteLine($"V2.1 fast summary: {Path.Combine(settings.OutputDirectory, "range-expansion-v21-fast-summary.json")}");
            Console.WriteLine($"V2.1 research answers: {Path.Combine(settings.OutputDirectory, "range-expansion-v21-research-answers.json")}");
        }
        else
        {
            Console.WriteLine($"Research answers: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-research-answers.json")}");
            Console.WriteLine($"Exit breakdown: {Path.Combine(settings.OutputDirectory, "range-expansion-v2-exit-breakdown.json")}");
        }
    }
    return 0;
}

if (settings.RunRangeExpansionResearch || settings.RunRangeExpansionFast || settings.RunRangeExpansionV2Fast || settings.RunRangeExpansionTargetFloorExperiment)
{
    var rangeExpansionApp = new RangeExpansionBreakoutApplication(settings);
    var rangeExpansionResult = await rangeExpansionApp.RunAsync(CancellationToken.None);
    var mode = settings.RunRangeExpansionTargetFloorExperiment
        ? "range-expansion-target-floor-experiment"
        : settings.RunRangeExpansionV2Fast
            ? "range-expansion-v2-fast"
            : settings.RunRangeExpansionFast
                ? "range-expansion-fast"
                : "range-expansion-research";

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode,
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.Intervals,
        settings.RobustnessWindows,
        settings.BootstrapDays,
        settings.FeeRatePercent,
        settings.EstimatedSpreadPercent,
        settings.SlippagePercent,
        profileCount = rangeExpansionResult.ProfileCount,
        candidateCount = rangeExpansionResult.Candidates.Count,
        tradeCount = rangeExpansionResult.Trades.Count,
        blockedEntryCount = rangeExpansionResult.BlockedEntries.Count,
        tradeabilityVerdict = rangeExpansionResult.Diagnostics.TradeabilityVerdict,
        separatorDetected = rangeExpansionResult.Diagnostics.SeparatorDetected,
        v2Recommended = rangeExpansionResult.Diagnostics.V2Recommendation?.ShouldCreateV2Profiles
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"Range expansion {mode} completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Profiles: {rangeExpansionResult.ProfileCount}, Candidates: {rangeExpansionResult.Candidates.Count}, Trades: {rangeExpansionResult.Trades.Count}");
    Console.WriteLine($"Tradeability: {rangeExpansionResult.Diagnostics.TradeabilityVerdict}, Separator: {rangeExpansionResult.Diagnostics.SeparatorDetected}");
    Console.WriteLine($"PnL decomposition JSON: {Path.Combine(settings.OutputDirectory, "range-expansion-pnl-decomposition.json")}");
    Console.WriteLine($"Diagnostic answers JSON: {Path.Combine(settings.OutputDirectory, "range-expansion-diagnostic-answers.json")}");
    Console.WriteLine($"Blocked reachable JSON: {Path.Combine(settings.OutputDirectory, "range-expansion-blocked-reachable-analysis.json")}");
    return 0;
}

if (settings.RunReachabilityResearch)
{
    var reachabilityApp = new ReachabilityResearchApplication(settings);
    var reachabilityResult = await reachabilityApp.RunAsync(CancellationToken.None);

    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");
    var metadata = new
    {
        startedAtUtc,
        completedAtUtc = DateTime.UtcNow,
        mode = "reachability-research",
        settings.DataDirectory,
        settings.OutputDirectory,
        settings.BootstrapDays,
        settings.FeeRatePercent,
        settings.EstimatedSpreadPercent,
        settings.SlippagePercent,
        profileCount = reachabilityResult.ProfileCount,
        candidateCount = reachabilityResult.Candidates.Count,
        tradeCount = reachabilityResult.Trades.Count,
        blockedEntryCount = reachabilityResult.BlockedEntries.Count
    };
    await File.WriteAllTextAsync(
        metadataPath,
        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));

    Console.WriteLine($"Reachability research completed. Output: {settings.OutputDirectory}");
    Console.WriteLine($"Profiles: {reachabilityResult.ProfileCount}, Candidates: {reachabilityResult.Candidates.Count}, Trades: {reachabilityResult.Trades.Count}");
    Console.WriteLine($"Summary JSON: {Path.Combine(settings.OutputDirectory, "candidate-reachability-summary.json")}");
    Console.WriteLine($"Research answers JSON: {Path.Combine(settings.OutputDirectory, "reachability-research-answers.json")}");
    return 0;
}

if (settings.RunRobustness)

{

    var robustnessApp = new RobustnessApplication(settings);

    var robustnessResult = await robustnessApp.RunAsync(CancellationToken.None);



    var metadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");

    var metadata = new

    {

        startedAtUtc,

        completedAtUtc = DateTime.UtcNow,

        mode = "robustness",

        settings.DataDirectory,

        settings.OutputDirectory,

        settings.Intervals,

        settings.RobustnessWindows,

        settings.RobustnessWindowStartUtc,

        settings.RobustnessWindowEndUtc,

        settings.BootstrapMissingData,

        settings.BootstrapDays,

        settings.BootstrapStartUtc,

        settings.BootstrapEndUtc,

        settings.FeeRatePercent,

        settings.EstimatedSpreadPercent,

        settings.SlippagePercent,

        profileCount = robustnessResult.ProfileCount,

        tradeCount = robustnessResult.Trades.Count,

        blockedEntryCount = robustnessResult.BlockedEntries.Count,

        windows = robustnessResult.Windows.Select(w => new

        {

            w.Label,

            w.StartUtc,

            w.EndUtc,

            w.SkippedInsufficientData,

            w.SkipReason

        })

    };

    await File.WriteAllTextAsync(

        metadataPath,

        JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));



    Console.WriteLine($"Robustness backtest completed. Output: {settings.OutputDirectory}");

    Console.WriteLine($"Profiles: {robustnessResult.ProfileCount}, Trades: {robustnessResult.Trades.Count}, Blocked entries: {robustnessResult.BlockedEntries.Count}");

    Console.WriteLine($"Windows run: {robustnessResult.WindowDetails.Select(x => x.WindowLabel).Distinct().Count()}, skipped: {robustnessResult.Windows.Count(w => w.SkippedInsufficientData)}");

    Console.WriteLine($"Robustness summary JSON: {Path.Combine(settings.OutputDirectory, "robustness-summary.json")}");

    Console.WriteLine($"Interval comparison JSON: {Path.Combine(settings.OutputDirectory, "interval-comparison-summary.json")}");

    return 0;

}



var app = new BacktestApplication(settings);

var result = await app.RunAsync(CancellationToken.None);



var standardMetadataPath = Path.Combine(settings.OutputDirectory, "run-metadata.json");

var standardMetadata = new

{

    startedAtUtc,

    completedAtUtc = DateTime.UtcNow,

    settings.DataDirectory,

    settings.OutputDirectory,

    settings.Intervals,

    settings.BootstrapMissingData,

    settings.BootstrapLimit,

    settings.BootstrapDays,

    settings.BootstrapStartUtc,

    settings.BootstrapEndUtc,

    settings.FeeRatePercent,

    settings.EstimatedSpreadPercent,

    settings.SlippagePercent,

    profileCount = result.Summaries.Count,

    tradeCount = result.Trades.Count,

    blockedEntryCount = result.BlockedEntries.Count

};

await File.WriteAllTextAsync(

    standardMetadataPath,

    JsonSerializer.Serialize(standardMetadata, new JsonSerializerOptions { WriteIndented = true }));



Console.WriteLine($"Backtest completed. Output: {settings.OutputDirectory}");

Console.WriteLine($"Profiles: {result.Summaries.Count}, Trades: {result.Trades.Count}, Blocked entries: {result.BlockedEntries.Count}");

if (settings.Intervals.Count > 1)

{

    Console.WriteLine("Per-interval reports written under interval folders (1m/3m/5m).");

    Console.WriteLine($"Cross-interval summary JSON: {Path.Combine(settings.OutputDirectory, "cross-interval-summary.json")}");

    Console.WriteLine($"Aggregation diagnostics JSON: {Path.Combine(settings.OutputDirectory, "aggregation-diagnostics.json")}");

}

else

{

    Console.WriteLine($"Summary JSON: {Path.Combine(settings.OutputDirectory, "summary.json")}");

    Console.WriteLine($"Trades JSON: {Path.Combine(settings.OutputDirectory, "trades.json")}");

    Console.WriteLine($"Blocked entries JSON: {Path.Combine(settings.OutputDirectory, "blocked-entries.json")}");

    Console.WriteLine($"Validation JSON: {Path.Combine(settings.OutputDirectory, "data-quality.json")}");

}

return 0;

