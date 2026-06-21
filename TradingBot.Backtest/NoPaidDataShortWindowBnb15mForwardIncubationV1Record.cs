namespace TradingBot.Backtest;

public sealed record NoPaidDataShortWindowBnb15mForwardIncubationV1RunResult(
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
    bool ProtectedFrozenFilesByteIdentical,
    IReadOnlyList<string> ProtectedFilesChecked);
