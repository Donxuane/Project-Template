namespace TradingBot.Backtest;

public static class EntryNearMissAuditV1Catalog
{
    public const string ModeName = "entry-near-miss-audit-v1";
    public const string DefaultOutputSubdir = "entry-near-miss-audit-v1";
    public const string DefaultScannerInputSubdir = CurrentOpportunityScannerV1Catalog.DefaultOutputSubdir;
    public const string DefaultV1InputSubdir = CurrentOpportunityScannerV1Catalog.DefaultV1InputSubdir;
    public const string DefaultV2InputSubdir = CurrentOpportunityScannerV1Catalog.DefaultV2InputSubdir;

    public const decimal PriceDistanceNearThresholdPercent = 0.25m;
    public const int TopNearMissLimit = 15;
    public const int FarMissLimit = 15;
}
