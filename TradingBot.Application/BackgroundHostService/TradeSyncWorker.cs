using System.Data;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Syncs trades from Binance for orders in TradesSyncPending or TradesSyncInProgress (or TradesSyncFailed retry).
/// Uses FOR UPDATE SKIP LOCKED to claim batches of 50; inserts TradeExecution rows and advances to TradesSynced.
/// </summary>
public class TradeSyncWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TradeSyncWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 30;
    private const int DefaultRetryDelaySeconds = 10;
    private const int DefaultMaxSyncRetryCount = 5;
    private const int DefaultBatchSize = 50;
    private const long DefaultRecvWindow = 30000;
    private const bool DefaultEnabled = true;
    private const int DefaultDelayBetweenRequestsMs = 100;
    private const int DefaultPerOrderTimeoutSeconds = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadSettings();
        if (!settings.Enabled)
        {
            logger.LogInformation("TradeSyncWorker is disabled by configuration.");
            return;
        }

        logger.LogInformation(
            "TradeSyncWorker started. Interval={Interval}s, MaxSyncRetryCount={MaxRetry}, BatchSize={BatchSize}, RecvWindow={RecvWindow}, DelayBetweenRequestsMs={DelayBetweenRequestsMs}, PerOrderTimeoutSeconds={PerOrderTimeoutSeconds}",
            settings.IntervalSeconds,
            settings.MaxSyncRetryCount,
            settings.BatchSize,
            settings.RecvWindow,
            settings.DelayBetweenRequestsMs,
            settings.PerOrderTimeoutSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncTradesAsync(settings, stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TradeSyncWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, settings.RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(settings.RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("TradeSyncWorker stopped.");
    }

    private async Task SyncTradesAsync(TradeSyncSettings settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var tradeExecutionRepository = scope.ServiceProvider.GetRequiredService<ITradeExecutionRepository>();
        var orderStatusService = scope.ServiceProvider.GetRequiredService<IOrderStatusService>();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
        var timeSyncService = scope.ServiceProvider.GetRequiredService<ITimeSyncService>();

        var toSyncById = new Dictionary<long, Order>();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        using (var trans = connection.BeginTransaction())
        {
            var pending = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(trans, ProcessingStatus.TradesSyncPending, settings.BatchSize, cancellationToken);
            foreach (var o in pending)
            {
                var claimed = await orderStatusService.TryUpdateProcessingStatusAsync(
                    o.Id,
                    ProcessingStatus.TradesSyncPending,
                    ProcessingStatus.TradesSyncInProgress,
                    cancellationToken,
                    trans);
                if (claimed)
                    toSyncById[o.Id] = o;
            }
            trans.Commit();
        }

        using (var trans = connection.BeginTransaction())
        {
            var failed = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(trans, ProcessingStatus.TradesSyncFailed, settings.BatchSize, cancellationToken);
            foreach (var o in failed.Where(o => o.SyncRetryCount < settings.MaxSyncRetryCount))
            {
                var updated = await orderStatusService.TryUpdateProcessingStatusAsync(o.Id, ProcessingStatus.TradesSyncFailed, ProcessingStatus.TradesSyncInProgress, cancellationToken, trans);
                if (updated)
                {
                    toSyncById[o.Id] = o;
                    logger.LogInformation("TradeSyncWorker retrying failed Order {OrderId} (retry count was {RetryCount})", o.Id, o.SyncRetryCount);
                }
            }
            trans.Commit();
        }

        var inProgress = await orderRepository.GetOrdersByProcessingStatusAsync(ProcessingStatus.TradesSyncInProgress, settings.BatchSize, cancellationToken);
        foreach (var order in inProgress)
            toSyncById[order.Id] = order;

        var toSync = toSyncById.Values.ToList();
        if (toSync.Count == 0)
            return;

        var tradesEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.Trades);

        foreach (var order in toSync)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (order.ExchangeOrderId is null)
            {
                await orderStatusService.TrySetTradesSyncFailedAsync(order.Id, ProcessingStatus.TradesSyncInProgress, cancellationToken);
                logger.LogWarning("Order has no ExchangeOrderId; cannot sync trades. OrderId={OrderId}", order.Id);
                continue;
            }

            try
            {
                using var perOrderCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                perOrderCts.CancelAfter(TimeSpan.FromSeconds(settings.PerOrderTimeoutSeconds));
                var perOrderToken = perOrderCts.Token;
                var adjustedTimestamp = await timeSyncService.GetAdjustedTimestampAsync(perOrderToken);

                var request = new TradesRequest
                {
                    Symbol = order.Symbol.ToString(),
                    OrderId = order.ExchangeOrderId,
                    Timestamp = adjustedTimestamp,
                    RecvWindow = settings.RecvWindow
                };

                var trades = await FetchTradesWithTimeoutAsync(
                    toolService,
                    request,
                    tradesEndpoint,
                    TimeSpan.FromSeconds(settings.PerOrderTimeoutSeconds),
                    perOrderToken);

                var orderTrades = trades.Where(x => x.OrderId == order.ExchangeOrderId).ToList();
                var inserted = 0;

                foreach (var trade in orderTrades)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var existing = await tradeExecutionRepository.GetByExchangeTradeIdAsync(trade.Id, cancellationToken);
                    if (existing != null)
                        continue;

                    if (!Enum.TryParse<TradingSymbol>(trade.Symbol, ignoreCase: false, out var symbol))
                        continue;

                    if (!TryParseDecimal(trade.Price, out var price))
                    {
                        logger.LogWarning(
                            "TradeSyncWorker skipped trade due to invalid price. OrderId={OrderId}, ExchangeTradeId={ExchangeTradeId}, RawPrice={RawPrice}",
                            order.Id, trade.Id, trade.Price);
                        continue;
                    }

                    if (!TryParseDecimal(trade.Qty, out var quantity))
                    {
                        logger.LogWarning(
                            "TradeSyncWorker skipped trade due to invalid quantity. OrderId={OrderId}, ExchangeTradeId={ExchangeTradeId}, RawQty={RawQty}",
                            order.Id, trade.Id, trade.Qty);
                        continue;
                    }

                    if (quantity <= 0m)
                        continue;

                    TryParseDecimal(trade.QuoteQty, out var quoteQty);
                    var resolvedQuoteQty = quoteQty > 0m ? quoteQty : price * quantity;
                    TryParseDecimal(trade.Commission, out var commission);

                    var side = trade.IsBuyer ? OrderSide.BUY : OrderSide.SELL;
                    var execution = new TradeExecution
                    {
                        OrderId = order.Id,
                        ExchangeOrderId = trade.OrderId,
                        ExchangeTradeId = trade.Id,
                        Symbol = symbol,
                        Side = side,
                        Price = price,
                        Quantity = quantity,
                        QuoteQuantity = resolvedQuoteQty,
                        Fee = commission,
                        FeeAsset = trade.CommissionAsset,
                        ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(trade.Time).UtcDateTime
                    };
                    // TODO: Persist additional trade fields when model supports them:
                    // Fee, FeeAsset, QuoteQuantity.
                    try
                    {
                        await tradeExecutionRepository.InsertAsync(execution, cancellationToken);
                        inserted++;
                    }
                    catch (Exception ex) when (IsDuplicateTradeInsert(ex))
                    {
                        // DB-level unique guard can reject racing inserts; this is safe to ignore.
                        logger.LogDebug(
                            "TradeSyncWorker duplicate trade insert ignored. OrderId={OrderId}, ExchangeTradeId={ExchangeTradeId}",
                            order.Id,
                            trade.Id);
                    }
                }

                var allExecutions = await tradeExecutionRepository.GetByOrderIdAsync(order.Id, cancellationToken);
                var executedQuantity = allExecutions.Sum(t => t.Quantity);
                var cumulativeQuote = allExecutions.Sum(t => t.Price * t.Quantity);
                var averageFillPrice = executedQuantity > 0m ? cumulativeQuote / executedQuantity : 0m;
                // TODO: Persist order aggregate fill fields when Order model supports them:
                // ExecutedQuantity, CumulativeQuoteQuantity, AverageFillPrice.

                if (IsFinalOrderStatus(order.Status))
                {
                    await orderStatusService.TryUpdateProcessingStatusAsync(
                        order.Id,
                        ProcessingStatus.TradesSyncInProgress,
                        ProcessingStatus.TradesSynced,
                        cancellationToken);
                }
                else
                {
                    await orderStatusService.TryUpdateProcessingStatusAsync(
                        order.Id,
                        ProcessingStatus.TradesSyncInProgress,
                        ProcessingStatus.TradesSyncPending,
                        cancellationToken);
                }

                logger.LogInformation(
                    "TradeSyncWorker synced order: OrderId={OrderId}, ExchangeOrderId={ExchangeOrderId}, Symbol={Symbol}, Status={Status}, InsertedTrades={InsertedTrades}, TotalTradesFromExchange={TotalTradesFromExchange}, ExecutedQuantity={ExecutedQuantity}, AverageFillPrice={AverageFillPrice}",
                    order.Id,
                    order.ExchangeOrderId,
                    order.Symbol,
                    order.Status,
                    inserted,
                    orderTrades.Count,
                    executedQuantity,
                    averageFillPrice);
                // TODO: Wrap trade inserts + aggregate update + status transition in one DB transaction when repository supports transaction-aware methods.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    "TradeSyncWorker timed out while syncing Order {OrderId}, ExchangeOrderId={ExchangeOrderId}",
                    order.Id,
                    order.ExchangeOrderId);
                await HandleSyncFailureAsync(orderRepository, orderStatusService, order, settings.MaxSyncRetryCount, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "TradeSyncWorker failed for Order {OrderId}, ExchangeOrderId {ExchangeOrderId}. Setting TradesSyncFailed (retry count will increment).",
                    order.Id, order.ExchangeOrderId);
                await HandleSyncFailureAsync(orderRepository, orderStatusService, order, settings.MaxSyncRetryCount, cancellationToken);
            }

            if (settings.DelayBetweenRequestsMs > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(settings.DelayBetweenRequestsMs), cancellationToken);
        }
    }

    private static async Task<List<TradeResponse>> FetchTradesWithTimeoutAsync(
        IToolService toolService,
        TradesRequest request,
        TradingBot.Shared.Shared.Models.Endpoint tradesEndpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var callTask = toolService.BinanceClientService.Call<List<TradeResponse>, TradesRequest>(request, tradesEndpoint, true);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        var completed = await Task.WhenAny(callTask, timeoutTask);
        if (completed != callTask)
            throw new TimeoutException("Timed out while fetching exchange trades.");

        return await callTask;
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

    private static bool IsFinalOrderStatus(OrderStatuses status)
    {
        return status is OrderStatuses.FILLED
            or OrderStatuses.CANCELED
            or OrderStatuses.EXPIRED
            or OrderStatuses.REJECTED
            or OrderStatuses.EXPIRED_IN_MATCH;
    }

    private static bool IsDuplicateTradeInsert(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleSyncFailureAsync(
        IOrderRepository orderRepository,
        IOrderStatusService orderStatusService,
        Order order,
        int maxSyncRetryCount,
        CancellationToken cancellationToken)
    {
        await orderStatusService.TrySetTradesSyncFailedAsync(order.Id, ProcessingStatus.TradesSyncInProgress, cancellationToken);
        var latest = await orderRepository.GetByIdAsync(order.Id, cancellationToken);
        if (latest?.SyncRetryCount >= maxSyncRetryCount)
        {
            logger.LogError(
                "TradeSyncWorker max retries reached. OrderId={OrderId}, ExchangeOrderId={ExchangeOrderId}, RetryCount={RetryCount}. Manual review required.",
                order.Id,
                order.ExchangeOrderId,
                latest.SyncRetryCount);
            // TODO: Add dedicated NeedsManualReview/terminal processing status and LastSyncError persistence.
        }
    }

    private TradeSyncSettings ReadSettings()
    {
        var intervalSeconds = Math.Max(1, configuration.GetValue<int?>("TradeSync:IntervalSeconds") ?? DefaultIntervalSeconds);
        var retryDelaySeconds = Math.Max(1, configuration.GetValue<int?>("TradeSync:RetryDelaySeconds") ?? DefaultRetryDelaySeconds);
        var maxSyncRetryCount = Math.Max(1, configuration.GetValue<int?>("TradeSync:MaxSyncRetryCount") ?? DefaultMaxSyncRetryCount);
        var batchSize = Math.Max(1, configuration.GetValue<int?>("TradeSync:BatchSize") ?? DefaultBatchSize);
        var recvWindow = Math.Max(1, configuration.GetValue<long?>("TradeSync:RecvWindow") ?? DefaultRecvWindow);
        var enabled = configuration.GetValue<bool?>("TradeSync:Enabled") ?? DefaultEnabled;
        var delayBetweenRequestsMs = Math.Max(0, configuration.GetValue<int?>("TradeSync:DelayBetweenRequestsMs") ?? DefaultDelayBetweenRequestsMs);
        var perOrderTimeoutSeconds = Math.Max(1, configuration.GetValue<int?>("TradeSync:PerOrderTimeoutSeconds") ?? DefaultPerOrderTimeoutSeconds);

        return new TradeSyncSettings
        {
            IntervalSeconds = intervalSeconds,
            RetryDelaySeconds = retryDelaySeconds,
            MaxSyncRetryCount = maxSyncRetryCount,
            BatchSize = batchSize,
            RecvWindow = recvWindow,
            Enabled = enabled,
            DelayBetweenRequestsMs = delayBetweenRequestsMs,
            PerOrderTimeoutSeconds = perOrderTimeoutSeconds
        };
    }

    private sealed class TradeSyncSettings
    {
        public int IntervalSeconds { get; init; }
        public int RetryDelaySeconds { get; init; }
        public int MaxSyncRetryCount { get; init; }
        public int BatchSize { get; init; }
        public long RecvWindow { get; init; }
        public bool Enabled { get; init; }
        public int DelayBetweenRequestsMs { get; init; }
        public int PerOrderTimeoutSeconds { get; init; }
    }
}
