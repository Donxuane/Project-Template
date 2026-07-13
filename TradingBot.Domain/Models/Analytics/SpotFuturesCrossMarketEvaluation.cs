using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Analytics;

/// <summary>
/// Full per-closed-candle evaluation state of the SpotFuturesCrossMarketTestnetV1 strategy:
/// the synchronized Spot and USD-M Futures indicator snapshots, the cross-market context,
/// and the resulting decision. One row is persisted for every fully closed candle the
/// strategy evaluates, including NoTrade cycles, so the entire strategy state history is
/// reconstructable from the database (mirroring how the live Spot path snapshots state
/// into trade_execution_decisions).
/// </summary>
public sealed class SpotFuturesCrossMarketEvaluation
{
    public long Id { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public TradingSymbol Symbol { get; set; }
    public string Interval { get; set; } = string.Empty;

    /// <summary>Open time (UTC) of the fully closed candle this evaluation is based on.</summary>
    public DateTime CandleOpenTimeUtc { get; set; }

    /// <summary>Close time (UTC) of the fully closed candle this evaluation is based on.</summary>
    public DateTime CandleCloseTimeUtc { get; set; }

    // Spot market state (leading context).
    public decimal SpotClose { get; set; }
    public TrendState SpotTrendState { get; set; }
    public int SpotTrendConfidenceScore { get; set; }
    public decimal SpotShortMaSlopePercent { get; set; }
    public decimal SpotTrendStrengthPercent { get; set; }
    public decimal SpotMomentumPercent { get; set; }

    // Futures market state (confirmation + traded instrument).
    public decimal FuturesClose { get; set; }
    public TrendState FuturesTrendState { get; set; }
    public int FuturesTrendConfidenceScore { get; set; }
    public decimal FuturesShortMaSlopePercent { get; set; }
    public decimal FuturesTrendStrengthPercent { get; set; }
    public decimal FuturesAtrPercent { get; set; }

    // Cross-market context.
    public decimal BasisPercent { get; set; }
    public decimal? FundingRate { get; set; }
    public decimal? MarkPrice { get; set; }
    public bool MarketsInSync { get; set; }

    // Decision output.
    public TradeExecutionIntent DecidedIntent { get; set; }

    /// <summary>OpenLong / OpenShort / CloseLong / CloseShort / NoTrade.</summary>
    public string DecisionLabel { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public bool Executed { get; set; }
    public long? PositionId { get; set; }
    public long? LocalOrderId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
