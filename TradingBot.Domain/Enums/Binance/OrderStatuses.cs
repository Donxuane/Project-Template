namespace TradingBot.Domain.Enums.Binance;

public enum OrderStatuses
{
    NEW,
    PARTIALLY_FILLED, 
    FILLED,
    CANCELED, 
    REJECTED, 
    EXPIRED,
    EXPIRED_IN_MATCH,
    PENDING_CANCEL,
    PENDING_NEW
}
