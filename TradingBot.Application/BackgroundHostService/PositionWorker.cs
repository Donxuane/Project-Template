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

                if (AreAllExecutionsProcessed(trades))
                {
                    await orderStatusService.TryUpdateProcessingStatusAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        ProcessingStatus.PositionUpdated,
                        cancellationToken);
                    logger.LogInformation(
                        "PositionWorker completed order with already-processed trades. OrderId={OrderId}, Symbol={Symbol}, TradeCount={TradeCount}",
                        order.Id,
                        order.Symbol,
                        trades.Count);
                    continue;
                }

                var currentPosition = await ResolvePositionForOrderAsync(order, positionRepository, cancellationToken);
                if (order.ParentPositionId.HasValue && order.ParentPositionId.Value > 0 && currentPosition is null)
                {
                    logger.LogError(
                        "PositionWorker moved order to PositionUpdateFailed because parent position was not found. OrderId={OrderId}, Symbol={Symbol}, Side={Side}, ParentPositionId={ParentPositionId}, TradeCount={TradeCount}",
                        order.Id,
                        order.Symbol,
                        order.Side,
                        order.ParentPositionId,
                        trades.Count);
                    await orderStatusService.TrySetPositionUpdateFailedAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        cancellationToken);
                    continue;
                }
                var oldQuantity = currentPosition?.Quantity ?? 0m;
                var oldAveragePrice = currentPosition?.AveragePrice ?? 0m;
                var oldRealizedPnl = currentPosition?.RealizedPnl ?? 0m;

                var accounting = accountingService.ApplyTrades(currentPosition, order, trades);
                if (accounting.ProcessedTradeCount == 0)
                {
                    var parentPosition = await ResolveParentPositionAsync(order, positionRepository, cancellationToken);
                    var expectedAlreadyAccounted = IsExpectedAlreadyAccountedClosePath(order, parentPosition);
                    if (expectedAlreadyAccounted)
                    {
                        if (parentPosition is not null && parentPosition.IsClosing)
                        {
                            parentPosition.IsClosing = false;
                            await positionRepository.UpsertAsync(parentPosition, cancellationToken);
                        }

                        await tradeExecutionRepository.MarkPositionProcessedByOrderAsync(order.Id, DateTime.UtcNow, cancellationToken);
                        var refreshedTrades = await tradeExecutionRepository.GetByOrderIdAsync(order.Id, cancellationToken);
                        if (!AreAllExecutionsProcessed(refreshedTrades))
                        {
                            logger.LogError(
                                "PositionWorker could not complete order because trade executions remain unprocessed after marking attempt. OrderId={OrderId}, Symbol={Symbol}, Side={Side}, ExecutionCount={ExecutionCount}, ParentPositionId={ParentPositionId}, Reason={Reason}",
                                order.Id,
                                order.Symbol,
                                order.Side,
                                refreshedTrades.Count,
                                order.ParentPositionId,
                                "Expected already-accounted close path, but position_processed_at still contains null values.");
                            await orderStatusService.TrySetPositionUpdateFailedAsync(
                                order.Id,
                                ProcessingStatus.PositionUpdating,
                                cancellationToken);
                            continue;
                        }

                        await orderStatusService.TryUpdateProcessingStatusAsync(
                            order.Id,
                            ProcessingStatus.PositionUpdating,
                            ProcessingStatus.PositionUpdated,
                            cancellationToken);
                        logger.LogWarning(
                            "PositionWorker marked executions as processed for already-closed parent position and completed order. OrderId={OrderId}, Symbol={Symbol}, Side={Side}, ExecutionCount={ExecutionCount}, ParentPositionId={ParentPositionId}, OrderSource={OrderSource}, CloseReason={CloseReason}, AccountingReason={AccountingReason}",
                            order.Id,
                            order.Symbol,
                            order.Side,
                            refreshedTrades.Count,
                            order.ParentPositionId,
                            order.OrderSource,
                            order.CloseReason,
                            accounting.Reason);
                        continue;
                    }

                    logger.LogError(
                        "PositionWorker moved order to PositionUpdateFailed because executions were not applied and path is not recognized as already-accounted close. OrderId={OrderId}, Symbol={Symbol}, Side={Side}, ExecutionCount={ExecutionCount}, ParentPositionId={ParentPositionId}, Reason={Reason}",
                        order.Id,
                        order.Symbol,
                        order.Side,
                        trades.Count,
                        order.ParentPositionId,
                        accounting.Reason);
                    await orderStatusService.TrySetPositionUpdateFailedAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        cancellationToken);
                    continue;
                }

                // TODO: Position upsert + trade processed marking + order status transition should be atomic per order.
                if (!accounting.Position.IsOpen && order.CloseReason != CloseReason.None)
                    accounting.Position.ExitReason = MapExitReason(order.CloseReason);
                accounting.Position.IsClosing = false;

                if (!ValidatePositionInvariants(order, accounting.Position))
                {
                    await orderStatusService.TrySetPositionUpdateFailedAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        cancellationToken);
                    continue;
                }

                await positionRepository.UpsertAsync(accounting.Position, cancellationToken);
                await tradeExecutionRepository.MarkPositionProcessedByOrderAsync(order.Id, DateTime.UtcNow, cancellationToken);
                var processedTrades = await tradeExecutionRepository.GetByOrderIdAsync(order.Id, cancellationToken);
                if (!AreAllExecutionsProcessed(processedTrades))
                {
                    logger.LogError(
                        "PositionWorker failed consistency check: order has unprocessed executions after successful accounting branch. OrderId={OrderId}, Symbol={Symbol}, Side={Side}, ExecutionCount={ExecutionCount}, ParentPositionId={ParentPositionId}",
                        order.Id,
                        order.Symbol,
                        order.Side,
                        processedTrades.Count,
                        order.ParentPositionId);
                    await orderStatusService.TrySetPositionUpdateFailedAsync(
                        order.Id,
                        ProcessingStatus.PositionUpdating,
                        cancellationToken);
                    continue;
                }
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

                logger.LogInformation(
                    "PositionWorker completed order with all executions marked processed. OrderId={OrderId}, Symbol={Symbol}, Side={Side}, ExecutionCount={ExecutionCount}",
                    order.Id,
                    order.Symbol,
                    order.Side,
                    processedTrades.Count);
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
            CloseReason.ManualClose => PositionExitReason.ManualClose,
            CloseReason.Reconciliation => PositionExitReason.Reconciliation,
            CloseReason.OppositeSignal => PositionExitReason.OppositeSignal,
            CloseReason.RiskExit => PositionExitReason.RiskExit,
            CloseReason.Unknown => PositionExitReason.Unknown,
            CloseReason.None => null,
            _ => null
        };
    }

    private static bool AreAllExecutionsProcessed(IReadOnlyList<TradeExecution> trades)
    {
        return trades.Count > 0 && trades.All(t => t.PositionProcessedAt is not null);
    }

    private static async Task<Position?> ResolveParentPositionAsync(
        Order order,
        IPositionRepository positionRepository,
        CancellationToken cancellationToken)
    {
        if (!order.ParentPositionId.HasValue || order.ParentPositionId.Value <= 0)
            return null;

        return await positionRepository.GetByIdAsync(order.ParentPositionId.Value, cancellationToken);
    }

    private static async Task<Position?> ResolvePositionForOrderAsync(
        Order order,
        IPositionRepository positionRepository,
        CancellationToken cancellationToken)
    {
        if (order.ParentPositionId.HasValue && order.ParentPositionId.Value > 0)
            return await positionRepository.GetByIdAsync(order.ParentPositionId.Value, cancellationToken);

        return await positionRepository.GetOpenPositionAsync(order.Symbol, cancellationToken);
    }

    private bool ValidatePositionInvariants(Order order, Position position)
    {
        if (!position.IsOpen)
        {
            if (position.Quantity != 0m)
            {
                logger.LogError(
                    "PositionWorker invariant failed: closed position has non-zero quantity. OrderId={OrderId}, PositionId={PositionId}, Quantity={Quantity}",
                    order.Id,
                    position.Id,
                    position.Quantity);
                return false;
            }

            if (!position.ClosedAt.HasValue)
            {
                logger.LogError(
                    "PositionWorker invariant failed: closed position is missing closed_at. OrderId={OrderId}, PositionId={PositionId}",
                    order.Id,
                    position.Id);
                return false;
            }

            if (!position.ExitPrice.HasValue)
            {
                logger.LogError(
                    "PositionWorker invariant failed: closed position is missing exit_price. OrderId={OrderId}, PositionId={PositionId}",
                    order.Id,
                    position.Id);
                return false;
            }
        }

        if (position.IsClosing)
        {
            logger.LogError(
                "PositionWorker invariant failed: position remained in closing state after accounting. OrderId={OrderId}, PositionId={PositionId}",
                order.Id,
                position.Id);
            return false;
        }

        return true;
    }

    private static bool IsExpectedAlreadyAccountedClosePath(Order order, Position? parentPosition)
    {
        if (order.Side != TradingBot.Domain.Enums.Binance.OrderSide.SELL)
            return false;

        if (parentPosition is null || parentPosition.IsOpen)
            return false;

        var sourceMatches = order.OrderSource is OrderSource.TradeMonitorWorker
            or OrderSource.Manual
            or OrderSource.Api
            or OrderSource.PositionReconciliationWorker;

        var closeReasonMatches = order.CloseReason is CloseReason.StopLoss
            or CloseReason.TakeProfit
            or CloseReason.MaxDuration
            or CloseReason.ManualClose
            or CloseReason.Reconciliation
            or CloseReason.RiskExit
            or CloseReason.Unknown;

        return sourceMatches || closeReasonMatches;
    }
}
