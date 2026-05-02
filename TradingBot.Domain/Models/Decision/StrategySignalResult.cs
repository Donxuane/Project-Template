using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class StrategySignalResult
{
    public string StrategyName { get; init; } = string.Empty;
    public TradeSignal Signal { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal Confidence { get; init; } = 1.0m;
}
