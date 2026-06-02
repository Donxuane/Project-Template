using MediatR;
using Microsoft.AspNetCore.Mvc;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;

namespace TradingBot.Controllers;

[ApiController]
[Route("api/test")]
public class TestOrderController(
    IMediator mediator) : ControllerBase
{
    [HttpPost("place-order")]
    public async Task<ActionResult<PlaceSpotOrderResult>> PlaceOrder(
        [FromBody] TestPlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Quantity <= 0)
            return BadRequest("Quantity must be greater than zero.");

        var command = new PlaceSpotOrderCommand(
            request.Symbol,
            request.Side,
            request.Quantity,
            Price: null,
            IsLimitOrder: false,
            OrderSource: OrderSource.Manual,
            CloseReason: request.Side == OrderSide.SELL ? CloseReason.ManualClose : CloseReason.None,
            ParentPositionId: null,
            CorrelationId: Guid.NewGuid().ToString("N"));
        var result = await mediator.Send(command, cancellationToken);
        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }
}


public class TestPlaceOrderRequest
{
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
}
