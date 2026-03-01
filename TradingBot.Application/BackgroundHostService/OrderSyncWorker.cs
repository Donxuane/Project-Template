using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models.TradingEndpoints;

namespace TradingBot.Application.BackgroundHostService;

public class OrderSyncWorker(IServiceProvider serviceProvider, ILogger<OrderSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
                var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
                var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();

                var openOrders = await orderRepository.GetOpenOrdersAsync(null, stoppingToken);
                if (openOrders.Count > 0)
                {
                    var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
                    var serverTime = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyResult>(
                        null, serverTimeEndpoint, false);

                    var queryEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Domain.Enums.Endpoints.Trading.QueryOrder);

                    foreach (var order in openOrders)
                    {
                        if (stoppingToken.IsCancellationRequested)
                            break;

                        if (order.ExchangeOrderId is null)
                            continue;

                        var query = new QueryOrderRequest
                        {
                            Symbol = order.Symbol.ToString(),
                            OrderId = order.ExchangeOrderId.Value,
                            Timestamp = serverTime.ServerTime,
                            RecvWindow = 30000
                        };

                        var response = await toolService.BinanceClientService.Call<Domain.Models.TradingEndpoints.OrderResponse, QueryOrderRequest>(
                            query, queryEndpoint, true);

                        var newStatus = response.Status.ToOrderStatus();
                        if (newStatus != order.Status)
                        {
                            order.Status = newStatus;
                            await orderRepository.UpdateAsync(order, stoppingToken);

                            if (newStatus is OrderStatuses.FILLED or OrderStatuses.PARTIALLY_FILLED)
                            {
                                var position = await positionRepository.GetOpenPositionAsync(order.Symbol, stoppingToken);
                                var existingQty = position?.Quantity ?? 0m;
                                var signedQty = order.Side == OrderSide.BUY ? order.Quantity : -order.Quantity;
                                var newQty = existingQty + signedQty;

                                if (position == null)
                                {
                                    position = new Domain.Models.Trading.Position
                                    {
                                        Symbol = order.Symbol,
                                        Side = order.Side,
                                        Quantity = newQty,
                                        AveragePrice = order.Price,
                                        IsOpen = newQty != 0
                                    };
                                }
                                else
                                {
                                    if (order.Side == OrderSide.BUY && newQty > 0)
                                    {
                                        var totalCost = position.AveragePrice * existingQty + order.Price * order.Quantity;
                                        position.AveragePrice = totalCost / newQty;
                                    }

                                    position.Quantity = newQty;
                                    position.IsOpen = newQty != 0;
                                }

                                await positionRepository.UpsertAsync(position, stoppingToken);
                            }
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    @"Exception in OrderSyncWorker at {time}",
                    DateTime.UtcNow);

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}

