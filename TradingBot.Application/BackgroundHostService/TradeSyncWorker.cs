using System.Data;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.AccountInformation;
using TradingBot.Domain.Models.GeneralApis;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.BackgroundHostService;

/// <summary>
/// Syncs trades from Binance for orders in TradesSyncPending or TradesSyncInProgress (or TradesSyncFailed retry).
/// Uses FOR UPDATE SKIP LOCKED to claim batches of 50; inserts TradeExecution rows and advances to TradesSynced.
/// </summary>
public class TradeSyncWorker(IServiceScopeFactory scopeFactory, ILogger<TradeSyncWorker> logger) : BackgroundService
{
    private const int IntervalSeconds = 30;
    private const int RetryDelaySeconds = 10;
    private const int MaxSyncRetryCount = 5;
    private const int BatchSize = 50;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TradeSyncWorker started. Interval: {Interval}s, MaxSyncRetryCount: {MaxRetry}",
            IntervalSeconds, MaxSyncRetryCount);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncTradesAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TradeSyncWorker cycle failed at {Time}. Retrying in {Delay}s",
                    DateTime.UtcNow, RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds), stoppingToken);
            }
        }

        logger.LogInformation("TradeSyncWorker stopped.");
    }

    private async Task SyncTradesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var connection = scope.ServiceProvider.GetRequiredService<IDbConnection>();
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var tradeExecutionRepository = scope.ServiceProvider.GetRequiredService<ITradeExecutionRepository>();
        var orderStatusService = scope.ServiceProvider.GetRequiredService<IOrderStatusService>();
        var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();

        var toSync = new List<Order>();

        if (connection.State != ConnectionState.Open)
            connection.Open();

        using (var trans = connection.BeginTransaction())
        {
            var pending = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(trans, ProcessingStatus.TradesSyncPending, BatchSize, cancellationToken);
            foreach (var o in pending)
            {
                await orderStatusService.TryUpdateProcessingStatusAsync(o.Id, ProcessingStatus.TradesSyncPending, ProcessingStatus.TradesSyncInProgress, cancellationToken, trans);
                toSync.Add(o);
            }
            trans.Commit();
        }

        using (var trans = connection.BeginTransaction())
        {
            var failed = await orderRepository.GetOrdersByProcessingStatusForWorkerAsync(trans, ProcessingStatus.TradesSyncFailed, BatchSize, cancellationToken);
            foreach (var o in failed.Where(o => o.SyncRetryCount < MaxSyncRetryCount))
            {
                var updated = await orderStatusService.TryUpdateProcessingStatusAsync(o.Id, ProcessingStatus.TradesSyncFailed, ProcessingStatus.TradesSyncInProgress, cancellationToken, trans);
                if (updated)
                {
                    toSync.Add(o);
                    logger.LogInformation("TradeSyncWorker retrying failed Order {OrderId} (retry count was {RetryCount})", o.Id, o.SyncRetryCount);
                }
            }
            trans.Commit();
        }

        var inProgress = await orderRepository.GetOrdersByProcessingStatusAsync(ProcessingStatus.TradesSyncInProgress, BatchSize, cancellationToken);
        toSync.AddRange(inProgress);

        if (toSync.Count == 0)
            return;

        var serverTimeEndpoint = toolService.BinanceEndpointsService.GetEndpoint(GeneralApis.CheckServerTime);
        var serverTime = await toolService.BinanceClientService.Call<ServerTimeResponse, EmptyRequest>(
            null, serverTimeEndpoint, false);
        var tradesEndpoint = toolService.BinanceEndpointsService.GetEndpoint(Account.Trades);

        foreach (var order in toSync)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (order.ExchangeOrderId is null)
            {
                await orderStatusService.TryUpdateProcessingStatusAsync(order.Id, ProcessingStatus.TradesSyncInProgress, ProcessingStatus.TradesSynced, cancellationToken);
                logger.LogDebug("Order {OrderId} has no ExchangeOrderId, marked TradesSynced", order.Id);
                continue;
            }

            try
            {
                var request = new TradesRequest
                {
                    Symbol = order.Symbol.ToString(),
                    OrderId = order.ExchangeOrderId,
                    Timestamp = serverTime.ServerTime,
                    RecvWindow = 30000
                };

                var trades = await toolService.BinanceClientService.Call<List<TradeResponse>, TradesRequest>(
                    request, tradesEndpoint, true);

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

                    var side = trade.IsBuyer ? OrderSide.BUY : OrderSide.SELL;
                    var execution = new TradeExecution
                    {
                        OrderId = order.Id,
                        ExchangeOrderId = trade.OrderId,
                        ExchangeTradeId = trade.Id,
                        Symbol = symbol,
                        Side = side,
                        Price = decimal.Parse(trade.Price, CultureInfo.InvariantCulture),
                        Quantity = decimal.Parse(trade.Qty, CultureInfo.InvariantCulture),
                        ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(trade.Time).UtcDateTime
                    };
                    await tradeExecutionRepository.InsertAsync(execution, cancellationToken);
                    inserted++;
                }

                await orderStatusService.TryUpdateProcessingStatusAsync(order.Id, ProcessingStatus.TradesSyncInProgress, ProcessingStatus.TradesSynced, cancellationToken);
                logger.LogInformation(
                    "Order {OrderId} trades synced: {Inserted} new trades, total for order: {Total}. Status -> TradesSynced",
                    order.Id, inserted, orderTrades.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "TradeSyncWorker failed for Order {OrderId}, ExchangeOrderId {ExchangeOrderId}. Setting TradesSyncFailed (retry count will increment).",
                    order.Id, order.ExchangeOrderId);
                await orderStatusService.TrySetTradesSyncFailedAsync(order.Id, ProcessingStatus.TradesSyncInProgress, cancellationToken);
            }
        }
    }
}
