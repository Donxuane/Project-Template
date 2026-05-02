using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Domain.Models.Binance;

public sealed class BinanceOrderNormalizationResult
{
    public required BinanceSymbolFilters Filters { get; init; }
    public required NewOrderRequest Request { get; init; }
    public decimal? OriginalQuantity { get; init; }
    public decimal? NormalizedQuantity { get; init; }
    public decimal? OriginalPrice { get; init; }
    public decimal? NormalizedPrice { get; init; }
    public decimal? EffectivePrice { get; init; }
    public decimal? Notional { get; init; }
}
