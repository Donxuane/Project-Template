using System.Reflection;
using TradingBot.Application.Trading;
using TradingBot.Domain.Enums.Binance;
using Xunit;

namespace TradingBot.Application.Tests;

public class PlaceSpotOrderSlippageTests
{
    [Fact]
    public void CalculateSlippagePercent_Buy_UsesExpectedFormula()
    {
        var method = typeof(PlaceSpotOrderCommandHandler).GetMethod(
            "CalculateSlippagePercent",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (decimal)method!.Invoke(null, [OrderSide.BUY, 100m, 101m])!;

        Assert.Equal(1m, result);
    }

    [Fact]
    public void CalculateSlippagePercent_Sell_UsesExpectedFormula()
    {
        var method = typeof(PlaceSpotOrderCommandHandler).GetMethod(
            "CalculateSlippagePercent",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = (decimal)method!.Invoke(null, [OrderSide.SELL, 100m, 99m])!;

        Assert.Equal(1m, result);
    }
}
