namespace TradingBot.Backtest;

public static class CrossSymbolExactEntryReconciliationAuditV1Catalog
{
    public const string ModeName = "cross-symbol-exact-entry-reconciliation-v1";
    public const string DefaultOutputSubdir = "cross-symbol-exact-entry-reconciliation-v1";
    public const string OutputPrefix = "cross-symbol-exact-entry-reconciliation-v1";
    public const string DefaultFrequencyInputSubdir = CrossCandidateExactEntryFrequencyStudyV1Catalog.DefaultOutputSubdir;

    public const int MinSampleMismatches = 20;
}
