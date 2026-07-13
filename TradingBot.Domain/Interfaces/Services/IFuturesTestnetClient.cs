using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Interfaces.Services;

/// <summary>
/// Minimal signed client for the Binance Futures Testnet (USD-M, /fapi). Used only by the
/// ETH15 testnet-validation execution path. Bound exclusively to the testnet base URL and
/// testnet keys; it cannot reach the live Spot client or production endpoints.
/// </summary>
public interface IFuturesTestnetClient
{
    Task EnsureLeverageAsync(string symbol, int leverage, CancellationToken cancellationToken = default);

    Task<decimal> GetMarkPriceAsync(string symbol, CancellationToken cancellationToken = default);

    Task<FuturesTestnetOrderResult> PlaceMarketOrderAsync(
        string symbol,
        OrderSide side,
        decimal quantity,
        bool reduceOnly,
        CancellationToken cancellationToken = default,
        string? positionSide = null);

    Task<IReadOnlyList<FuturesTestnetUserTrade>> GetUserTradesAsync(
        string symbol,
        long orderId,
        CancellationToken cancellationToken = default);

    /// <summary>Re-queries an order (GET /fapi/v1/order) to get authoritative avgPrice/executedQty after placement.</summary>
    Task<FuturesTestnetOrderResult> GetOrderAsync(
        string symbol,
        long orderId,
        CancellationToken cancellationToken = default);

    /// <summary>USD-M futures klines (public /fapi/v1/klines on the testnet host).</summary>
    Task<IReadOnlyList<FuturesTestnetKline>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>Mark price + funding data (public /fapi/v1/premiumIndex on the testnet host).</summary>
    Task<FuturesTestnetPremiumIndex> GetPremiumIndexAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Quantity/notional trading filters for a symbol (public /fapi/v1/exchangeInfo on the testnet host).</summary>
    Task<FuturesTestnetSymbolFilters> GetSymbolFiltersAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Wallet + available balance for an asset (signed GET /fapi/v2/balance). Used for balance-based sizing.</summary>
    Task<FuturesTestnetBalance> GetBalanceAsync(string asset, CancellationToken cancellationToken = default);

    /// <summary>Account-specific USD-M futures commission rate (signed GET /fapi/v1/commissionRate).</summary>
    Task<FuturesTestnetCommissionRate> GetCommissionRateAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Exchange-confirmed open futures position for the symbol (signed GET /fapi/v2/positionRisk).</summary>
    Task<FuturesTestnetPositionRisk?> GetPositionRiskAsync(string symbol, CancellationToken cancellationToken = default);

    /// <summary>Signed futures income rows, used sparingly for known funding credits/debits.</summary>
    Task<IReadOnlyList<FuturesTestnetIncome>> GetIncomeAsync(
        string symbol,
        string incomeType,
        DateTime startTimeUtc,
        DateTime endTimeUtc,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed class FuturesTestnetOrderResult
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ExecutedQty { get; set; }
    public decimal AvgPrice { get; set; }
    public decimal CumQuote { get; set; }
    public long UpdateTimeMs { get; set; }
}

public sealed class FuturesTestnetUserTrade
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Qty { get; set; }
    public decimal QuoteQty { get; set; }
    public decimal Commission { get; set; }
    public string CommissionAsset { get; set; } = string.Empty;
    public long TimeMs { get; set; }
}

public sealed class FuturesTestnetKline
{
    public DateTime OpenTimeUtc { get; set; }
    public DateTime CloseTimeUtc { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public decimal QuoteVolume { get; set; }
    public long TradeCount { get; set; }
    public decimal TakerBuyBaseVolume { get; set; }
}

public sealed class FuturesTestnetPremiumIndex
{
    public string Symbol { get; set; } = string.Empty;
    public decimal MarkPrice { get; set; }
    public decimal IndexPrice { get; set; }
    public decimal LastFundingRate { get; set; }
    public DateTime NextFundingTimeUtc { get; set; }
}

public sealed class FuturesTestnetSymbolFilters
{
    public string Symbol { get; set; } = string.Empty;
    public decimal QuantityStepSize { get; set; }
    public decimal MinQuantity { get; set; }
    public decimal MinNotional { get; set; }
    public decimal PriceTickSize { get; set; }
}

public sealed class FuturesTestnetBalance
{
    public string Asset { get; set; } = string.Empty;
    /// <summary>Wallet balance (excludes unrealized PnL).</summary>
    public decimal WalletBalance { get; set; }
    /// <summary>Balance available for opening new positions.</summary>
    public decimal AvailableBalance { get; set; }
    public decimal CrossUnrealizedPnl { get; set; }
}

public sealed class FuturesTestnetCommissionRate
{
    public string Symbol { get; set; } = string.Empty;
    public decimal MakerCommissionRate { get; set; }
    public decimal TakerCommissionRate { get; set; }
    public decimal? RpiCommissionRate { get; set; }
}

public sealed class FuturesTestnetPositionRisk
{
    public string Symbol { get; set; } = string.Empty;
    public decimal PositionAmt { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal MarkPrice { get; set; }
    public decimal UnrealizedProfit { get; set; }
    public string PositionSide { get; set; } = string.Empty;
    public long UpdateTimeMs { get; set; }
}

public sealed class FuturesTestnetIncome
{
    public string Symbol { get; set; } = string.Empty;
    public string IncomeType { get; set; } = string.Empty;
    public decimal Income { get; set; }
    public string Asset { get; set; } = string.Empty;
    public long TimeMs { get; set; }
    public string? Info { get; set; }
}
