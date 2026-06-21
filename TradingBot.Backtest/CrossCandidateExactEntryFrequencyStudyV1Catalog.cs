namespace TradingBot.Backtest;

public static class CrossCandidateExactEntryFrequencyStudyV1Catalog
{
    public const string ModeName = "cross-candidate-exact-entry-frequency-v1";
    public const string DefaultOutputSubdir = "cross-candidate-exact-entry-frequency-v1";
    public const string OutputPrefix = "cross-candidate-exact-entry-frequency-v1";

    public const int TopFrequencyLimit = 15;
    public const int TopStressPositiveLimit = 15;
    public const int MinTradeCountForShadow = CrossSymbolCandidateEngineV2Catalog.MinTradeCountForShadow;

    public const int StaleLastEntryDays = 14;
    public const int TooRareMaxExactEntries = 2;
    public const decimal TooRareMaxEntriesPerDay = 0.05m;
    public const decimal FrequentEnoughMinEntriesPerDay = 0.10m;
    public const decimal ModerateFrequencyMinEntriesPerDay = 0.05m;

    public const decimal EntryFrequencyScoreWeight = 0.55m;
    public const decimal StressQualityScoreWeight = 0.45m;
}
