using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Trading;

public sealed class FuturesCommissionRate
{
    public long Id { get; set; }
    public string ApplicationId { get; set; } = string.Empty;
    public string ExecutionEnvironment { get; set; } = string.Empty;
    public string AccountKey { get; set; } = string.Empty;
    public TradingSymbol Symbol { get; set; }
    public decimal MakerCommissionRate { get; set; }
    public decimal TakerCommissionRate { get; set; }
    public decimal? RpiCommissionRate { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool IsFallback { get; set; }
    public string? FallbackReason { get; set; }
    public DateTime FetchedAtUtc { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdaptiveRollingProfitExitStateRecord
{
    public long PositionId { get; set; }
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public AdaptiveRollingProfitExitState State { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal EntryNotional { get; set; }
    public decimal? OriginalStopLossPrice { get; set; }
    public decimal? OriginalTakeProfitPrice { get; set; }
    public decimal? CurrentStopLossPrice { get; set; }
    public decimal? CurrentTakeProfitPrice { get; set; }
    public DateTime? OriginalMaxHoldUntilUtc { get; set; }
    public DateTime? EligibleSinceUtc { get; set; }
    public int ConsecutiveProfitableObservations { get; set; }
    public DateTime? ArmedAtUtc { get; set; }
    public decimal? ArmingExecutablePrice { get; set; }
    public decimal? ArmingProjectedNetPnl { get; set; }
    public string? ArmingFeeSnapshotJson { get; set; }
    public string? ArmingTrendFlowSnapshotJson { get; set; }
    public decimal PeakProjectedNetPnl { get; set; }
    public decimal? BestExecutablePrice { get; set; }
    public DateTime? PeakUpdatedAtUtc { get; set; }
    public DateTime? LastPeakPersistedAtUtc { get; set; }
    public decimal LastProjectedNetPnl { get; set; }
    public decimal LastGrossProjectedPnl { get; set; }
    public decimal LastEstimatedExitFee { get; set; }
    public decimal LastActualEntryFee { get; set; }
    public decimal LastFunding { get; set; }
    public decimal LastAdverseMoveReserve { get; set; }
    public decimal LastSpreadBps { get; set; }
    public decimal LastEstimatedSlippageBps { get; set; }
    public decimal LastTrendFlowScore { get; set; }
    public string? LastFeeSource { get; set; }
    public long? LastFeeAgeSeconds { get; set; }
    public string? LastDecision { get; set; }
    public string? LastRejectionReason { get; set; }
    public DateTime? LastEvaluatedAtUtc { get; set; }
    public DateTime? LastTransitionAtUtc { get; set; }
    public string? CloseCorrelationId { get; set; }
    public long? CloseLocalOrderId { get; set; }
    public long? CloseExchangeOrderId { get; set; }
    public DateTime? CloseSubmittedAtUtc { get; set; }
    public DateTime? CloseAcknowledgedAtUtc { get; set; }
    public DateTime? CloseFilledAtUtc { get; set; }
    public decimal? ActualRealizedGrossPnl { get; set; }
    public decimal? ActualRealizedNetPnl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AdaptiveRollingProfitExitEvaluationRecord
{
    public long Id { get; set; }
    public long PositionId { get; set; }
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public AdaptiveRollingProfitExitState State { get; set; }
    public decimal RemainingQuantity { get; set; }
    public decimal EstimatedExecutablePrice { get; set; }
    public decimal GrossProjectedPnl { get; set; }
    public decimal ActualEntryCommissions { get; set; }
    public decimal EstimatedExitCommission { get; set; }
    public decimal Funding { get; set; }
    public decimal AdverseMoveReserve { get; set; }
    public decimal ProjectedNetPnl { get; set; }
    public decimal BreakEvenExecutablePrice { get; set; }
    public decimal PeakProjectedNetPnl { get; set; }
    public decimal GivebackAmount { get; set; }
    public decimal GivebackPercent { get; set; }
    public decimal SpreadBps { get; set; }
    public decimal EstimatedSlippageBps { get; set; }
    public decimal TopBidNotional { get; set; }
    public decimal TopAskNotional { get; set; }
    public decimal OrderBookImbalance { get; set; }
    public decimal Microprice { get; set; }
    public decimal AggressiveBuyQuantity { get; set; }
    public decimal AggressiveSellQuantity { get; set; }
    public decimal AggressiveFlowImbalance { get; set; }
    public decimal NormalizedVelocityBps { get; set; }
    public decimal RealizedVolatilityBps { get; set; }
    public decimal TrendFlowScore { get; set; }
    public DateTime? MarketDataEventTimeUtc { get; set; }
    public DateTime? MarketDataTransactionTimeUtc { get; set; }
    public DateTime? MarketDataLocalReceiptUtc { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
    public long MarketDataAgeMs { get; set; }
    public long StreamLatencyMs { get; set; }
    public bool IsMarketDataFresh { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? RejectionReason { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class AdaptiveRollingProfitCounterfactualRecord
{
    public long PositionId { get; set; }
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal? OriginalStopLossPrice { get; set; }
    public decimal? OriginalTakeProfitPrice { get; set; }
    public DateTime? OriginalMaxHoldUntilUtc { get; set; }
    public decimal ActualRollingExitPrice { get; set; }
    public decimal ActualRollingNetPnl { get; set; }
    public DateTime ActualRollingClosedAtUtc { get; set; }
    public decimal MaxAdditionalFavorableMove { get; set; }
    public decimal MaxAvoidedAdverseMove { get; set; }
    public decimal? CounterfactualExitPrice { get; set; }
    public decimal? CounterfactualNetPnl { get; set; }
    public string? CounterfactualExitReason { get; set; }
    public string? BetterExitMethod { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
