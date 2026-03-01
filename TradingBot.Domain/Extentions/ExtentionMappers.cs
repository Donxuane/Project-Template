using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Domain.Extentions;

public static class ExtentionMappers
{
    public static OrderStatuses ToOrderStatus(this string status) =>
        Enum.TryParse<OrderStatuses>(status, true, out var result)
            ? result
            : OrderStatuses.NEW;

    public static decimal ToDecimal(this string stringDecimal) =>
        decimal.TryParse(stringDecimal, out var value) 
        ? value 
        : 0;
}
