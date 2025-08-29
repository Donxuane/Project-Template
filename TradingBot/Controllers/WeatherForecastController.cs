using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Models.TradingEndpoints;
using TradingBot.Domain.Enums.Binance;
using OrderResponse = TradingBot.Domain.Models.TradingEndpoints.OrderResponse;

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

        public WeatherForecastController(ILogger<WeatherForecastController> logger,
            IBinanceEndpointsService service,
            IBinanceSettingsService settings,
            IBinanceClientService client)
        {
            _logger = logger;
            _service = service;
            _settings = settings;
            _client = client;
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
                Type = OrderTypes.MARKET,
                Quantity = 0.1m,
                Timestamp = serverTime.ServerTime
            };
            var newOrderEndpoint = _service.GetEndpoint(Trading.NewOrder);
            var order = await _client.Call<OrderResponse, NewOrderRequest>(newMarketOrder, newOrderEndpoint, true);
            return Ok(order);
        }

    }
}
