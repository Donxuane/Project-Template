using MediatR;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.Trading.Commands;

public record PlaceSpotOrderCommand(
    TradingSymbol Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal? Price,
    bool IsLimitOrder,
    OrderSource OrderSource = OrderSource.Unknown,
    CloseReason CloseReason = CloseReason.None,
    long? ParentPositionId = null,
    string? CorrelationId = null,
    decimal? CandidatePrice = null,
    TradingMode? TradingMode = null,
    TradeExecutionIntent? ExecutionIntent = null,
    TradeSignal? RawSignal = null,
    bool? RequiresReducedPositionSize = null,
    decimal? ExpectedTargetPrice = null,
    decimal? ExpectedMovePercent = null,
    string? ExpectedTargetSource = null,
    decimal? BreakoutRangeHigh = null,
    decimal? BreakoutRangeLow = null,
    decimal? BreakoutThresholdPrice = null,
    decimal? ExpectedTargetStructureExtensionUsed = null,
    decimal? ExpectedTargetAtrUsed = null
) : IRequest<PlaceSpotOrderResult>;

public sealed class PlaceSpotOrderResult
{
    public Order? Order { get; init; }
    public bool Success { get; init; }
    public string? Error { get; init; }
}

