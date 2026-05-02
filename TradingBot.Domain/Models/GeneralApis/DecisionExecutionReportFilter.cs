using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.GeneralApis;

public class DecisionExecutionReportFilter
{
    public TradingSymbol? Symbol { get; set; }
    public DateTimeOffset? FromDateUtc { get; set; }
    public DateTimeOffset? ToDateUtc { get; set; }
    public string? StrategyName { get; set; }
    public TradingMode? TradingMode { get; set; }
    public TradeExecutionIntent? ExecutionIntent { get; set; }
    public bool? OnlyExecuted { get; set; }
    public bool? OnlySkipped { get; set; }
    public bool? OnlyFailed { get; set; }
    public bool? OnlyCooldownBlocked { get; set; }
    public bool? OnlyIdempotencyDuplicates { get; set; }
    public GuardStage? BlockedBy { get; set; }
    public OrderSource? OrderSource { get; set; }
    public CloseReason? CloseReason { get; set; }
}
