namespace TradingBot.Backtest;

public sealed record FuturesTestnetShadowDecisionRow
{
    public DateTime TimestampUtc { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public bool ActivationPassed { get; init; }
    public string ActivationReason { get; init; } = string.Empty;
    public bool EntrySignalPresent { get; init; }
    public string EntryReason { get; init; } = string.Empty;
    public bool WouldPlaceOrder { get; init; }
    public string OrderSide { get; init; } = string.Empty;
    public decimal? IntendedEntryPrice { get; init; }
    public decimal? TargetPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public decimal HoldHours { get; init; }
    public decimal AssumedNotionalUsdt { get; init; }
    public decimal? NetPnlPer100Usdt { get; init; }
    public decimal? RequiredMarginAtLeverage { get; init; }
    public int Leverage { get; init; }
    public decimal? QuantityRaw { get; init; }
    public decimal? QuantityRounded { get; init; }
    public decimal? PriceTickSize { get; init; }
    public decimal? QuantityStepSize { get; init; }
    public decimal? MinNotional { get; init; }
    public bool PrecisionValid { get; init; }
    public string RiskStatus { get; init; } = string.Empty;
    public string ReasonIfBlocked { get; init; } = string.Empty;
    public bool RequireForwardTradeEvidence { get; init; }
    public int ForwardTradeCount { get; init; }
    public decimal? ForwardNetModerate { get; init; }
    public decimal? ForwardNetStressPlus { get; init; }
    public bool ForwardEvidencePassed { get; init; }
    public bool ShadowRunnerCanPlaceIfSignalAppears { get; init; }
    public string ForwardEvidenceSourceFile { get; init; } = string.Empty;
    public string ForwardEvidenceSourceProfileName { get; init; } = string.Empty;
    public DateTime? ForwardEvidenceWindowStartUtc { get; init; }
    public DateTime? ForwardEvidenceWindowEndUtc { get; init; }
    public bool ForwardEvidenceIsTrueForwardOnly { get; init; }
}

public sealed record FuturesTestnetShadowRiskRow
{
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public decimal AssumedNotionalUsdt { get; init; }
    public int Leverage { get; init; }
    public decimal? RequiredMarginAtLeverage { get; init; }
    public decimal MaxNotionalUsdtLimit { get; init; }
    public bool WithinMaxNotional { get; init; }
    public bool PrecisionValid { get; init; }
    public string RiskStatus { get; init; } = string.Empty;
    public string ReasonIfBlocked { get; init; } = string.Empty;
}

public sealed record FuturesTestnetShadowSummaryRow
{
    public DateTime RunAtUtc { get; init; }
    public DateTime EvaluationUtc { get; init; }
    public string Mode { get; init; } = "futures-testnet-shadow-runner";
    public bool BacktestOnly { get; init; } = true;
    public bool TestnetShadowOnly { get; init; } = true;
    public bool RealOrdersPlaced { get; init; }
    public bool LiveFuturesRecommended { get; init; }
    public bool DryRunOnly { get; init; } = true;
    public bool AllowTestnetOrders { get; init; }
    public bool AllowRealOrders { get; init; }
    public bool ShadowEnabledInConfig { get; init; }
    public string KeySafetyStatus { get; init; } = string.Empty;
    public int ProfilesEvaluated { get; init; }
    public int ActivationPassedCount { get; init; }
    public int EntrySignalCount { get; init; }
    public int WouldPlaceOrderCount { get; init; }
    public string CompactSummaryLine { get; init; } = string.Empty;
}

public sealed record FuturesTestnetShadowRunResult(
    FuturesTestnetShadowSummaryRow Summary,
    IReadOnlyList<FuturesTestnetShadowDecisionRow> Decisions,
    IReadOnlyList<FuturesTestnetShadowRiskRow> RiskRows,
    string KeySafetyStatus,
    bool SafetyBlockedRealKeys,
    IReadOnlyList<ShortWindowDownloadOutcome> DownloadOutcomes,
    bool BootstrapAttempted);
