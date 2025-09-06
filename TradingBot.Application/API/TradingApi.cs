using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models.TradingEndpoints;
using OrderResponse = TradingBot.Domain.Models.TradingEndpoints.OrderResponse;

namespace TradingBot.Application.API;

public class TradingApi(IToolService service, ILogger<TradingApi> logger)
{
    public async Task<List<OrderResponse>?>? CancelOpenOrdersOnSymbol(TradingSymbol symbol)
    {
        try
        {
            var serverTimeEndpoint = service.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
            var serverTime = await service.BinanceClientService.Call<ServerTimeResponse, EmptyResult>(null, serverTimeEndpoint, false);

            var cancelAllOrdersEndpoint = service.BinanceEndpointsService.GetEndpoint(Trading.CancelAllOrders);
            var cacelAllOrders = await service.BinanceClientService.Call<List<OrderResponse>, CancelAllOrdersRequest>(new CancelAllOrdersRequest
            {
                RecvWindow = 30000,
                Symbol = symbol.ToString(),
                Timestamp = serverTime.ServerTime
            }, cancelAllOrdersEndpoint, true);
            return cacelAllOrders;
        }
        catch(Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}",
                ex.Message,
                DateTime.Now
            );
            return null;
        }
    }

    public async Task<List<OrderResponse>> GetOpenOrders(TradingSymbol symbol)
    {
        try
        {
            var serverTimeEndpoint = service.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
            var serverTime = await service.BinanceClientService.Call<ServerTimeResponse, EmptyResult>(null, serverTimeEndpoint, false);

            var openOrdersEndoint = service.BinanceEndpointsService.GetEndpoint(Trading.QueryOpenOrders);
            var openOrders = await service.BinanceClientService.Call<List<OrderResponse>, QueryOrderRequest>(new QueryOrderRequest
            {
                RecvWindow = 30000,
                Symbol = symbol.ToString(),
                Timestamp = serverTime.ServerTime
            }, openOrdersEndoint, true);
            return openOrders;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                @"Exception: {ex},  
                  DateTime: {date}",
                ex.Message,
                DateTime.Now
            );
            return null;
        }
        
    }
}
