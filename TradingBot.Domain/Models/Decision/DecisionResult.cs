using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class DecisionResult
{
    public string StrategyName { get; init; } = string.Empty;
    public TradeSignal Action { get; init; }
    public TradeSignal RawSignal { get; init; }
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public TradeExecutionIntent ExecutionIntent { get; init; } = TradeExecutionIntent.None;
    public string Reason { get; init; } = string.Empty;
    public TradeCandidate? Candidate { get; init; }
    public decimal Confidence { get; init; } = 1.0m;
}
