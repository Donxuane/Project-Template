using System.Globalization;

namespace TradingBot.Domain.Utilities;

public static class BinanceDecimalFormatter
{
    private const string NonScientificFormat = "0.############################";

    public static string FormatDecimal(decimal value)
    {
        if (value == 0m)
            return "0";

        var formatted = value.ToString(NonScientificFormat, CultureInfo.InvariantCulture);
        return formatted == "-0" ? "0" : formatted;
    }

    public static string FormatQuantity(decimal value) => FormatDecimal(value);

    public static string FormatPrice(decimal value) => FormatDecimal(value);
}
