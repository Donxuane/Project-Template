using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Applies synced trade executions to local position accounting.
/// </summary>
public class PositionWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<PositionWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 30;
    private const int DefaultRetryDelaySeconds = 10;
    private const int DefaultBatchSize = 50;
    private const int DefaultMaxRetryCount = 5;
    private const bool DefaultEnabled = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadSettings();
        if (!settings.Enabled)
        {
            logger.LogInformation("PositionWorker is disabled by configuration.");
            return;
        }

        logger.LogInformation(
            "PositionWorker started. Interval={Interval}s, RetryDelay={RetryDelay}s, BatchSize={BatchSize}, MaxRetryCount={MaxRetryCount}",
            settings.IntervalSeconds,
            settings.RetryDelaySeconds,
            settings.BatchSize,
            settings.MaxRetryCount);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UpdatePositionsAsync(settings, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "PositionWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow,
                    settings.RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("PositionWorker stopped.");
    }

    private async Task UpdatePositionsAsync(PositionWorkerSettings settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var tradeExecutionRepository = scope.ServiceProvider.GetRequiredService<ITradeExecutionRepository>();
        var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var orderStatusService = scope.ServiceProvider.GetRequiredService<IOrderStatusService>();
        var accountingService = scope.ServiceProvider.GetRequiredService<IPositionAccountingService>();

        var toProcess = new List<Order>();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        using (var trans = connection.BeginTransaction())
        {
            var synced = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(
                trans,
                ProcessingStatus.TradesSynced,
                settings.BatchSize,
                cancellationToken);
            foreach (var order in synced)
            {
                var claimed = await orderStatusService.TryUpdateProcessingStatusAsync(
                    order.Id,
                    ProcessingStatus.TradesSynced,
                    ProcessingStatus.PositionUpdating,
                    cancellationToken,
                    trans);
                if (claimed)
                    toProcess.Add(order);
            }
            trans.Commit();
        }

        using (var trans = connection.BeginTransaction())
        {
            var failed = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(
                trans,
                ProcessingStatus.PositionUpdateFailed,
                settings.BatchSize,
                cancellationToken);
            foreach (var order in failed)
            {
                if (order.SyncRetryCount >= settings.MaxRetryCount)
                {
                    logger.LogCritical(
                        "PositionWorker max retry count reached. Manual review required. OrderId={OrderId}, Symbol={Symbol}, RetryCount={RetryCount}, MaxRetryCount={MaxRetryCount}",
                        order.Id,
                        order.Symbol,
                        order.SyncRetryCount,
                        settings.MaxRetryCount);
                    // TODO: Add dedicated NeedsManualReview status and position-specific retry counters.
                    continue;
                }

                var updated = await orderStatusService.TryUpdateProcessingStatusAsync(
                    order.Id,
                    ProcessingStatus.PositionUpdateFailed,
                    ProcessingStatus.PositionUpdating,
                    cancellationToken,
                    trans);
                if (updated)
                {
                    toProcess.Add(order);
                    logger.LogInformation(
                        "PositionWorker retrying failed Order {OrderId} (RetryCount={RetryCount})",
                        order.Id,
                        order.SyncRetryCount);
                }
            }
            trans.Commit();
        }

        toProcess = toProcess
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        foreach (var order in toProcess)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var trades = await tradeExecutionRepository.GetByOrderIdAsync(order.Id, cancellationToken);
                if (trades.Count == 0)
                {
                    logger.LogWarning(
                        "No trades found for TradesSynced order. OrderId={OrderId}, Symbol={Symbol}",
                        order.Id,
                        order.Symbol);
                    await orderStatusService.TrySetPositionUpdateFailedAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        cancellationToken);
                    continue;
                }

                if (trades.All(t => t.PositionProcessedAt is not null))
                {
                    await orderStatusService.TryUpdateProcessingStatusAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        ProcessingStatus.PositionUpdated,
                        cancellationToken);
                    logger.LogInformation(
                        "PositionWorker skipped already-processed trades. OrderId={OrderId}, Symbol={Symbol}, TradeCount={TradeCount}",
                        order.Id,
                        order.Symbol,
                        trades.Count);
                    continue;
                }

                var currentPosition = await positionRepository.GetOpenPositionAsync(order.Symbol, cancellationToken);
                var oldQuantity = currentPosition?.Quantity ?? 0m;
                var oldAveragePrice = currentPosition?.AveragePrice ?? 0m;
                var oldRealizedPnl = currentPosition?.RealizedPnl ?? 0m;

                var accounting = accountingService.ApplyTrades(currentPosition, order, trades);
                if (accounting.ProcessedTradeCount == 0)
                {
                    await orderStatusService.TryUpdateProcessingStatusAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        ProcessingStatus.PositionUpdated,
                        cancellationToken);
                    logger.LogWarning(
                        "PositionWorker applied no-op accounting and completed order safely. OrderId={OrderId}, Symbol={Symbol}, Reason={Reason}",
                        order.Id,
                        order.Symbol,
                        accounting.Reason);
                    continue;
                }

                // TODO: Position upsert + trade processed marking + order status transition should be atomic per order.
                if (!accounting.Position.IsOpen && order.CloseReason != CloseReason.None)
                    accounting.Position.ExitReason = MapExitReason(order.CloseReason);

                await positionRepository.UpsertAsync(accounting.Position, cancellationToken);
                await tradeExecutionRepository.MarkPositionProcessedByOrderAsync(order.Id, DateTime.UtcNow, cancellationToken);
                await orderStatusService.TryUpdateProcessingStatusAsync(
                    order.Id,
                    ProcessingStatus.PositionUpdating,
                    ProcessingStatus.PositionUpdated,
                    cancellationToken);

                logger.LogInformation(
                    "PositionWorker processed order: OrderId={OrderId}, Symbol={Symbol}, TradeCount={TradeCount}, OldQuantity={OldQuantity}, NewQuantity={NewQuantity}, OldAveragePrice={OldAveragePrice}, NewAveragePrice={NewAveragePrice}, RealizedPnlDelta={RealizedPnlDelta}, TotalRealizedPnl={TotalRealizedPnl}, PositionOpened={PositionOpened}, PositionClosed={PositionClosed}, PositionFlipped={PositionFlipped}",
                    order.Id,
                    order.Symbol,
                    accounting.ProcessedTradeCount,
                    oldQuantity,
                    accounting.Position.Quantity,
                    oldAveragePrice,
                    accounting.Position.AveragePrice,
                    accounting.RealizedPnlDelta,
                    accounting.Position.RealizedPnl,
                    accounting.PositionOpened,
                    accounting.PositionClosed,
                    accounting.PositionFlipped);

                if (accounting.PositionOpened)
                    logger.LogInformation(
                        "Position lifecycle: opened/added. OrderId={OrderId}, PositionId={PositionId}, Symbol={Symbol}, Quantity={Quantity}, AveragePrice={AveragePrice}, RealizedPnl={RealizedPnl}",
                        order.Id,
                        accounting.Position.Id,
                        order.Symbol,
                        accounting.Position.Quantity,
                        accounting.Position.AveragePrice,
                        accounting.Position.RealizedPnl);

                var quantityReduced = Math.Abs(accounting.Position.Quantity) < Math.Abs(oldQuantity);
                if (quantityReduced && accounting.Position.IsOpen)
                    logger.LogInformation(
                        "Position lifecycle: partial close. OrderId={OrderId}, PositionId={PositionId}, Symbol={Symbol}, PreviousQuantity={PreviousQuantity}, RemainingQuantity={RemainingQuantity}, AveragePrice={AveragePrice}, RealizedPnlDelta={RealizedPnlDelta}, TotalRealizedPnl={TotalRealizedPnl}",
                        order.Id,
                        accounting.Position.Id,
                        order.Symbol,
                        oldQuantity,
                        accounting.Position.Quantity,
                        accounting.Position.AveragePrice,
                        accounting.RealizedPnlDelta,
                        accounting.Position.RealizedPnl);

                if (!accounting.Position.IsOpen && oldQuantity != 0m)
                    logger.LogInformation(
                        "Position lifecycle: full close. OrderId={OrderId}, PositionId={PositionId}, Symbol={Symbol}, ClosedAt={ClosedAt}, ExitPrice={ExitPrice}, ExitReason={ExitReason}, PreviousQuantity={PreviousQuantity}, RealizedPnlDelta={RealizedPnlDelta}, RealizedPnlBefore={RealizedPnlBefore}, RealizedPnlAfter={RealizedPnlAfter}",
                        order.Id,
                        accounting.Position.Id,
                        order.Symbol,
                        accounting.Position.ClosedAt,
                        accounting.Position.ExitPrice,
                        accounting.Position.ExitReason,
                        oldQuantity,
                        accounting.RealizedPnlDelta,
                        oldRealizedPnl,
                        accounting.Position.RealizedPnl);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "PositionWorker failed for OrderId={OrderId}, Symbol={Symbol}. Setting PositionUpdateFailed.",
                    order.Id,
                    order.Symbol);
                await orderStatusService.TrySetPositionUpdateFailedAsync(
                    order.Id,
                    ProcessingStatus.PositionUpdating,
                    cancellationToken);
            }
        }
    }

    private PositionWorkerSettings ReadSettings()
    {
        var intervalSeconds = Math.Max(1, configuration.GetValue<int?>("PositionWorker:IntervalSeconds") ?? DefaultIntervalSeconds);
        var retryDelaySeconds = Math.Max(1, configuration.GetValue<int?>("PositionWorker:RetryDelaySeconds") ?? DefaultRetryDelaySeconds);
        var batchSize = Math.Max(1, configuration.GetValue<int?>("PositionWorker:BatchSize") ?? DefaultBatchSize);
        var enabled = configuration.GetValue<bool?>("PositionWorker:Enabled") ?? DefaultEnabled;
        var maxRetryCount = Math.Max(1, configuration.GetValue<int?>("PositionWorker:MaxRetryCount") ?? DefaultMaxRetryCount);

        return new PositionWorkerSettings
        {
            IntervalSeconds = intervalSeconds,
            RetryDelaySeconds = retryDelaySeconds,
            BatchSize = batchSize,
            Enabled = enabled,
            MaxRetryCount = maxRetryCount
        };
    }

    private sealed class PositionWorkerSettings
    {
        public int IntervalSeconds { get; init; }
        public int RetryDelaySeconds { get; init; }
        public int BatchSize { get; init; }
        public int MaxRetryCount { get; init; }
        public bool Enabled { get; init; }
    }

    private static PositionExitReason? MapExitReason(CloseReason closeReason)
    {
        return closeReason switch
        {
            CloseReason.StopLoss => PositionExitReason.StopLoss,
            CloseReason.TakeProfit => PositionExitReason.TakeProfit,
            CloseReason.MaxDuration => PositionExitReason.Time,
            CloseReason.RiskExit => PositionExitReason.TrailingStop,
            _ => null
        };
    }
}
