using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// The five actions the SpotFuturesCrossMarketTestnetV1 strategy can decide on each fully
/// closed candle.
/// </summary>
public enum CrossMarketAction
{
    NoTrade = 0,
    OpenLong = 1,
    OpenShort = 2,
    CloseLong = 3,
    CloseShort = 4
}

/// <summary>Outcome of one deterministic entry gate.</summary>
public enum EntryGateState
{
    NotEvaluated = 0,
    Pass = 1,
    Fail = 2
}

/// <summary>One gate outcome, including the market values used to reach it.</summary>
public sealed record EntryGateResult(
    string Gate,
    EntryGateState State,
    string Detail);

/// <summary>Ordered gate outcomes for one candidate entry direction.</summary>
public sealed record EntrySideGateTrace(
    string Side,
    bool SetupQualified,
    string? PrimaryRejection,
    IReadOnlyList<EntryGateResult> Gates);

/// <summary>Long and short entry diagnostics from one closed-candle evaluation.</summary>
public sealed record CrossMarketEntryGateTrace(
    EntrySideGateTrace Long,
    EntrySideGateTrace Short)
{
    /// <summary>
    /// Compact, stable representation persisted inside the existing decision reason column.
    /// Detailed values remain available in the structured report/Redis representation.
    /// </summary>
    public string ToCompactString()
        => $"long{{{Compact(Long)}}} short{{{Compact(Short)}}}";

    private static string Compact(EntrySideGateTrace trace)
        => string.Join(',', trace.Gates.Select(g => $"{g.Gate}={StateCode(g.State)}"));

    private static char StateCode(EntryGateState state) => state switch
    {
        EntryGateState.Pass => 'P',
        EntryGateState.Fail => 'F',
        _ => 'N'
    };
}

/// <summary>
/// Synchronized Spot + USD-M Futures view of one symbol, anchored on the latest candle that
/// is fully closed on BOTH markets. Spot provides leading context; Futures is the traded
/// instrument and confirms the move.
/// </summary>
public sealed class CrossMarketSnapshot
{
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = string.Empty;

    /// <summary>Open time (UTC) of the aligned fully closed candle on both markets.</summary>
    public DateTime CandleOpenTimeUtc { get; init; }

    /// <summary>Close time (UTC) of the aligned fully closed candle on both markets.</summary>
    public DateTime CandleCloseTimeUtc { get; init; }

    /// <summary>True when both feeds have the same latest fully closed candle and enough history.</summary>
    public bool MarketsInSync { get; init; }
    public string? SyncIssue { get; init; }

    /// <summary>Closed spot candles up to and including the aligned candle.</summary>
    public MarketSnapshot? Spot { get; init; }

    /// <summary>Closed futures candles up to and including the aligned candle.</summary>
    public MarketSnapshot? Futures { get; init; }

    /// <summary>Synchronized higher-timeframe spot context used as the directional regime.</summary>
    public MarketSnapshot? RegimeSpot { get; init; }

    /// <summary>Synchronized higher-timeframe futures confirmation of the directional regime.</summary>
    public MarketSnapshot? RegimeFutures { get; init; }

    /// <summary>Live futures order-book, aggressive-flow and velocity state at evaluation time.</summary>
    public AdaptiveRollingMarketDataSnapshot? Microstructure { get; set; }

    public decimal SpotClose { get; init; }
    public decimal FuturesClose { get; init; }

    /// <summary>(FuturesClose - SpotClose) / SpotClose * 100 on the aligned closed candle.</summary>
    public decimal BasisPercent { get; init; }

    /// <summary>Latest funding rate from /fapi/v1/premiumIndex; null when unavailable.</summary>
    public decimal? FundingRate { get; init; }

    /// <summary>Current futures mark price from /fapi/v1/premiumIndex; null when unavailable.</summary>
    public decimal? MarkPrice { get; init; }
}

/// <summary>Full decision output of one closed-candle evaluation, including diagnostics.</summary>
public sealed class CrossMarketDecision
{
    public CrossMarketAction Action { get; init; } = CrossMarketAction.NoTrade;
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Full long/short entry gate state. Null for position-management decisions where entry
    /// qualification is intentionally not evaluated.
    /// </summary>
    public CrossMarketEntryGateTrace? EntryGateTrace { get; init; }

    public TrendState SpotTrendState { get; init; }
    public int SpotTrendConfidenceScore { get; init; }
    public decimal SpotShortMaSlopePercent { get; init; }
    public decimal SpotTrendStrengthPercent { get; init; }
    public decimal SpotMomentumPercent { get; init; }

    public TrendState FuturesTrendState { get; init; }
    public int FuturesTrendConfidenceScore { get; init; }
    public decimal FuturesShortMaSlopePercent { get; init; }
    public decimal FuturesTrendStrengthPercent { get; init; }

    /// <summary>Normalized futures ATR as percent of the futures close.</summary>
    public decimal FuturesAtrPercent { get; init; }

    /// <summary>Take-profit distance in percent (ATR-projected); used by the fee/expectancy gate.</summary>
    public decimal ExpectedMovePercent { get; init; }

    /// <summary>Proposed protective levels for an entry (null for NoTrade/close decisions).</summary>
    public decimal? StopLossPrice { get; init; }
    public decimal? TakeProfitPrice { get; init; }

    public TradeExecutionIntent ToExecutionIntent() => Action switch
    {
        CrossMarketAction.OpenLong => TradeExecutionIntent.OpenLong,
        CrossMarketAction.OpenShort => TradeExecutionIntent.OpenShort,
        CrossMarketAction.CloseLong => TradeExecutionIntent.CloseLong,
        CrossMarketAction.CloseShort => TradeExecutionIntent.CloseShort,
        _ => TradeExecutionIntent.None
    };
}
