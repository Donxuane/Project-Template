using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Decision;

public sealed class TradeCandidate
{
    public TradingSymbol Symbol { get; init; }
    public OrderSide Side { get; init; }
    public decimal Quantity { get; init; }
    public decimal? Price { get; init; }
}
