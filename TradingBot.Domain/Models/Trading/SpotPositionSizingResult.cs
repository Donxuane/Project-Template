using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Trading;

public sealed class SpotPositionSizingResult
{
    public bool IsSuccess { get; init; }
    public string? Reason { get; init; }
    public decimal Quantity { get; init; }
    public SpotQuantitySource QuantitySource { get; init; }
    public decimal? AvailableQuoteBalance { get; init; }
    public decimal? ReservedQuoteBalance { get; init; }
    public decimal? UsableQuoteBalance { get; init; }
    public decimal? QuoteAllocationPercentPerTrade { get; init; }
    public decimal? DesiredQuoteAmount { get; init; }
    public decimal? CappedQuoteAmount { get; init; }
    public decimal? CurrentPrice { get; init; }
    public decimal? RawQuantity { get; init; }
    public decimal? NormalizedQuantity { get; init; }
    public decimal? FinalNotional { get; init; }
    public decimal? MinNotional { get; init; }
}

public sealed class SpotPositionSizingRequest
{
    public required TradingSymbol Symbol { get; init; }
    public required decimal GlobalQuantity { get; init; }
    public required IReadOnlyDictionary<TradingSymbol, decimal> SymbolQuantities { get; init; }
}

public sealed class SpotMinNotionalValidationResult
{
    public bool IsValid { get; init; }
    public string? Reason { get; init; }
    public decimal Quantity { get; init; }
    public decimal Price { get; init; }
    public decimal Notional { get; init; }
    public decimal? MinNotional { get; init; }
}
