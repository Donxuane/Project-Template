using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Extentions;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;
using TradingOrder = TradingBot.Domain.Models.Trading.Order;

namespace TradingBot.Application.SpotFuturesCrossMarket;

public sealed class SpotFuturesCrossMarketCloseOrderService(
    IFuturesTestnetClient futuresClient,
    IOrderRepository orderRepository,
    ITradeExecutionRepository executionRepository,
    IPositionRepository positionRepository,
    SpotFuturesCrossMarketAccounting accounting,
    IConnectionMultiplexer redis,
    ILogger<SpotFuturesCrossMarketCloseOrderService> logger)
{
    public async Task<SpotFuturesCrossMarketCloseResult> CloseAsync(
        SpotFuturesCrossMarketCloseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.Settings.CanPlaceTestnetOrders)
        {
            logger.LogWarning(
                "SpotFuturesCrossMarket close skipped: testnet orders not allowed by config. PositionId={PositionId}",
                request.Position.Id);
            return SpotFuturesCrossMarketCloseResult.Failed("TestnetOrdersNotAllowed");
        }

        var lockKey = CloseLockKey(request.Position.Id);
        var lockValue = Guid.NewGuid().ToString("N");
        var db = redis.GetDatabase();
        var lockAcquired = false;

        try
        {
            lockAcquired = await db.StringSetAsync(
                lockKey,
                lockValue,
                TimeSpan.FromSeconds(Math.Max(10, request.CloseLockSeconds)),
                when: When.NotExists);

            if (!lockAcquired)
            {
                logger.LogWarning(
                    "SpotFuturesCrossMarket close skipped: per-position Redis lock already held. PositionId={PositionId} CorrelationId={CorrelationId}",
                    request.Position.Id,
                    request.CorrelationId);
                return SpotFuturesCrossMarketCloseResult.Duplicate("CloseLockAlreadyHeld");
            }

            if (await orderRepository.HasActiveCloseOrderForPositionByEnvironmentAsync(
                    request.Position.Id,
                    SpotFuturesCrossMarketSettings.ExecutionEnvironment,
                    cancellationToken))
            {
                logger.LogWarning(
                    "SpotFuturesCrossMarket close skipped: active environment-scoped close order already exists. PositionId={PositionId}",
                    request.Position.Id);
                return SpotFuturesCrossMarketCloseResult.Duplicate("ActiveCloseOrderAlreadyExists");
            }

            var markedClosing = await positionRepository.TryMarkPositionClosingAsync(request.Position.Id, cancellationToken);
            if (!markedClosing)
            {
                logger.LogWarning(
                    "SpotFuturesCrossMarket close skipped: position is already closing, closed, or invalid. PositionId={PositionId}",
                    request.Position.Id);
                return SpotFuturesCrossMarketCloseResult.Duplicate("PositionAlreadyClosingOrClosed");
            }

            try
            {
                return await SubmitReduceOnlyCloseAsync(request, cancellationToken);
            }
            catch
            {
                await positionRepository.ClearPositionClosingAsync(request.Position.Id, cancellationToken);
                throw;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "SpotFuturesCrossMarket close order placement failed. PositionId={PositionId} CorrelationId={CorrelationId}",
                request.Position.Id,
                request.CorrelationId);
            return SpotFuturesCrossMarketCloseResult.Failed($"CloseOrderPlacementFailed: {ex.Message}");
        }
        finally
        {
            if (lockAcquired)
            {
                try
                {
                    var current = await db.StringGetAsync(lockKey);
                    if (current == lockValue)
                        await db.KeyDeleteAsync(lockKey);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "SpotFuturesCrossMarket close lock cleanup failed. PositionId={PositionId}", request.Position.Id);
                }
            }
        }
    }

    private async Task<SpotFuturesCrossMarketCloseResult> SubmitReduceOnlyCloseAsync(
        SpotFuturesCrossMarketCloseRequest request,
        CancellationToken cancellationToken)
    {
        var position = request.Position;
        var closeSide = position.Side == OrderSide.BUY ? OrderSide.SELL : OrderSide.BUY;
        var closeQuantity = await ResolveCloseQuantityAsync(position, request.RequestedQuantity, cancellationToken);
        if (closeQuantity.Quantity <= 0m)
        {
            await positionRepository.ClearPositionClosingAsync(position.Id, cancellationToken);
            return SpotFuturesCrossMarketCloseResult.Failed("NoExchangeConfirmedRemainingQuantity");
        }

        logger.LogInformation(
            "SpotFuturesCrossMarket ORDER INTENT (exit). PositionId={PositionId} Symbol={Symbol} Side={Side} Quantity={Quantity} ExitReason={ExitReason} CorrelationId={CorrelationId} Reason={Reason}",
            position.Id,
            position.Symbol,
            closeSide,
            closeQuantity.Quantity,
            request.ExitReason,
            request.CorrelationId,
            request.Reason);

        var submittedAt = DateTime.UtcNow;
        var result = await futuresClient.PlaceMarketOrderAsync(
            position.Symbol.ToString(),
            closeSide,
            closeQuantity.Quantity,
            reduceOnly: true,
            cancellationToken,
            positionSide: closeQuantity.PositionSide);
        var acknowledgedAt = DateTime.UtcNow;
        var (fills, avgPrice, filledQty, exitFee) = await ResolveFillAsync(position.Symbol.ToString(), result, cancellationToken);
        var filledAt = DateTime.UtcNow;
        var exitPrice = avgPrice > 0m ? avgPrice : position.AveragePrice;
        var executedQty = filledQty > 0m ? filledQty : closeQuantity.Quantity;

        var order = new TradingOrder
        {
            ExchangeOrderId = result.OrderId,
            CorrelationId = request.CorrelationId,
            ParentPositionId = position.Id,
            OrderSource = request.OrderSource,
            CloseReason = request.CloseReason,
            Symbol = position.Symbol,
            Side = closeSide,
            Status = result.Status.ToOrderStatus(),
            ProcessingStatus = ProcessingStatus.PositionUpdated,
            Price = exitPrice,
            Quantity = executedQty,
            ExecutionEnvironment = SpotFuturesCrossMarketSettings.ExecutionEnvironment
        };
        await orderRepository.InsertAsync(order, cancellationToken);
        await PersistExecutionsAsync(order, fills, result, position.Symbol, closeSide, exitPrice, executedQty, cancellationToken);

        var closedPosition = await accounting.CloseAsync(position, exitPrice, exitFee, request.ExitReason, DateTime.UtcNow, cancellationToken);
        var gross = SpotFuturesCrossMarketAccounting.GrossPnl(position.Side, position.AveragePrice, exitPrice, executedQty);

        logger.LogInformation(
            "SpotFuturesCrossMarket EXIT FILLED. PositionId={PositionId} ExitReason={ExitReason} ExitPrice={ExitPrice} RealizedPnl={RealizedPnl} Reason={Reason} LocalOrderId={LocalOrderId} ExchangeOrderId={ExchangeOrderId} DecisionToSubmitMs={DecisionToSubmitMs} SubmitToFillMs={SubmitToFillMs}",
            closedPosition.Id,
            request.ExitReason,
            exitPrice,
            closedPosition.RealizedPnl,
            request.Reason,
            order.Id,
            order.ExchangeOrderId,
            (submittedAt - request.DecisionAcceptedAtUtc).TotalMilliseconds,
            (filledAt - submittedAt).TotalMilliseconds);

        return SpotFuturesCrossMarketCloseResult.Succeeded(
            order,
            closedPosition,
            gross,
            closedPosition.RealizedPnl,
            submittedAt,
            acknowledgedAt,
            filledAt);
    }

    private async Task<CloseQuantityResolution> ResolveCloseQuantityAsync(
        Position position,
        decimal? requestedQuantity,
        CancellationToken cancellationToken)
    {
        var localQuantity = requestedQuantity is > 0m
            ? Math.Min(requestedQuantity.Value, position.Quantity)
            : position.Quantity;

        decimal exchangeQuantity = 0m;
        string? positionSide = null;
        try
        {
            var risk = await futuresClient.GetPositionRiskAsync(position.Symbol.ToString(), cancellationToken);
            if (risk is not null)
            {
                exchangeQuantity = Math.Abs(risk.PositionAmt);
                if (!string.Equals(risk.PositionSide, "BOTH", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(risk.PositionSide))
                {
                    positionSide = position.Side == OrderSide.BUY ? "LONG" : "SHORT";
                }

                logger.LogInformation(
                    "SpotFuturesCrossMarket exchange position reconciliation before close. PositionId={PositionId} Symbol={Symbol} LocalQty={LocalQty} ExchangeQty={ExchangeQty} PositionSide={PositionSide}",
                    position.Id,
                    position.Symbol,
                    position.Quantity,
                    exchangeQuantity,
                    risk.PositionSide);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "SpotFuturesCrossMarket exchange position reconciliation failed before close; falling back to local remaining quantity. PositionId={PositionId}",
                position.Id);
        }

        var rawQuantity = exchangeQuantity > 0m ? Math.Min(localQuantity, exchangeQuantity) : localQuantity;
        if (rawQuantity <= 0m)
            return new CloseQuantityResolution(0m, positionSide);

        try
        {
            var filters = await futuresClient.GetSymbolFiltersAsync(position.Symbol.ToString(), cancellationToken);
            var step = filters.QuantityStepSize > 0m ? filters.QuantityStepSize : 0m;
            if (step > 0m)
                rawQuantity = Math.Floor(rawQuantity / step) * step;

            if (filters.MinQuantity > 0m && rawQuantity < filters.MinQuantity)
                return new CloseQuantityResolution(0m, positionSide);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpotFuturesCrossMarket close quantity normalization failed; using local quantity. PositionId={PositionId}", position.Id);
        }

        return new CloseQuantityResolution(rawQuantity, positionSide);
    }

    private async Task<(IReadOnlyList<FuturesTestnetUserTrade> Fills, decimal AvgPrice, decimal ExecutedQty, decimal Fee)> ResolveFillAsync(
        string symbol,
        FuturesTestnetOrderResult placement,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FuturesTestnetUserTrade> fills = Array.Empty<FuturesTestnetUserTrade>();

        for (var attempt = 0; attempt < 4 && fills.Count == 0; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);

            try
            {
                fills = await futuresClient.GetUserTradesAsync(symbol, placement.OrderId, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "SpotFuturesCrossMarket fill query failed (attempt {Attempt}). OrderId={OrderId}", attempt + 1, placement.OrderId);
            }
        }

        if (fills.Count > 0)
            return (fills, Vwap(fills), fills.Sum(f => f.Qty), fills.Sum(f => f.Commission));

        if (placement.AvgPrice > 0m)
            return (fills, placement.AvgPrice, placement.ExecutedQty, 0m);

        try
        {
            var order = await futuresClient.GetOrderAsync(symbol, placement.OrderId, cancellationToken);
            if (order.AvgPrice > 0m)
            {
                logger.LogInformation(
                    "SpotFuturesCrossMarket fill resolved via order re-query. OrderId={OrderId} AvgPrice={AvgPrice} ExecutedQty={ExecutedQty}",
                    placement.OrderId,
                    order.AvgPrice,
                    order.ExecutedQty);
                return (fills, order.AvgPrice, order.ExecutedQty, 0m);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpotFuturesCrossMarket order re-query failed. OrderId={OrderId}", placement.OrderId);
        }

        logger.LogWarning(
            "SpotFuturesCrossMarket could not resolve fill price for OrderId={OrderId}; caller will fall back to a reference price. Recorded PnL may be inexact for this trade.",
            placement.OrderId);
        return (fills, 0m, placement.ExecutedQty, 0m);
    }

    private async Task PersistExecutionsAsync(
        TradingOrder order,
        IReadOnlyList<FuturesTestnetUserTrade> fills,
        FuturesTestnetOrderResult result,
        TradingSymbol symbol,
        OrderSide side,
        decimal fallbackPrice,
        decimal fallbackQty,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if (fills.Count > 0)
        {
            foreach (var fill in fills)
            {
                await executionRepository.InsertAsync(new TradeExecution
                {
                    OrderId = order.Id,
                    ExchangeOrderId = order.ExchangeOrderId,
                    ExchangeTradeId = fill.Id,
                    Symbol = symbol,
                    Side = side,
                    Price = fill.Price,
                    Quantity = fill.Qty,
                    QuoteQuantity = fill.QuoteQty > 0m ? fill.QuoteQty : fill.Price * fill.Qty,
                    Fee = fill.Commission,
                    FeeAsset = string.IsNullOrWhiteSpace(fill.CommissionAsset) ? "USDT" : fill.CommissionAsset,
                    PositionProcessedAt = now,
                    ExecutedAt = fill.TimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(fill.TimeMs).UtcDateTime : now
                }, cancellationToken);
            }

            return;
        }

        await executionRepository.InsertAsync(new TradeExecution
        {
            OrderId = order.Id,
            ExchangeOrderId = order.ExchangeOrderId,
            ExchangeTradeId = null,
            Symbol = symbol,
            Side = side,
            Price = fallbackPrice,
            Quantity = fallbackQty,
            QuoteQuantity = result.CumQuote > 0m ? result.CumQuote : fallbackPrice * fallbackQty,
            Fee = 0m,
            FeeAsset = "USDT",
            PositionProcessedAt = now,
            ExecutedAt = result.UpdateTimeMs > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(result.UpdateTimeMs).UtcDateTime : now
        }, cancellationToken);
    }

    private static decimal Vwap(IReadOnlyList<FuturesTestnetUserTrade> fills)
    {
        decimal quote = 0m, qty = 0m;
        foreach (var fill in fills)
        {
            if (fill.Price <= 0m || fill.Qty <= 0m)
                continue;
            quote += fill.Price * fill.Qty;
            qty += fill.Qty;
        }

        return qty > 0m ? quote / qty : 0m;
    }

    private static string CloseLockKey(long positionId)
        => $"SpotFuturesCrossMarket:{SpotFuturesCrossMarketSettings.ExecutionEnvironment}:CloseLock:{positionId}";

    private sealed record CloseQuantityResolution(decimal Quantity, string? PositionSide);
}

