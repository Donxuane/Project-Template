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
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FuturesTestnetUserTrade>> GetUserTradesAsync(
        string symbol,
        long orderId,
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
