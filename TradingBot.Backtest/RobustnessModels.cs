namespace TradingBot.Backtest;

public sealed record RobustnessWindow(
    string Label,
    DateTime StartUtc,
    DateTime EndUtc,
    bool SkippedInsufficientData,
    string? SkipReason);

public sealed record RobustnessWindowDetailRow
{
    public string ProfileName { get; init; } = string.Empty;
    public string Interval { get; init; } = "1m";
    public string WindowLabel { get; init; } = string.Empty;
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
    public int TradesCount { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public IReadOnlyDictionary<string, decimal> NetPnlBySymbol { get; init; } = new Dictionary<string, decimal>();
    public int ProfitLockExitTrades { get; init; }
    public int OppositeSignalExitTrades { get; init; }
    public decimal AvgMfePercent { get; init; }
    public decimal AvgMaePercent { get; init; }
    public decimal AvgGivebackFromMfePercent { get; init; }
    public decimal AvgCapturedMfePercent { get; init; }
    public string CapturedMfeCalculationMode { get; init; } = string.Empty;
    public decimal? AvgCapturedMfeIncludingNegativeRatio { get; init; }
    public int NegativeCaptureTradeCount { get; init; }
    public bool BnbPullbackGuardEnabled { get; init; }
    public int BnbPullbackGuardBlockedSignals { get; init; }
    public IReadOnlyDictionary<string, int> BnbPullbackGuardBlockedByReason { get; init; } = new Dictionary<string, int>();
}

public sealed record RobustnessSummaryRow
{
    public string ProfileName { get; init; } = string.Empty;
    public string Interval { get; init; } = "1m";
    public DateTime WindowStartUtc { get; init; }
    public DateTime WindowEndUtc { get; init; }
    public int WindowCount { get; init; }
    public int TradesCount { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public IReadOnlyDictionary<string, decimal> NetPnlBySymbol { get; init; } = new Dictionary<string, decimal>();
    public int ProfitLockExitTrades { get; init; }
    public int OppositeSignalExitTrades { get; init; }
    public decimal AvgMfePercent { get; init; }
    public decimal AvgMaePercent { get; init; }
    public decimal AvgGivebackFromMfePercent { get; init; }
    public decimal AvgCapturedMfePercent { get; init; }
    public string CapturedMfeCalculationMode { get; init; } = string.Empty;
    public decimal? AvgCapturedMfeIncludingNegativeRatio { get; init; }
    public int NegativeCaptureTradeCount { get; init; }
    public int PositiveWindowsCount { get; init; }
    public int NegativeWindowsCount { get; init; }
    public decimal MedianNetPnlPerTrade { get; init; }
    public decimal MinWindowNetPnl { get; init; }
    public bool OneTradeProfileWarning { get; init; }
    public bool BnbPullbackGuardEnabled { get; init; }
    public int BnbPullbackGuardBlockedSignals { get; init; }
    public IReadOnlyDictionary<string, int> BnbPullbackGuardBlockedByReason { get; init; } = new Dictionary<string, int>();
}

public sealed record RobustnessRunResult(
    IReadOnlyList<RobustnessWindowDetailRow> WindowDetails,
    IReadOnlyList<RobustnessSummaryRow> Summaries,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<RobustnessWindow> Windows,
    int ProfileCount);
