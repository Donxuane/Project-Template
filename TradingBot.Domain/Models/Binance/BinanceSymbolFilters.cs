namespace TradingBot.Domain.Models.Binance;

public sealed class BinanceSymbolFilters
{
    public required string Symbol { get; init; }
    public decimal? StepSize { get; init; }
    public decimal? MinQty { get; init; }
    public decimal? MaxQty { get; init; }
    public decimal? TickSize { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public decimal? MinNotional { get; init; }
    public decimal? MaxNotional { get; init; }
}
