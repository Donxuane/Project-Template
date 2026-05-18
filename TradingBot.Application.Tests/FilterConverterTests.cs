using System.Text.Json;
using TradingBot.Domain.Models;
using TradingBot.Domain.Models.GeneralApis;
using Xunit;

namespace TradingBot.Application.Tests;

public class FilterConverterTests
{
    [Fact]
    public void ExchangeInfoFilters_DeserializeKnownTypes_WhenOptionalFieldsMissing()
    {
        const string json = """
        {
          "timezone": "UTC",
          "serverTime": 1710000000000,
          "rateLimits": [],
          "exchangeFilters": [],
          "sors": [],
          "symbols": [
            {
              "symbol": "BNBUSDT",
              "status": "TRADING",
              "baseAsset": "BNB",
              "quoteAsset": "USDT",
              "baseAssetPrecision": 8,
              "quoteAssetPrecision": 8,
              "baseCommissionPrecision": 8,
              "quoteCommissionPrecision": 8,
              "orderTypes": [ "LIMIT", "MARKET" ],
              "icebergAllowed": true,
              "ocoAllowed": true,
              "otoAllowed": true,
              "quoteOrderQtyMarketAllowed": true,
              "allowTrailingStop": true,
              "cancelReplaceAllowed": true,
              "amendAllowed": false,
              "pegInstructionsAllowed": false,
              "isSpotTradingAllowed": true,
              "isMarginTradingAllowed": false,
              "permissions": [ "SPOT" ],
              "permissionSets": [],
              "defaultSelfTradePreventionMode": "NONE",
              "allowedSelfTradePreventionModes": [ "NONE" ],
              "filters": [
                { "filterType": "PRICE_FILTER", "minPrice": "0.01000000", "maxPrice": "100000.00000000", "tickSize": "0.01000000" },
                { "filterType": "LOT_SIZE", "minQty": "0.00100000", "maxQty": "1000.00000000", "stepSize": "0.00100000" },
                { "filterType": "MARKET_LOT_SIZE", "minQty": "0.00100000", "stepSize": "0.00100000" },
                { "filterType": "MIN_NOTIONAL", "minNotional": "5.00000000" },
                { "filterType": "NOTIONAL", "minNotional": "5.00000000", "applyMinToMarket": true },
                { "filterType": "PERCENT_PRICE", "multiplierUp": "1.3000", "multiplierDown": "0.7000" }
              ]
            }
          ]
        }
        """;

        var result = JsonSerializer.Deserialize<ExchangeInfoResponse>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        var filters = result!.Symbols.Single().Filters;
        Assert.Contains(filters, f => f is PriceFilter pf && pf.TickSize == "0.01000000");
        Assert.Contains(filters, f => f is LotSizeFilter lf && lf.StepSize == "0.00100000");
        Assert.Contains(filters, f => f is MarketLotSizeFilter);
        Assert.Contains(filters, f => f is NotionalFilter nf && nf.FilterType == "MIN_NOTIONAL" && nf.MinNotional == "5.00000000");
        Assert.Contains(filters, f => f is NotionalFilter nf && nf.FilterType == "NOTIONAL");
        Assert.Contains(filters, f => f is PercentPriceFilter pp && pp.MultiplierUp == "1.3000");
    }

    [Fact]
    public void ExchangeInfoFilters_UnknownFilterType_DoesNotThrow_AndBecomesUnknownFilter()
    {
        const string json = """
        {
          "timezone": "UTC",
          "serverTime": 1710000000000,
          "rateLimits": [],
          "exchangeFilters": [],
          "sors": [],
          "symbols": [
            {
              "symbol": "BNBUSDT",
              "status": "TRADING",
              "baseAsset": "BNB",
              "quoteAsset": "USDT",
              "baseAssetPrecision": 8,
              "quoteAssetPrecision": 8,
              "baseCommissionPrecision": 8,
              "quoteCommissionPrecision": 8,
              "orderTypes": [ "LIMIT" ],
              "icebergAllowed": true,
              "ocoAllowed": true,
              "otoAllowed": true,
              "quoteOrderQtyMarketAllowed": true,
              "allowTrailingStop": true,
              "cancelReplaceAllowed": true,
              "amendAllowed": false,
              "pegInstructionsAllowed": false,
              "isSpotTradingAllowed": true,
              "isMarginTradingAllowed": false,
              "permissions": [ "SPOT" ],
              "permissionSets": [],
              "defaultSelfTradePreventionMode": "NONE",
              "allowedSelfTradePreventionModes": [ "NONE" ],
              "filters": [
                { "filterType": "SOME_NEW_FILTER", "foo": "bar" }
              ]
            }
          ]
        }
        """;

        var result = JsonSerializer.Deserialize<ExchangeInfoResponse>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        var unknown = Assert.IsType<UnknownFilter>(result!.Symbols.Single().Filters.Single());
        Assert.Equal("SOME_NEW_FILTER", unknown.FilterType);
        Assert.Contains("SOME_NEW_FILTER", unknown.RawJson);
        Assert.Contains("\"foo\"", unknown.RawJson);
    }

    [Fact]
    public void ExchangeInfoFilters_DeserializeFromCachedPascalCaseFilterType()
    {
        const string json = """
        {
          "Timezone": "UTC",
          "ServerTime": 1710000000000,
          "RateLimits": [],
          "ExchangeFilters": [],
          "Sors": [],
          "Symbols": [
            {
              "Symbol": "BNBUSDT",
              "Status": "TRADING",
              "BaseAsset": "BNB",
              "QuoteAsset": "USDT",
              "BaseAssetPrecision": 8,
              "QuoteAssetPrecision": 8,
              "BaseCommissionPrecision": 8,
              "QuoteCommissionPrecision": 8,
              "OrderTypes": [ "LIMIT" ],
              "IcebergAllowed": true,
              "OcoAllowed": true,
              "OtoAllowed": true,
              "QuoteOrderQtyMarketAllowed": true,
              "AllowTrailingStop": true,
              "CancelReplaceAllowed": true,
              "AmendAllowed": false,
              "PegInstructionsAllowed": false,
              "IsSpotTradingAllowed": true,
              "IsMarginTradingAllowed": false,
              "Permissions": [ "SPOT" ],
              "PermissionSets": [],
              "DefaultSelfTradePreventionMode": "NONE",
              "AllowedSelfTradePreventionModes": [ "NONE" ],
              "Filters": [
                { "FilterType": "PRICE_FILTER", "MinPrice": "0.01000000", "MaxPrice": "100000.00000000", "TickSize": "0.01000000" }
              ]
            }
          ]
        }
        """;

        var result = JsonSerializer.Deserialize<ExchangeInfoResponse>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(result);
        Assert.IsType<PriceFilter>(result!.Symbols.Single().Filters.Single());
    }
}
