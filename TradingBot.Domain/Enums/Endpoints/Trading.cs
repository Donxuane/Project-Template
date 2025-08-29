namespace TradingBot.Domain.Enums.Endpoints;

public enum Trading
{
    NewOrder,
    QueryOrder,
    CancelOrder,
    CancelAllOrders,
    CancelReplaceOrder,
    OrderAmendKeepPriority,
    NewOrderList_OCO,
    CancelOrderList,
    NewOrderList_OTO,
    NewOrderList_OTOCO
}
