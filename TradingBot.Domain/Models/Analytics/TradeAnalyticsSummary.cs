namespace TradingBot.Domain.Models.Analytics;

public sealed class TradeAnalyticsSummary
{
    public decimal TotalPnl { get; init; }
    public decimal WinRate { get; init; }
    public decimal AverageWin { get; init; }
    public decimal AverageLoss { get; init; }
    public int TotalTrades { get; init; }
    public decimal MaxDrawdown { get; init; }
}
