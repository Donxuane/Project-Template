using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Interfaces.Services;

public interface IConfidenceGate
{
    Task<ConfidenceGateResult> EvaluateAsync(ConfidenceGateRequest request, CancellationToken cancellationToken = default);
}

public sealed class ConfidenceGateRequest
{
    public string StrategyName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public TradeSignal Action { get; init; } = TradeSignal.Hold;
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public TradeExecutionIntent ExecutionIntent { get; init; } = TradeExecutionIntent.None;
    public decimal Confidence { get; init; }
}

public sealed class ConfidenceGateResult
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string StrategyName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public TradeSignal Action { get; init; } = TradeSignal.Hold;
    public TradeExecutionIntent ExecutionIntent { get; init; } = TradeExecutionIntent.None;
    public decimal Confidence { get; init; }
    public decimal MinConfidence { get; init; }
}
