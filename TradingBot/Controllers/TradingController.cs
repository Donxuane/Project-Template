using Microsoft.AspNetCore.Mvc;
using TradingBot.Application.API;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradingController(TradingApi api) : ControllerBase
{
    [HttpDelete("cancelOpenOrders/symbol")]
    public async Task<ActionResult<List<OrderResponse>>> CancelOpenOrders(TradingSymbol symbol) =>
        await api.CancelOpenOrdersOnSymbol(symbol);

    [HttpGet("openOrders")]
    public async Task<ActionResult<List<OrderResponse>>> GetOpenOrders(TradingSymbol symbol) =>
        await api.GetOpenOrders(symbol);
}
