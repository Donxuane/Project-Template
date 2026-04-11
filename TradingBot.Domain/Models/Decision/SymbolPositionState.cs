using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class SymbolPositionState
{
    public bool IsInPosition { get; init; }
    public PositionType PositionType { get; init; } = PositionType.None;
    public decimal EntryPrice { get; init; }
    public DateTime EntryTimeUtc { get; init; } = DateTime.MinValue;
    public TrendState TrendState { get; init; } = TrendState.Neutral;
}
