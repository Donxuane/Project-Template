using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Models.Trading;

public class Position
{
    public long Id { get; set; }
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal EntryPrice
    {
        get => AveragePrice;
        set => AveragePrice = value;
    }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public PositionExitReason? ExitReason { get; set; }
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal RealizedPnl { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsOpen { get; set; }
    public bool IsClosing { get; set; }
}

