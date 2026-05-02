using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Decision;

public sealed class TradeCandidate
{
    public TradingSymbol Symbol { get; init; }
    public OrderSide Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal? Price { get; init; }
    public bool RequiresReducedPositionSize { get; init; }
    public TradingMode TradingMode { get; init; } = TradingMode.Spot;
    public TradeSignal RawSignal { get; init; }
    public TradeExecutionIntent ExecutionIntent { get; init; } = TradeExecutionIntent.None;
}
