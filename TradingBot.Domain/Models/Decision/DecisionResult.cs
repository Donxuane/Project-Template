using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class DecisionResult
{
    public TradeSignal Action { get; init; }
    public string Reason { get; init; } = string.Empty;
    public TradeCandidate? Candidate { get; init; }
    public decimal Confidence { get; init; } = 1.0m;
}
