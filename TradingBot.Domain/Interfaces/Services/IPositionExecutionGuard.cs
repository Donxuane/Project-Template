using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Interfaces.Services;

public interface IPositionExecutionGuard
{
    Task<PositionExecutionGuardResult> EvaluateAsync(PositionExecutionGuardRequest request, CancellationToken cancellationToken = default);
}

public sealed class PositionExecutionGuardRequest
{
    public TradingSymbol Symbol { get; init; }
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public TradeSignal RawSignal { get; init; } = TradeSignal.Hold;
    public TradeExecutionIntent ExecutionIntent { get; init; } = TradeExecutionIntent.None;
    public OrderSide RequestedSide { get; init; }
    public decimal RequestedQuantity { get; init; }
    public bool IsProtectiveExit { get; init; }
}

public sealed class PositionExecutionGuardResult
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal OpenPositionQuantity { get; init; }
}
