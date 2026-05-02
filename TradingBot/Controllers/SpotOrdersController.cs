using MediatR;
using Microsoft.AspNetCore.Mvc;
using TradingBot.Application.Trading.Commands;
using TradingBot.Application.Trading.Queries;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpotOrdersController(IMediator mediator) : ControllerBase
{
    [HttpPost("place")]
    public async Task<ActionResult<PlaceSpotOrderResult>> PlaceOrder(
        TradingSymbol symbol,
        OrderSide side,
        decimal quantity,
        decimal? price,
        bool isLimitOrder,
        CancellationToken cancellationToken)
    {
        var command = new PlaceSpotOrderCommand(
            symbol,
            side,
            quantity,
            price,
            isLimitOrder,
            OrderSource.Api,
            side == OrderSide.SELL ? CloseReason.ManualClose : CloseReason.None,
            ParentPositionId: null,
            CorrelationId: Guid.NewGuid().ToString("N"));
        var result = await mediator.Send(command, cancellationToken);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("cancel")]
    public async Task<ActionResult<CancelSpotOrderResult>> CancelOrder(
        TradingSymbol symbol,
        long exchangeOrderId,
        CancellationToken cancellationToken)
    {
        var command = new CancelSpotOrderCommand(symbol, exchangeOrderId);
        var result = await mediator.Send(command, cancellationToken);
        if (!result.Success)
            return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("open-orders")]
    public async Task<ActionResult<IReadOnlyList<Order>>> GetOpenOrders(
        TradingSymbol? symbol,
        CancellationToken cancellationToken)
    {
        var query = new GetOpenOrdersQuery(symbol);
        var orders = await mediator.Send(query, cancellationToken);
        return Ok(orders);
    }

    [HttpGet("positions")]
    public async Task<ActionResult<IReadOnlyList<Position>>> GetPositions(CancellationToken cancellationToken)
    {
        var positions = await mediator.Send(new GetPositionsQuery(), cancellationToken);
        return Ok(positions);
    }

    [HttpGet("balances")]
    public async Task<ActionResult<IReadOnlyList<BalanceSnapshot>>> GetBalances(CancellationToken cancellationToken)
    {
        var balances = await mediator.Send(new GetBalanceQuery(), cancellationToken);
        return Ok(balances);
    }
}

