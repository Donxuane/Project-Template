using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Interfaces.Services;

public interface ITradeExecutionService
{
    Task<TradeExecutionResult> ExecuteMarketOrderAsync(TradeExecutionRequest request, CancellationToken cancellationToken = default);
}

public sealed class TradeExecutionRequest
{
    public required string CorrelationId { get; init; }
    public required string DecisionId { get; init; }
    public TradingSymbol Symbol { get; init; }
    public OrderSide Side { get; init; }
    public decimal Quantity { get; init; }
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public TradeSignal RawSignal { get; init; } = TradeSignal.Hold;
    public TradeExecutionIntent ExecutionIntent { get; init; } = TradeExecutionIntent.None;
    public bool RequiresReducedPositionSize { get; init; }
    public decimal? CandidatePrice { get; init; }
    public decimal? ExpectedTargetPrice { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public decimal? BreakoutRangeHigh { get; init; }
    public decimal? BreakoutRangeLow { get; init; }
    public decimal? BreakoutThresholdPrice { get; init; }
    public decimal? ExpectedTargetStructureExtensionUsed { get; init; }
    public decimal? ExpectedTargetAtrUsed { get; init; }
}

public sealed class TradeExecutionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public long? LocalOrderId { get; init; }
    public long? ExchangeOrderId { get; init; }
}
