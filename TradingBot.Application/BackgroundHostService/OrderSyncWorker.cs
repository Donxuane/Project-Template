using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models.TradingEndpoints;
using BinanceOrderResponse = TradingBot.Domain.Models.TradingEndpoints.OrderResponse;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Synchronizes order status between Binance and the local database.
/// Fetches orders with Status NEW or PARTIALLY_FILLED, calls GET /api/v3/order, and updates DB.
/// </summary>
public class OrderSyncWorker(IServiceScopeFactory scopeFactory, ILogger<OrderSyncWorker> logger) : BackgroundService
{
    private const int IntervalSeconds = 20;
    private const int RetryDelaySeconds = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OrderSyncWorker started. Interval: {Interval}s", IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOrdersAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OrderSyncWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("OrderSyncWorker stopped.");
    }

    private async Task SyncOrdersAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var orderStatusService = scope.ServiceProvider.GetRequiredService<IOrderStatusService>();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();

        var openOrders = await orderRepository.GetOpenOrdersAsync(null, 50, cancellationToken);
        if (openOrders.Count == 0)
            return;

        var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
        var serverTime = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyRequest>(
            null, serverTimeEndpoint, false);

        var queryEndpoint = toolService.BinanceEndpointsService.GetEndpoint(TradingBot.Domain.Enums.Endpoints.Trading.QueryOrder);

        foreach (var order in openOrders)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (order.ExchangeOrderId is null)
            {
                logger.LogDebug("Order {OrderId} has no ExchangeOrderId, skipping", order.Id);
                continue;
            }

            try
            {
                var query = new QueryOrderRequest
                {
                    Symbol = order.Symbol.ToString(),
                    OrderId = order.ExchangeOrderId.Value,
                    Timestamp = serverTime.ServerTime,
                    RecvWindow = 30000
                };

                var response = await toolService.BinanceClientService.Call<BinanceOrderResponse, QueryOrderRequest>(
                    query, queryEndpoint, true);

                var exchangeStatus = response.Status.ToOrderStatus();

                if (exchangeStatus == order.Status)
                    continue;

                order.Status = exchangeStatus;
                order.UpdatedAt = DateTime.UtcNow;
                if (response.ExecutedQty != null)
                {
                    var executedQty = decimal.TryParse(response.ExecutedQty, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var q) ? q : order.Quantity;
                    if (executedQty > 0)
                        order.Quantity = executedQty;
                }

                await orderRepository.UpdateAsync(order, cancellationToken);

                logger.LogInformation(
                    "Order {OrderId} status updated: {OldStatus} -> {NewStatus} (ExchangeOrderId: {ExchangeOrderId})",
                    order.Id, order.Status, exchangeStatus, order.ExchangeOrderId);

                if (exchangeStatus is OrderStatuses.FILLED or OrderStatuses.PARTIALLY_FILLED)
                {
                    var updated = await orderStatusService.TryUpdateProcessingStatusAsync(
                        order.Id, ProcessingStatus.OrderPlaced, ProcessingStatus.TradesSyncPending, cancellationToken);
                    if (updated)
                        logger.LogDebug("Order {OrderId} set to TradesSyncPending", order.Id);
                }

                if (exchangeStatus == OrderStatuses.CANCELED)
                    logger.LogInformation("Order {OrderId} canceled on exchange", order.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "OrderSyncWorker failed for Order {OrderId}, ExchangeOrderId {ExchangeOrderId}. Will retry next cycle.",
                    order.Id, order.ExchangeOrderId);
            }
        }
    }
}
