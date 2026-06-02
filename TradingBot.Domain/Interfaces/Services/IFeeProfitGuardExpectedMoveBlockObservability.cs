using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Interfaces.Services;

public interface IFeeProfitGuardExpectedMoveBlockObservability
{
    void RecordExpectedMoveBlock(FeeProfitGuardExpectedMoveBlockObservation observation);

    void FlushAndLog(
        decimal currentMinExpectedMovePercent,
        decimal currentMinNetProfitPercent,
        TimeSpan reportingWindow);
}

public sealed class FeeProfitGuardExpectedMoveBlockObservation
{
    public required TradingSymbol Symbol { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal ExpectedNetProfitPercent { get; init; }
    public string? ExpectedTargetSource { get; init; }
    public decimal? Confidence { get; init; }
    public required string RejectionReason { get; init; }
}

public sealed class FeeProfitGuardExpectedMoveBlockAggregate
{
    public required TradingSymbol Symbol { get; init; }
    public int TotalBlockedCandidates { get; init; }
    public decimal? AvgExpectedMovePercent { get; init; }
    public decimal? MinExpectedMovePercent { get; init; }
    public decimal? MaxExpectedMovePercent { get; init; }
    public decimal AvgExpectedNetProfitPercent { get; init; }
    public required string ExpectedTargetSourceBreakdown { get; init; }
    public decimal? AvgConfidence { get; init; }
    public required string RejectionReasonBreakdown { get; init; }
}
