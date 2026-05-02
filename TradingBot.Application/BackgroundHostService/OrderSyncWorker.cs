using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.TradingEndpoints;
using BinanceOrderResponse = TradingBot.Domain.Models.TradingEndpoints.OrderResponse;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Synchronizes order status between Binance and the local database.
/// Fetches orders with Status NEW or PARTIALLY_FILLED, calls GET /api/v3/order, and updates DB.
/// </summary>
public class OrderSyncWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<OrderSyncWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 20;
    private const int DefaultRetryDelaySeconds = 10;
    private const int DefaultBatchSize = 50;
    private const int DefaultRecvWindow = 30000;
    private const bool DefaultEnabled = true;
    private const int DefaultPerOrderTimeoutSeconds = 10;
    private const int DefaultDelayBetweenRequestsMs = 100;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadSettings();
        if (!settings.Enabled)
        {
            logger.LogInformation("OrderSyncWorker is disabled by configuration.");
            return;
        }

        logger.LogInformation(
            "OrderSyncWorker started. Interval={Interval}s, RetryDelay={RetryDelay}s, BatchSize={BatchSize}, RecvWindow={RecvWindow}, PerOrderTimeout={PerOrderTimeout}s, DelayBetweenRequestsMs={DelayBetweenRequestsMs}",
            settings.IntervalSeconds,
            settings.RetryDelaySeconds,
            settings.BatchSize,
            settings.RecvWindow,
            settings.PerOrderTimeoutSeconds,
            settings.DelayBetweenRequestsMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncOrdersAsync(settings, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "OrderSyncWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, settings.RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("OrderSyncWorker stopped.");
    }

    private async Task SyncOrdersAsync(OrderSyncSettings settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var orderStatusService = scope.ServiceProvider.GetRequiredService<IOrderStatusService>();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
        var timeSyncService = scope.ServiceProvider.GetRequiredService<ITimeSyncService>();

        var openOrders = await orderRepository.GetOpenOrdersAsync(null, settings.BatchSize, cancellationToken);
        if (openOrders.Count == 0)
        {
            logger.LogDebug("OrderSyncWorker cycle: no open orders found.");
            return;
        }

        var processedCount = 0;
        var updatedCount = 0;
        var unchangedCount = 0;
        var notFoundCount = 0;
        var timedOutCount = 0;
        var failedCount = 0;
        var skippedNoExchangeIdCount = 0;

        var queryEndpoint = toolService.BinanceEndpointsService.GetEndpoint(TradingBot.Domain.Enums.Endpoints.Trading.QueryOrder);

        foreach (var order in openOrders)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (order.ExchangeOrderId is null)
            {
                logger.LogDebug("Order {OrderId} has no ExchangeOrderId, skipping", order.Id);
                skippedNoExchangeIdCount++;
                continue;
            }

            processedCount++;
            try
            {
                using var orderTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                orderTimeoutCts.CancelAfter(TimeSpan.FromSeconds(settings.PerOrderTimeoutSeconds));
                var perOrderToken = orderTimeoutCts.Token;
                var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(perOrderToken);

                var query = new QueryOrderRequest
                {
                    Symbol = order.Symbol.ToString(),
                    OrderId = order.ExchangeOrderId.Value,
                    Timestamp = adjustedTimestamp,
                    RecvWindow = settings.RecvWindow
                };

                var response = await QueryExchangeOrderAsync(
                    toolService,
                    query,
                    queryEndpoint,
                    TimeSpan.FromSeconds(settings.PerOrderTimeoutSeconds),
                    perOrderToken);

                var exchangeStatus = response.Status.ToOrderStatus();
                var oldStatus = order.Status;
                var statusChanged = exchangeStatus != oldStatus;

                TryParseDecimal(response.ExecutedQty, out var executedQuantity);
                TryParseDecimal(response.CummulativeQuoteQty, out var cumulativeQuoteQuantity);
                var averageFillPrice = (executedQuantity > 0m && cumulativeQuoteQuantity > 0m)
                    ? cumulativeQuoteQuantity / executedQuantity
                    : 0m;
                TryParseDecimal(response.Price, out var exchangePrice);
                TryParseDecimal(response.StopPrice, out var exchangeStopPrice);

                var hasExecutedFieldSupport = false;
                var hasCumulativeFieldSupport = false;
                var hasAverageFillPriceSupport = false;
                var changed = statusChanged;
                // TODO: Add Order model fields and persistence for:
                // ExecutedQuantity, CumulativeQuoteQuantity, AverageFillPrice.
                // Once added, compare those persisted values here and include them in changed detection.

                if (!changed)
                {
                    unchangedCount++;
                    continue;
                }

                order.Status = exchangeStatus;
                order.UpdatedAt = DateTime.UtcNow;
                await orderRepository.UpdateAsync(order, cancellationToken);
                updatedCount++;

                logger.LogInformation(
                    "OrderSync update: LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, Symbol={Symbol}, Side={Side}, OldStatus={OldStatus}, NewStatus={NewStatus}, OriginalQuantity={OriginalQuantity}, ExecutedQuantity={ExecutedQuantity}, AverageFillPrice={AverageFillPrice}",
                    order.Id,
                    order.ExchangeOrderId,
                    order.Symbol,
                    order.Side,
                    oldStatus,
                    exchangeStatus,
                    order.Quantity,
                    executedQuantity,
                    averageFillPrice);

                if (exchangeStatus is OrderStatuses.FILLED or OrderStatuses.PARTIALLY_FILLED)
                {
                    var movedToPending = await orderStatusService.TryUpdateProcessingStatusAsync(
                        order.Id, ProcessingStatus.OrderPlaced, ProcessingStatus.TradesSyncPending, cancellationToken);
                    if (!movedToPending)
                    {
                        await orderStatusService.TryUpdateProcessingStatusAsync(
                            order.Id, ProcessingStatus.TradesSyncFailed, ProcessingStatus.TradesSyncPending, cancellationToken);
                    }
                }

                if (exchangeStatus is OrderStatuses.CANCELED or OrderStatuses.EXPIRED or OrderStatuses.REJECTED or OrderStatuses.EXPIRED_IN_MATCH)
                {
                    await MoveToTerminalProcessingStatusAsync(orderStatusService, order.Id, cancellationToken);
                    // TODO: Introduce explicit terminal/manual-review processing statuses if needed.
                }

                _ = hasExecutedFieldSupport;
                _ = hasCumulativeFieldSupport;
                _ = hasAverageFillPriceSupport;
                _ = exchangePrice;
                _ = exchangeStopPrice;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "OrderSyncWorker timed out while syncing Order {OrderId}, ExchangeOrderId {ExchangeOrderId}.",
                    order.Id, order.ExchangeOrderId);
                timedOutCount++;
            }
            catch (Exception ex)
            {
                if (IsOrderNotFoundError(ex))
                {
                    logger.LogWarning(
                        "OrderSyncWorker order not found on exchange: LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, Symbol={Symbol}",
                        order.Id,
                        order.ExchangeOrderId,
                        order.Symbol);
                    // TODO: Mark missing exchange orders as Unknown/NeedsManualReview when model supports it.
                    notFoundCount++;
                    continue;
                }

                logger.LogError(ex,
                    "OrderSyncWorker failed for Order {OrderId}, ExchangeOrderId {ExchangeOrderId}. Will retry next cycle.",
                    order.Id, order.ExchangeOrderId);
                failedCount++;
            }

            if (settings.DelayBetweenRequestsMs > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(settings.DelayBetweenRequestsMs), cancellationToken);
        }

        logger.LogInformation(
            "OrderSyncWorker cycle summary: Processed={Processed}, Updated={Updated}, Unchanged={Unchanged}, NotFound={NotFound}, TimedOut={TimedOut}, Failed={Failed}, SkippedNoExchangeId={SkippedNoExchangeId}, BatchRequested={BatchRequested}",
            processedCount,
            updatedCount,
            unchangedCount,
            notFoundCount,
            timedOutCount,
            failedCount,
            skippedNoExchangeIdCount,
            openOrders.Count);
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0m;
            return false;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out result);
    }

    private static bool IsOrderNotFoundError(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("order does not exist", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unknown order", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<BinanceOrderResponse> QueryExchangeOrderAsync(
        IToolService toolService,
        QueryOrderRequest query,
        TradingBot.Shared.Shared.Models.Endpoint queryEndpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var callTask = toolService.BinanceClientService.Call<BinanceOrderResponse, QueryOrderRequest>(query, queryEndpoint, true);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(callTask, timeoutTask);
        if (completed != callTask)
            throw new TimeoutException("Timed out waiting for Binance order query response.");

        return await callTask;
    }

    private static async Task MoveToTerminalProcessingStatusAsync(
        IOrderStatusService orderStatusService,
        long orderId,
        CancellationToken cancellationToken)
    {
        var expectedCandidates = new[]
        {
            ProcessingStatus.OrderPlaced,
            ProcessingStatus.TradesSyncPending,
            ProcessingStatus.TradesSyncInProgress,
            ProcessingStatus.TradesSyncFailed,
            ProcessingStatus.PositionUpdatePending,
            ProcessingStatus.PositionUpdating,
            ProcessingStatus.PositionUpdateFailed
        };

        foreach (var expected in expectedCandidates)
        {
            var moved = await orderStatusService.TryUpdateProcessingStatusAsync(
                orderId,
                expected,
                ProcessingStatus.Completed,
                cancellationToken);
            if (moved)
                return;
        }
    }

    private OrderSyncSettings ReadSettings()
    {
        var intervalSeconds = Math.Max(1, configuration.GetValue<int?>("OrderSync:IntervalSeconds") ?? DefaultIntervalSeconds);
        var retryDelaySeconds = Math.Max(1, configuration.GetValue<int?>("OrderSync:RetryDelaySeconds") ?? DefaultRetryDelaySeconds);
        var batchSize = Math.Max(1, configuration.GetValue<int?>("OrderSync:BatchSize") ?? DefaultBatchSize);
        var recvWindow = Math.Max(1, configuration.GetValue<long?>("OrderSync:RecvWindow") ?? DefaultRecvWindow);
        var enabled = configuration.GetValue<bool?>("OrderSync:Enabled") ?? DefaultEnabled;
        var perOrderTimeoutSeconds = Math.Max(1, configuration.GetValue<int?>("OrderSync:PerOrderTimeoutSeconds") ?? DefaultPerOrderTimeoutSeconds);
        var delayBetweenRequestsMs = Math.Max(0, configuration.GetValue<int?>("OrderSync:DelayBetweenRequestsMs") ?? DefaultDelayBetweenRequestsMs);

        return new OrderSyncSettings
        {
            IntervalSeconds = intervalSeconds,
            RetryDelaySeconds = retryDelaySeconds,
            BatchSize = batchSize,
            RecvWindow = recvWindow,
            Enabled = enabled,
            PerOrderTimeoutSeconds = perOrderTimeoutSeconds,
            DelayBetweenRequestsMs = delayBetweenRequestsMs
        };
    }

    private sealed class OrderSyncSettings
    {
        public int IntervalSeconds { get; init; }
        public int RetryDelaySeconds { get; init; }
        public int BatchSize { get; init; }
        public long RecvWindow { get; init; }
        public bool Enabled { get; init; }
        public int PerOrderTimeoutSeconds { get; init; }
        public int DelayBetweenRequestsMs { get; init; }
    }
}
