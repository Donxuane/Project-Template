using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Cache;
using TradingBot.Domain.Models.Binance;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Domain.Utilities;
using TradingBot.Percistance.Services.Main;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Shared.Shared.Models;

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

    [Fact]
    public void MarketOrder_PrefersMarketLotSize_WhenAvailable()
    {
        var filters = new BinanceSymbolFilters
        {
            Symbol = "BTCUSDT",
            StepSize = 0.01m,
            MinQty = 0.01m,
            MaxQty = 10m,
            MarketStepSize = 0.1m,
            MarketMinQty = 0.1m,
            MarketMaxQty = 10m,
            MinNotional = 5m
        };
        var request = new NewOrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.BUY,
            Type = OrderTypes.MARKET,
            Quantity = 0.15m,
            Timestamp = 123
        };

        var result = BinanceOrderNormalizationService.NormalizeNewOrder(request, filters, marketPrice: 100m);

        Assert.Equal(0.1m, result.Request.Quantity);
    }

    [Fact]
    public void MarketSellCloseLong_StillAppliesQuantityNormalization()
    {
        var filters = new BinanceSymbolFilters
        {
            Symbol = "BTCUSDT",
            StepSize = 0.01m,
            MinQty = 0.01m,
            MaxQty = 10m,
            MarketStepSize = 0.1m,
            MarketMinQty = 0.1m,
            MarketMaxQty = 10m,
            MinNotional = 5m
        };
        var request = new NewOrderRequest
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.SELL,
            Type = OrderTypes.MARKET,
            Quantity = 0.19m,
            Timestamp = 123
        };

        var result = BinanceOrderNormalizationService.NormalizeNewOrder(request, filters, marketPrice: 100m);

        Assert.Equal(0.1m, result.Request.Quantity);
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

public class BinanceOrderNormalizationServiceTests
{
    [Fact]
    public async Task GetSymbolFilters_FallsBackToMinNotional_WhenNotionalFilterMissing()
    {
        var exchangeInfo = new ExchangeInfoResponse
        {
            Timezone = "UTC",
            ServerTime = 0,
            RateLimits = [],
            ExchangeFilters = [],
            Sors = [],
            Symbols =
            [
                new SymbolInfo
                {
                    Symbol = "SOLUSDT",
                    Status = "TRADING",
                    BaseAsset = "SOL",
                    QuoteAsset = "USDT",
                    BaseAssetPrecision = 8,
                    QuoteAssetPrecision = 8,
                    BaseCommissionPrecision = 8,
                    QuoteCommissionPrecision = 8,
                    OrderTypes = [ "LIMIT", "MARKET" ],
                    IcebergAllowed = true,
                    OcoAllowed = true,
                    OtoAllowed = true,
                    QuoteOrderQtyMarketAllowed = true,
                    AllowTrailingStop = false,
                    CancelReplaceAllowed = true,
                    AmendAllowed = false,
                    PegInstructionsAllowed = false,
                    IsSpotTradingAllowed = true,
                    IsMarginTradingAllowed = false,
                    Permissions = [ "SPOT" ],
                    PermissionSets = [],
                    DefaultSelfTradePreventionMode = "NONE",
                    AllowedSelfTradePreventionModes = [ "NONE" ],
                    Filters =
                    [
                        new LotSizeFilter
                        {
                            FilterType = "LOT_SIZE",
                            MinQty = "0.01",
                            MaxQty = "1000",
                            StepSize = "0.01"
                        },
                        new NotionalFilter
                        {
                            FilterType = "MIN_NOTIONAL",
                            MinNotional = "5",
                            ApplyMinToMarket = true,
                            MaxNotional = string.Empty,
                            ApplyMaxToMarket = false,
                            AvgPriceMins = 5
                        }
                    ]
                }
            ]
        };

        var service = new BinanceOrderNormalizationService(
            new FakeBinanceClientService(exchangeInfo),
            new FakeBinanceEndpointsService(),
            new FakeRedisCacheService(),
            NullLogger<BinanceOrderNormalizationService>.Instance);

        var filters = await service.GetSymbolFiltersAsync("SOLUSDT");

        Assert.Equal(5m, filters.MinNotional);
    }

    private sealed class FakeBinanceClientService(ExchangeInfoResponse exchangeInfo) : IBinanceClientService
    {
        public Task<TResponse> Call<TResponse, TRequest>(TRequest? request, Endpoint endpoint, bool enableSignature)
            => Task.FromResult((TResponse)(object)exchangeInfo);
    }

    private sealed class FakeBinanceEndpointsService : IBinanceEndpointsService
    {
        private static readonly Endpoint ExchangeEndpoint = new()
        {
            API = "/api/v3/exchangeInfo",
            Type = "GET"
        };

        public Endpoint GetEndpoint(Account account) => ExchangeEndpoint;
        public Endpoint GetEndpoint(GeneralApis general) => ExchangeEndpoint;
        public Endpoint GetEndpoint(MarketData marketData) => ExchangeEndpoint;
        public Endpoint GetEndpoint(TradingBot.Domain.Enums.Endpoints.Trading trading) => ExchangeEndpoint;
    }

    private sealed class FakeRedisCacheService : IRedisCacheService
    {
        private readonly Dictionary<string, object?> _cache = new(StringComparer.Ordinal);

        public Task<TRequest?> SetCacheValue<TRequest>(string key, TRequest value)
        {
            _cache[key] = value;
            return Task.FromResult<TRequest?>(value);
        }

        public Task<TResponse?> GetCacheValue<TResponse>(string key)
        {
            if (_cache.TryGetValue(key, out var value) && value is TResponse typed)
                return Task.FromResult<TResponse?>(typed);

            return Task.FromResult<TResponse?>(default);
        }

        public Task RemoveCacheValue(string key)
        {
            _cache.Remove(key);
            return Task.CompletedTask;
        }

        public Task<List<object?>?> GetAllCachedData(List<string> keys)
            => Task.FromResult<List<object?>?>(keys.Select(key => _cache.TryGetValue(key, out var value) ? value : null).ToList());
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
