using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Models.TradingEndpoints;
using BinanceOrderResponse = TradingBot.Domain.Models.TradingEndpoints.OrderResponse;
using NewOrderRequest = TradingBot.Domain.Models.TradingEndpoints.NewOrderRequest;

namespace TradingBot.Controllers;

[ApiController]
[Route("api/test")]
public class TestOrderController(
    IToolService toolService,
    ITimeSyncService timeSyncService,
    IOrderRepository orderRepository,
    ITradeExecutionRepository tradeExecutionRepository) : ControllerBase
{
    /// <summary>
    /// Place a spot MARKET order on Binance testnet and persist it in the orders table.
    /// </summary>
    [HttpPost("place-order")]
    public async Task<ActionResult<BinanceOrderResponse>> PlaceOrder(
        [FromBody] TestPlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request?.Quantity <= 0)
            return BadRequest("Quantity must be greater than zero.");

        // 1. Get adjusted timestamp from local time sync service
        var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(cancellationToken);

        // 2. Build and send MARKET order (POST /api/v3/order)
        var newOrderEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Trading.NewOrder);
        var newOrderRequest = new NewOrderRequest
        {
            Symbol = request!.Symbol.ToString(),
            Side = request.Side,
            Type = OrderTypes.MARKET,
            Quantity = request.Quantity,
            Timestamp = adjustedTimestamp,
            RecvWindow = 30000
        };

        var response = await toolService.BinanceClientService.Call<BinanceOrderResponse, NewOrderRequest>(
            newOrderRequest, newOrderEndpoint, true);

        // 3. Save order in orders table
        var price = decimal.TryParse(response.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0m;
        var quantity = response.ExecutedQty.ToDecimal() > 0 ? response.ExecutedQty.ToDecimal() : request.Quantity;

        var order = new Order
        {
            ExchangeOrderId = response.OrderId,
            Symbol = request.Symbol,
            Side = request.Side,
            Status = response.Status.ToOrderStatus(),
            Price = price,
            Quantity = quantity,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await orderRepository.InsertAsync(order, cancellationToken);

        if (response.Fills is { Count: > 0 })
        {
            var responseSymbol = Enum.TryParse<TradingSymbol>(response.Symbol, true, out var sym) ? sym : request.Symbol;
            var responseSide = Enum.TryParse<OrderSide>(response.Side, true, out var sid) ? sid : request.Side;
            var executedAt = DateTimeOffset.FromUnixTimeMilliseconds(response.TransactTime).UtcDateTime;

            foreach (var fill in response.Fills)
            {
                var execution = new TradeExecution
                {
                    OrderId = order.Id,
                    ExchangeOrderId = response.OrderId,
                    ExchangeTradeId = fill.TradeId,
                    Symbol = responseSymbol,
                    Side = responseSide,
                    Price = fill.Price.ToDecimal(),
                    Quantity = fill.Qty.ToDecimal(),
                    ExecutedAt = executedAt
                };
                await tradeExecutionRepository.InsertAsync(execution, cancellationToken);
            }
        }

        return Ok(response);
    }
}


public class TestPlaceOrderRequest
{
    public TradingSymbol Symbol { get; set; }
    public OrderSide Side { get; set; }
    public decimal Quantity { get; set; }
}
