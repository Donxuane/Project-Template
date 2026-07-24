namespace TradingBot.Domain.Enums;

/// <summary>
/// Order states used by the Spot/Futures close-order guard.
/// </summary>
public enum ProcessingStatus
{
    PositionUpdated = 22,
    Completed = 100
}
