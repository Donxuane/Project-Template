using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Trading;

public class BalanceSnapshot
{
    public long Id { get; set; }
    public string Asset { get; set; } = string.Empty;
    /// <summary>
    /// Maps to balance_snapshots.symbol in PostgreSQL.
    /// Despite the legacy column name, this is an Assets enum value (asset id), not TradingSymbol.
    /// Balance snapshot lookup/write code should always use AssetId semantics.
    /// </summary>
    public Assets AssetId { get; set; }

    [Obsolete("Use AssetId. Legacy alias kept for compatibility with existing callers.")]
    public Assets Symbol
    {
        get => AssetId;
        set => AssetId = value;
    }

    public OrderSide Side { get; set; }
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total => Free + Locked;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } 
}

