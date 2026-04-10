namespace TradingBot.Domain.Enums;

/// <summary>
/// Internal workflow state of an order. Only forward transitions are allowed.
/// </summary>
public enum ProcessingStatus
{
    OrderPlaced = 1,
    TradesSyncPending = 10,
    TradesSyncInProgress = 11,
    TradesSynced = 12,
    TradesSyncFailed = 13,
    PositionUpdatePending = 20,
    PositionUpdating = 21,
    PositionUpdated = 22,
    PositionUpdateFailed = 23,
    Completed = 100
}
