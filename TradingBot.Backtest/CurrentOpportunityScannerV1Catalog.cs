namespace TradingBot.Backtest;

public static class CurrentOpportunityScannerV1Catalog
{
    public const string ModeName = "current-opportunity-scanner-v1";
    public const string DefaultOutputSubdir = "current-opportunity-scanner-v1";
    public const string DefaultV1InputSubdir = CrossSymbolCandidateEngineV2Catalog.DefaultV1InputSubdir;
    public const string DefaultV2InputSubdir = CrossSymbolCandidateEngineV2Catalog.DefaultOutputSubdir;
    public const string DefaultBottleneckAuditSubdir = CrossSymbolCandidateEngineV2Catalog.DefaultBottleneckAuditSubdir;
    public const string DefaultShadowRunnerSubdir = CrossSymbolCandidateEngineV2Catalog.DefaultShadowRunnerSubdir;

    public const decimal DefaultShadowNotionalUsdt = 25m;
    public const int TopActionableLimit = 10;
    public const int TopAlmostActionableLimit = 10;
    public const int TopBlockersLimit = 5;
}
