using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Extensions;

public static class OrderStatusExtensions
{
    public static OrderStatuses ToOrderStatus(this string status) =>
        Enum.TryParse<OrderStatuses>(status, true, out var result)
            ? result
            : OrderStatuses.NEW;
}
