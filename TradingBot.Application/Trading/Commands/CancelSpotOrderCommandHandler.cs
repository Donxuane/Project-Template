using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Application.Trading;

public class CancelSpotOrderCommandHandler(
    IToolService toolService,
    IOrderRepository orderRepository,
    ILogger<CancelSpotOrderCommandHandler> logger) : IRequestHandler<CancelSpotOrderCommand, CancelSpotOrderResult>
{
    public async Task<CancelSpotOrderResult> Handle(CancelSpotOrderCommand request, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await orderRepository.GetByExchangeOrderIdAsync(request.ExchangeOrderId, cancellationToken);

            var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
            var serverTime = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyResult>(
                null, serverTimeEndpoint, false);

            var cancelEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Domain.Enums.Endpoints.Trading.CancelOrder);
            var cancelRequest = new CancelOrderRequest
            {
                Symbol = request.Symbol.ToString(),
                OrderId = request.ExchangeOrderId,
                Timestamp = serverTime.ServerTime,
                RecvWindow = 30000
            };

            var response = await toolService.BinanceClientService.Call<Domain.Models.TradingEndpoints.OrderResponse, CancelOrderRequest>(
                cancelRequest, cancelEndpoint, true);

            Order? updatedOrder = existing;
            if (existing != null)
            {
                existing.Status = response.Status.ToOrderStatus();
                await orderRepository.UpdateAsync(existing, cancellationToken);
                updatedOrder = existing;
            }

            return new CancelSpotOrderResult
            {
                Success = true,
                Order = updatedOrder
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception in {handler} at {time}",
                nameof(CancelSpotOrderCommandHandler),
                DateTime.UtcNow);

            return new CancelSpotOrderResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
}

