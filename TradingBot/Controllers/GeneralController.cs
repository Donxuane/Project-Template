using Microsoft.AspNetCore.Mvc;
using TradingBot.Application.API;

namespace TradingBot.Controllers;

[ApiController]
[Route("[controller]")]
public class GeneralController(GeneralApi generalApi) : ControllerBase 
{
    [HttpGet("getServerTime")]
    public async Task<ActionResult<long>> GetServerTime()
    {
        return await generalApi.GetServerTime();
    }
}
