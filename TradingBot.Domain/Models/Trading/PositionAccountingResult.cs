namespace TradingBot.Domain.Models.Trading;

public sealed class PositionAccountingResult
{
    public required Position Position { get; init; }
    public decimal RealizedPnlDelta { get; init; }
    public decimal FeeDelta { get; init; }
    public int ProcessedTradeCount { get; init; }
    public bool PositionOpened { get; init; }
    public bool PositionClosed { get; init; }
    public bool PositionFlipped { get; init; }
    public string Reason { get; init; } = string.Empty;
}
