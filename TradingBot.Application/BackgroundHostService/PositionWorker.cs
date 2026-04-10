using System.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Updates positions from orders that have synced trades (TradesSynced).
/// Uses FOR UPDATE SKIP LOCKED to claim batches of 50; computes average price and realized PnL.
/// </summary>
public class PositionWorker(IServiceScopeFactory scopeFactory, ILogger<PositionWorker> logger) : BackgroundService
{
    private const int IntervalSeconds = 30;
    private const int RetryDelaySeconds = 10;
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PositionWorker started. Interval: {Interval}s", IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdatePositionsAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PositionWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("PositionWorker stopped.");
    }

    private async Task UpdatePositionsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var tradeExecutionRepository = scope.ServiceProvider.GetRequiredService<ITradeExecutionRepository>();
        var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var orderStatusService = scope.ServiceProvider.GetRequiredService<IOrderStatusService>();

        var toProcess = new List<Order>();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        using (var trans = connection.BeginTransaction())
        {
            var synced = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(trans, ProcessingStatus.TradesSynced, BatchSize, cancellationToken);
            foreach (var order in synced)
            {
                await orderStatusService.TryUpdateProcessingStatusAsync(order.Id, ProcessingStatus.TradesSynced, ProcessingStatus.PositionUpdating, cancellationToken, trans);
                toProcess.Add(order);
            }
            trans.Commit();
        }

        using (var trans = connection.BeginTransaction())
        {
            var failed = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(trans, ProcessingStatus.PositionUpdateFailed, BatchSize, cancellationToken);
            foreach (var order in failed)
            {
                var updated = await orderStatusService.TryUpdateProcessingStatusAsync(order.Id, ProcessingStatus.PositionUpdateFailed, ProcessingStatus.PositionUpdating, cancellationToken, trans);
                if (updated)
                {
                    toProcess.Add(order);
                    logger.LogInformation("PositionWorker retrying failed Order {OrderId}", order.Id);
                }
            }
            trans.Commit();
        }

        foreach (var order in toProcess)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var trades = await tradeExecutionRepository.GetByOrderIdAsync(order.Id, cancellationToken);
                if (trades.Count == 0)
                {
                    await orderStatusService.TryUpdateProcessingStatusAsync(order.Id, ProcessingStatus.PositionUpdating, ProcessingStatus.PositionUpdated, cancellationToken);
                    logger.LogDebug("Order {OrderId} has no trades, marked PositionUpdated", order.Id);
                    continue;
                }

                decimal netQty = 0;
                decimal cost = 0;
                decimal buyQty = 0;
                foreach (var t in trades)
                {
                    var signed = t.Side == OrderSide.BUY ? t.Quantity : -t.Quantity;
                    netQty += signed;
                    if (t.Side == OrderSide.BUY)
                    {
                        cost += t.Price * t.Quantity;
                        buyQty += t.Quantity;
                    }
                }

                var position = await positionRepository.GetOpenPositionAsync(order.Symbol, cancellationToken);

                if (position == null)
                {
                    position = new Position
                    {
                        Symbol = order.Symbol,
                        Side = netQty >= 0 ? OrderSide.BUY : OrderSide.SELL,
                        Quantity = netQty,
                        AveragePrice = buyQty > 0 ? cost / buyQty : 0m,
                        OpenedAt = DateTime.UtcNow,
                        ClosedAt = null,
                        ExitPrice = null,
                        RealizedPnl = 0m,
                        UnrealizedPnl = 0m,
                        IsOpen = netQty != 0
                    };
                }
                else
                {
                    var existingQty = position.Quantity;
                    var newQty = existingQty + netQty;

                    if (order.Side == OrderSide.BUY && netQty > 0)
                    {
                        var totalCost = position.AveragePrice * existingQty + cost;
                        position.AveragePrice = newQty != 0 ? totalCost / newQty : position.AveragePrice;
                    }
                    else if (order.Side == OrderSide.SELL && netQty < 0)
                    {
                        var avgCost = position.AveragePrice;
                        foreach (var t in trades.Where(x => x.Side == OrderSide.SELL))
                            position.RealizedPnl += (t.Price - avgCost) * t.Quantity;
                    }

                    position.Quantity = newQty;
                    position.IsOpen = newQty != 0;
                    if (position.IsOpen && position.OpenedAt == null)
                        position.OpenedAt = DateTime.UtcNow;
                }

                await positionRepository.UpsertAsync(position, cancellationToken);
                await orderStatusService.TryUpdateProcessingStatusAsync(order.Id, ProcessingStatus.PositionUpdating, ProcessingStatus.PositionUpdated, cancellationToken);

                logger.LogInformation(
                    "PositionWorker updated position for Order {OrderId}, Symbol {Symbol}: Qty={Quantity}, AvgPrice={AvgPrice}, RealizedPnl={RealizedPnl}",
                    order.Id, order.Symbol, position.Quantity, position.AveragePrice, position.RealizedPnl);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "PositionWorker failed for Order {OrderId}. Setting PositionUpdateFailed.",
                    order.Id);
                await orderStatusService.TrySetPositionUpdateFailedAsync(order.Id, ProcessingStatus.PositionUpdating, cancellationToken);
            }
        }
    }
}
