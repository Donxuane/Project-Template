using System.Text.Json.Serialization;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Shared.Shared.Models;

namespace TradingBot.Domain.Models.GeneralApis;

public sealed class OrderTradeExecutionView
{
    public long? ExchangeOrderId { get; set; }
    public string? CorrelationId { get; set; }
    public long? ParentPositionId { get; set; }
    [JsonIgnore]
    public OrderSource? OrderSource { get; set; }
    public BaseModelDto? OrderSourceModel
    {
        get
        {
            if (OrderSource == null)
            {
                return null;
            }
            return OrderSource.Value.ToModel();
        }
    }
    [JsonIgnore]
    public CloseReason? CloseReason { get; set; }
    public BaseModelDto? CloseReasonModel
    {
        get
        {
            if (CloseReason == null)
            {
                return null;
            }
            return CloseReason.Value.ToModel();
        }
    }
    public OrderStatuses? Status { get; set; }
    [JsonIgnore]
    public ProcessingStatus? ProcessingStatus { get; set; }
    public BaseModelDto? ProcessingStatusModel { get 
        {
        if(ProcessingStatus == null)
            {
                return null;
            }
            return ProcessingStatus.Value.ToModel();
        } 
    }
    public long? ExchangeTradeId { get; set; }
    [JsonIgnore]
    public TradingSymbol? Symbol { get; set; }
    public BaseModelDto? SymbolModel
    {
        get
        {
            if (Symbol == null)
            {
                return null;
            }
            return Symbol.Value.ToModel();
        }
    }
    public decimal? Price { get; set; }
    public decimal? Quantity { get; set; }
    [JsonIgnore]
    public OrderSide? Side { get; set; }
    public BaseModelDto? SideModel
    {
        get
        {
            if (Side == null)
            {
                return null;
            }
            return Side.Value.ToModel();
        }
    }
    public DateTimeOffset? ExecutedAt { get; set; }
    [JsonIgnore]
    public TradeSignal? DesicionAction { get; set; }
    public BaseModelDto? DesicionActionModel
    {
        get
        {
            if (DesicionAction == null)
            {
                return null;
            }
            return DesicionAction.Value.ToModel();
        }
    }
    public decimal? Confidence { get; set; }
    public decimal? MinConfidence { get; set; }
    public bool? IsInCooldown { get; set; }
    public long? CooldownRemainingSeconds { get; set; }
    public bool? RiskIsAllowed { get; set; }
    public string? RiskReason { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public bool? ExecutionSuccess { get; set; }
    public string? ExecutionError { get; set; }
}
