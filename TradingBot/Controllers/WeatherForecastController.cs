using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.GeneralApis;

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
            var result = _client.Call<ServerTimeResponse,EmptyResult>(null, serverTime);
            var exchange = _service.GetEndpoint(Domain.Enums.Endpoints.Account.AccoutnInformation);
            var request = new AccountInfoRequest {
            RecvWindow = 10000,Timestamp = result.Result.ServerTime};
            var response = await _client.Call<AccountInfoResponse,AccountInfoRequest>(request, exchange);
            return Ok(new { response });
        }
    }
}
