namespace TradingBot.Backtest;

/// <summary>
/// Run result for the SOL forward-incubation track. Reuses the frozen-candidate state/summary,
/// coverage, health-gate, and history record types from the BNB track (types only — the BNB
/// track's files and data are never touched), and the cross-symbol trade/period/cost row types
/// matching the engine that discovered the candidate.
/// </summary>
public sealed record NoPaidDataShortWindowSolForwardIncubationV1RunResult(
    FrozenCandidateSummaryRow FrozenSummary,
    IReadOnlyList<ForwardDataCoverageRow> DataCoverage,
    IReadOnlyList<CrossSymbolTradeRow> ForwardTrades,
    IReadOnlyList<CrossSymbolPeriodRow> ForwardPeriods,
    IReadOnlyList<CrossSymbolCostSensitivityRow> CostSensitivity,
    IReadOnlyList<ForwardHealthGateRow> HealthGates,
    IReadOnlyList<ForwardIncubationHistoryEntry> History,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    ForwardIncubationNoTradeReasonSummary NoTradeReasonSummary,
    string Verdict,
    DateTime FrozenStartUtc,
    DateTime ForwardWindowEndUtc,
    decimal ForwardSpanDays,
    bool BnbFrozenFilesByteIdentical,
    IReadOnlyList<string> BnbFilesChecked);
