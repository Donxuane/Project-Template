using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Trading;

public class BalanceSnapshot
{
    public long Id { get; set; }
    public string Asset { get; set; } = string.Empty;
    public Assets Symbol { get; set; }
    public OrderSide Side { get; set; }
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total => Free + Locked;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } 
}