public sealed record SpotFuturesCrossMarketCloseRequest(
    SpotFuturesCrossMarketSettings Settings,
    Position Position,
    PositionExitReason ExitReason,
    CloseReason CloseReason,
    string Reason,
    string CorrelationId,
    OrderSource OrderSource,
    DateTime DecisionAcceptedAtUtc,
    decimal? RequestedQuantity = null,
    int CloseLockSeconds = 120);

public sealed class SpotFuturesCrossMarketCloseResult
{
    public bool Success { get; init; }
    public bool DuplicatePrevented { get; init; }
    public string? Error { get; init; }
    public TradingOrder? Order { get; init; }
    public Position? ClosedPosition { get; init; }
    public decimal ActualGrossPnl { get; init; }
    public decimal ActualNetPnl { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public DateTime? AcknowledgedAtUtc { get; init; }
    public DateTime? FilledAtUtc { get; init; }

    public static SpotFuturesCrossMarketCloseResult Succeeded(
        TradingOrder order,
        Position closedPosition,
        decimal actualGrossPnl,
        decimal actualNetPnl,
        DateTime submittedAtUtc,
        DateTime acknowledgedAtUtc,
        DateTime filledAtUtc)
        => new()
        {
            Success = true,
            Order = order,
            ClosedPosition = closedPosition,
            ActualGrossPnl = actualGrossPnl,
            ActualNetPnl = actualNetPnl,
            SubmittedAtUtc = submittedAtUtc,
            AcknowledgedAtUtc = acknowledgedAtUtc,
            FilledAtUtc = filledAtUtc
        };

    public static SpotFuturesCrossMarketCloseResult Duplicate(string reason)
        => new() { Success = false, DuplicatePrevented = true, Error = reason };

    public static SpotFuturesCrossMarketCloseResult Failed(string error)
        => new() { Success = false, Error = error };
}
