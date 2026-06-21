namespace TradingBot.Backtest;

/// <summary>
/// Reporting-only risk-normalized PnL metrics. Does not affect strategy, activation, or verdict logic.
/// Assumes 1-unit simulated trades scaled to reference notional with optional fractional sizing by account leverage.
/// </summary>
public sealed record NormalizedRiskPnlMetrics
{
    /// <summary>Reference 1-unit position notional (USDT) used for normalization.</summary>
    public decimal AssumedUnitNotionalUsdt { get; init; }

    /// <summary>Fraction of 1 unit when account leverage caps deployable notional (1 = full unit).</summary>
    public decimal FractionalPositionScaleAt100Usdt3x { get; init; }
    public decimal FractionalPositionScaleAt100Usdt5x { get; init; }
    public decimal FractionalPositionScaleAt1000Usdt3x { get; init; }
    public decimal FractionalPositionScaleAt1000Usdt5x { get; init; }

    public decimal NetPnlPer100UsdtAt3x { get; init; }
    public decimal NetPnlPer100UsdtAt5x { get; init; }
    public decimal NetPnlPer1000UsdtAt3x { get; init; }
    public decimal NetPnlPer1000UsdtAt5x { get; init; }

    public decimal RequiredMarginUsdtAt3x { get; init; }
    public decimal RequiredMarginUsdtAt5x { get; init; }

    /// <summary>Max drawdown expressed as percent of a $100 account after fractional scaling.</summary>
    public decimal MaxDrawdownPercentOf100Usdt { get; init; }

    /// <summary>Max drawdown expressed as percent of a $1,000 account after fractional scaling.</summary>
    public decimal MaxDrawdownPercentOf1000Usdt { get; init; }
}

/// <summary>Trade row plus reporting-only normalization fields for JSON/CSV export.</summary>
public sealed record NormalizedCrossSymbolTradeReportRow
{
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public string ActivationRule { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public bool IsWinner { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
}

/// <summary>Short-window (BNB 5m) trade row plus reporting-only normalization fields.</summary>
public sealed record NormalizedShortWindowTradeReportRow
{
    public string ActivationRuleName { get; init; } = string.Empty;
    public DateTime EntryTimeUtc { get; init; }
    public DateTime ExitTimeUtc { get; init; }
    public decimal NetPnlQuote { get; init; }
    public bool IsWinner { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public string CostScenario { get; init; } = string.Empty;
    public DateTime ActivationStartUtc { get; init; }
    public DateTime ActivationEndUtc { get; init; }
    public bool SparseLookbackActivation { get; init; }
    public NormalizedRiskPnlMetrics NormalizedRisk { get; init; } = new();
}
