using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Domain.Utilities;
using TradingBot.Percistance.Services.Main;
using Xunit;

namespace TradingBot.Application.Tests;

public class BinanceDecimalFormatterTests
{
    [Fact]
    public void FormatDecimal_RemovesTrailingZeros()
    {
        Assert.Equal("0.0001", BinanceDecimalFormatter.FormatDecimal(0.000100000000000000m));
        Assert.Equal("633.51", BinanceDecimalFormatter.FormatDecimal(633.51000000m));
        Assert.Equal("5", BinanceDecimalFormatter.FormatDecimal(5.00000000m));
        Assert.Equal("0", BinanceDecimalFormatter.FormatDecimal(0m));
    }

    [Fact]
    public void FormatDecimal_DoesNotUseScientificNotation()
    {
        var value = decimal.Parse("0.00000000000000000001", System.Globalization.CultureInfo.InvariantCulture);
        var result = BinanceDecimalFormatter.FormatDecimal(value);

        Assert.DoesNotContain("E", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0.00000000000000000001", result);
    }
}

public class BinanceOrderNormalizationTests
{
    private static readonly BinanceSymbolFilters BtcFilters = new()
    {
        Symbol = "BTCUSDT",
        StepSize = 0.00001m,
        MinQty = 0.00010m,
        MaxQty = 100m,
        TickSize = 0.01m,
        MinPrice = 0.01m,
        MaxPrice = 1_000_000m,
        MinNotional = 10m,
        MaxNotional = null
    };

    [Fact]
    public void LotSize_ExactStep_RemainsSame()
    {
        var request = CreateLimitOrder(quantity: 0.00010m, price: 100_000m);

        var result = BinanceOrderNormalizationService.NormalizeNewOrder(request, BtcFilters, marketPrice: null);

        Assert.Equal(0.00010m, result.Request.Quantity);
    }

    [Fact]
    public void LotSize_TrailingZeros_FormatsCorrectly()
    {
        var request = CreateLimitOrder(quantity: 0.000100000000000000m, price: 100_000m);

        var result = BinanceOrderNormalizationService.NormalizeNewOrder(request, BtcFilters, marketPrice: null);

        Assert.Equal("0.0001", BinanceDecimalFormatter.FormatQuantity(result.Request.Quantity!.Value));
    }

    [Fact]
    public void LotSize_ExtraPrecision_FloorsDown()
    {
        var request = CreateLimitOrder(quantity: 0.000109m, price: 100_000m);

        var result = BinanceOrderNormalizationService.NormalizeNewOrder(request, BtcFilters, marketPrice: null);

        Assert.Equal(0.00010m, result.Request.Quantity);
    }

    [Fact]
    public void LotSize_BelowMinQty_FailsLocally()
    {
        var request = CreateLimitOrder(quantity: 0.00009m, price: 100_000m);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BinanceOrderNormalizationService.NormalizeNewOrder(request, BtcFilters, marketPrice: null));

        Assert.Contains("minQty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PriceFilter_LimitPrice_FloorsToTickSize()
    {
        var request = CreateLimitOrder(quantity: 0.02000m, price: 633.519m);

        var result = BinanceOrderNormalizationService.NormalizeNewOrder(request, BtcFilters, marketPrice: null);

        Assert.Equal(633.51m, result.Request.Price);
    }

    [Fact]
    public void PriceFilter_MarketOrder_DoesNotSendPrice()
    {
        var request = new NewOrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.BUY,
            Type = OrderTypes.MARKET,
            Quantity = 0.00011m,
            Price = 1m,
            TimeInForce = TimeInForce.GTC,
            Timestamp = 123
        };

        var result = BinanceOrderNormalizationService.NormalizeNewOrder(request, BtcFilters, marketPrice: 100_000m);

        Assert.Null(result.Request.Price);
        Assert.Null(result.Request.TimeInForce);
    }

    [Fact]
    public void PriceFilter_LimitOrder_RequiresPriceAndTimeInForce()
    {
        var request = new NewOrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.BUY,
            Type = OrderTypes.LIMIT,
            Quantity = 0.0002m,
            Timestamp = 123
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            BinanceOrderNormalizationService.NormalizeNewOrder(request, BtcFilters, marketPrice: null));

        Assert.Contains("price", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static NewOrderRequest CreateLimitOrder(decimal quantity, decimal price)
    {
        return new NewOrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.BUY,
            Type = OrderTypes.LIMIT,
            Quantity = quantity,
            Price = price,
            TimeInForce = TimeInForce.GTC,
            Timestamp = 123
        };
    }
}

public class BinanceRequestSerializationTests
{
    [Fact]
    public void MarketOrderQuery_DoesNotContainPriceOrTimeInForce()
    {
        var request = new NewOrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.BUY,
            Type = OrderTypes.MARKET,
            Quantity = 0.000100000000000000m,
            Price = 0m,
            TimeInForce = TimeInForce.GTC,
            Timestamp = 123
        };

        var dictionary = BinanceRequestQueryBuilder.BuildRequestDictionary(request);
        var query = BinanceRequestQueryBuilder.BuildQueryString(dictionary);

        Assert.DoesNotContain("price=", query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("timeInForce=", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quantity=0.0001", query, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LimitOrderQuery_ContainsPriceAndTimeInForce()
    {
        var request = new NewOrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.BUY,
            Type = OrderTypes.LIMIT,
            Quantity = 5.00000000m,
            Price = 633.51000000m,
            TimeInForce = TimeInForce.GTC,
            Timestamp = 123
        };

        var dictionary = BinanceRequestQueryBuilder.BuildRequestDictionary(request);
        var query = BinanceRequestQueryBuilder.BuildQueryString(dictionary);

        Assert.Contains("price=633.51", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timeInForce=GTC", query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quantity=5", query, StringComparison.OrdinalIgnoreCase);
    }
}
