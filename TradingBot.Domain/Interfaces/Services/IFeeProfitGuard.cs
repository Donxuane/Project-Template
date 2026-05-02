using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Interfaces.Services;

public interface IFeeProfitGuard
{
    Task<FeeProfitGuardResult> EvaluateAsync(FeeProfitGuardRequest request, CancellationToken cancellationToken = default);
}

public sealed class FeeProfitGuardRequest
{
    public TradingSymbol Symbol { get; init; }
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public TradeSignal RawSignal { get; init; } = TradeSignal.Hold;
    public TradeExecutionIntent ExecutionIntent { get; init; } = TradeExecutionIntent.None;
    public OrderSide Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal? EntryPrice { get; init; }
    public decimal? TargetPrice { get; init; }
    public decimal? StopLossPrice { get; init; }
    public bool IsProtectiveExit { get; init; }
}

public sealed class FeeProfitGuardResult
{
    public bool IsAllowed { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal EntryPrice { get; init; }
    public decimal TargetPrice { get; init; }
    public decimal? StopLossPrice { get; init; }
    public decimal GrossExpectedProfitPercent { get; init; }
    public decimal EstimatedEntryFeePercent { get; init; }
    public decimal EstimatedExitFeePercent { get; init; }
    public decimal EstimatedSpreadPercent { get; init; }
    public decimal EstimatedTotalCostPercent { get; init; }
    public decimal NetExpectedProfitPercent { get; init; }
}
