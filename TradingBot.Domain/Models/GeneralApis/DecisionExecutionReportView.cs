using System.Text.Json.Serialization;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Domain.Models.GeneralApis;

public sealed class DecisionExecutionReportView
{
    public long DecisionDbId { get; set; }
    public string? DecisionId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonIgnore]
    public TradingSymbol? Symbol { get; set; }
    public BaseModelDto? SymbolModel => Symbol?.ToModel();

    [JsonIgnore]
    public TradeSignal? DecisionAction { get; set; }
    public BaseModelDto? DecisionActionModel => DecisionAction?.ToModel();

    [JsonIgnore]
    public OrderSide? Side { get; set; }
    public BaseModelDto? SideModel => Side?.ToModel();

    [JsonIgnore]
    public TradeSignal? RawSignal { get; set; }
    public BaseModelDto? RawSignalModel => RawSignal?.ToModel();

    [JsonIgnore]
    public TradingMode? TradingMode { get; set; }
    public BaseModelDto? TradingModeModel => TradingMode?.ToModel();

    [JsonIgnore]
    public TradeExecutionIntent? ExecutionIntent { get; set; }
    public BaseModelDto? ExecutionIntentModel => ExecutionIntent?.ToModel();

    public string? StrategyName { get; set; }
    public decimal? Confidence { get; set; }
    public decimal? MinConfidence { get; set; }
    public string? Reason { get; set; }

    [JsonIgnore]
    public DecisionStatus? DecisionStatus { get; set; }
    public BaseModelDto? DecisionStatusModel => DecisionStatus?.ToModel();

    [JsonIgnore]
    public GuardStage? GuardStage { get; set; }
    public BaseModelDto? GuardStageModel => GuardStage?.ToModel();

    public bool? IsInCooldown { get; set; }
    public long? CooldownRemainingSeconds { get; set; }
    public DateTimeOffset? CooldownLastTrade { get; set; }
    public bool? IdempotencyDuplicate { get; set; }
    public bool? RiskIsAllowed { get; set; }
    public string? RiskReason { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public bool? ExecutionSuccess { get; set; }
    public string? ExecutionError { get; set; }

    public long? LocalOrderId { get; set; }
    public long? ExchangeOrderId { get; set; }

    [JsonIgnore]
    public OrderStatuses? OrderStatus { get; set; }
    public BaseModelDto? OrderStatusModel => OrderStatus?.ToModel();

    [JsonIgnore]
    public ProcessingStatus? ProcessingStatus { get; set; }
    public BaseModelDto? ProcessingStatusModel => ProcessingStatus?.ToModel();

    [JsonIgnore]
    public OrderSource? OrderSource { get; set; }
    public BaseModelDto? OrderSourceModel => OrderSource?.ToModel();

    [JsonIgnore]
    public CloseReason? CloseReason { get; set; }
    public BaseModelDto? CloseReasonModel => CloseReason?.ToModel();

    public long? ParentPositionId { get; set; }
    public string? OrderCorrelationId { get; set; }

    public long? ExchangeTradeId { get; set; }
    public DateTimeOffset? ExecutedAt { get; set; }
    public decimal? ExecutionPrice { get; set; }
    public decimal? ExecutedQuantity { get; set; }
    public decimal? Fees { get; set; }

    public long? PositionId { get; set; }
    public decimal? RealizedPnl { get; set; }
    public decimal? UnrealizedPnl { get; set; }

    [JsonIgnore]
    public PositionExitReason? ExitReason { get; set; }
    public BaseModelDto? ExitReasonModel => ExitReason?.ToModel();
    public bool? IsOpen { get; set; }
}
