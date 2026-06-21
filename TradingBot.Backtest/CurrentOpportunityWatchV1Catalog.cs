namespace TradingBot.Backtest;

public static class CurrentOpportunityWatchV1Catalog
{
    public const string ModeName = "current-opportunity-watch-v1";
    public const string DefaultOutputSubdir = "current-opportunity-watch-v1";
    public const string DefaultScannerOutputSubdir = CurrentOpportunityScannerV1Catalog.DefaultOutputSubdir;
    public const string DefaultNearMissOutputSubdir = EntryNearMissAuditV1Catalog.DefaultOutputSubdir;
    public const string DefaultV2InputSubdir = CrossSymbolCandidateEngineV2Catalog.DefaultOutputSubdir;

    public const int DefaultWatchIntervalMinutes = 5;
    public const int MaxHistoryRows = 5000;
    public const int TopWatchlistLimit = 25;

    // Fixed-frequency study integration (diagnostic only).
    public const string DefaultFrequencyStudySubdir = CrossCandidateExactEntryFrequencyStudyV1Catalog.DefaultOutputSubdir;
    public const string FrequencyStudyOutputPrefix = CrossCandidateExactEntryFrequencyStudyV1Catalog.OutputPrefix;
    public const string PromoteToExactEntryWatcherRecommendation = "PromoteToExactEntryWatcher";
    public const string FixedFrequencyWatchKind = "FixedFrequencyExactEntryWatch";

    public const string WatchReasonCurrentExactEntry = "HistoricalFrequencyStrongCurrentExactEntryPresent";
    public const string WatchReasonReadinessBlocked = "HistoricalFrequencyStrongButCurrentReadinessBlocked";
    public const string WatchReasonNeedsIncubation = "HistoricalFrequencyStrongNeedsForwardIncubation";
    public const string WatchReasonAwaitingEntry = "HistoricalFrequencyStrongAwaitingCurrentExactEntry";

    public const string ExactEntryAppearedMessage =
        "Exact shadow entry signal appeared. Review before any testnet action.";

    public const string SolUsdt30mShortSymbol = "SOLUSDT";
    public const string SolUsdt30mShortInterval = "30m";
    public const string SolUsdt30mShortDirection = "Short";
}
