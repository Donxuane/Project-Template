namespace TradingBot.Domain.Models.Decision;

public sealed class RiskEvaluationResult
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
}
