using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Domain.Enums.Binance;
using OrderResponse = TradingBot.Domain.Models.TradingEndpoints.OrderResponse;
using TradingBot.Domain.Models.MarketData;
using System.Net.WebSockets;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<WeatherForecastController> _logger;
        private readonly IBinanceEndpointsService _service;
        private readonly IBinanceSettingsService _settings;
        private readonly IBinanceClientService _client;
        private readonly ISlicerService _slicer;
        private readonly IMemoryCacheService _cache;
        private readonly IAICLinetService _iclinetService;

        public WeatherForecastController(ILogger<WeatherForecastController> logger,
            IBinanceEndpointsService service,
            IBinanceSettingsService settings,
            IBinanceClientService client,
            ISlicerService slicer,
            IMemoryCacheService cache,
            IAICLinetService iclinetService)
        {
            _logger = logger;
            _service = service;
            _settings = settings;
            _client = client;
            _slicer = slicer;
            _cache = cache;
            _iclinetService = iclinetService;
        }

        [HttpGet(Name = "GetWeatherForecast")]
        public IEnumerable<WeatherForecast> Get()
        {
            return Enumerable.Range(1, 5).Select(index => new WeatherForecast
            {
                Date = DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                TemperatureC = Random.Shared.Next(-20, 55),
                Summary = Summaries[Random.Shared.Next(Summaries.Length)]
            })
            .ToArray();
        }
        [HttpGet("GetAccountEndpoint")]
        public async Task<ActionResult> GetAction()
        {
            var serverTime = _service.GetEndpoint(Domain.Enums.Endpoints.GeneralApis.CheckServerTime);
            var result = _client.Call<ServerTimeResponse, EmptyResult>(null, serverTime, false);
            var exchange = _service.GetEndpoint(Account.AccoutnInformation);
            var request = new AccountInfoRequest
            {
                RecvWindow = 10000,
                Timestamp = result.Result.ServerTime,
                OmitZeroBalances = false
            };
            var response = await _client.Call<AccountInfoResponse, AccountInfoRequest>(request, exchange, true);
            return Ok(new { response });
        }

        [HttpPost("Make an order")]
        public async Task<ActionResult> MakeAnOrder()
        {
            var checkServerTimeEndpoint = _service.GetEndpoint(GeneralApis.CheckServerTime);
            var serverTime = await _client.Call<ServerTimeResponse, EmptyResult>(null, checkServerTimeEndpoint, false);
            var newMarketOrder = new NewOrderRequest
            {
                Symbol = TradingSymbol.BTCUSDT.ToString(),
                Side = OrderSide.SELL,
                Type = OrderTypes.LIMIT,
                Quantity = 0.1m,
                Timestamp = serverTime.ServerTime,
                TimeInForce = TimeInForce.GTC,
                Price = 120000m
            };
            var newOrderEndpoint = _service.GetEndpoint(Trading.NewOrder);
            var order = await _client.Call<OrderResponse, NewOrderRequest>(newMarketOrder, newOrderEndpoint, true);
            return Ok(order);
        }

        [HttpGet("exchangeInformation")]
        public async Task<ActionResult> GetExchangeInformation()
        {
            var endpoint = _service.GetEndpoint(GeneralApis.ExchangeInformation);
            var result = await _client.Call<ExchangeInfoResponse, CurrencyPairs>(
                new CurrencyPairs
                {
                    Symbol = TradingSymbol.BTCUSDT.ToString()
                }, endpoint, false);
            return Ok(result);
        }

        [HttpGet("AssetPortion")]
        public async Task<ActionResult> GetCurrentPrice(decimal price)
        {
            var endpoint = _service.GetEndpoint(MarketData.SymbolPriceTicker);
            var result = await _client.Call<SymbolPriceTickerResponse, SymbolPriceTickerRequest>(new SymbolPriceTickerRequest
            {
                Symbol = TradingSymbol.BTCUSDT.ToString()
            }, endpoint, false);
            var slice = _slicer.GetSliceAmount(result, price);
            return Ok(slice);
        }

        [HttpPost("cacheData")]
        public ActionResult CacheData(string name, int age)
        {
            var key = $"{name}_{age}";
            _cache.SetCacheValue(key, new { name, age });
            return Ok(key);
        }

        [HttpDelete("removeCacheData")]
        public ActionResult RemoveCachedData(string key)
        {
            _cache.RemoveCacheValue(key);
            return Ok();
        }

        [HttpGet("RateLimits")]
        public async Task<ActionResult> RateLimits()
        {
            var result = await _settings.GetRateLimitterSettings(RateLimitType.REQUEST_WEIGHT, TradingSymbol.BTCUSDT);
            return Ok(result);
        }

        [HttpGet("PriceValidator")]
        public async Task<ActionResult> ValidatePrice(decimal price)
        {
            var result = await _settings.ValidatePrice(TradingSymbol.BTCUSDT, price);
            return Ok(result);
        }

        [HttpPost("AiCall")]
        public async Task<ActionResult> CallAI(string call)
        {
            var result = await _iclinetService.Call<string, string>(call, Domain.Enums.AI.AiRequestModels.String);
            return Ok(result);
        }
    }
}
