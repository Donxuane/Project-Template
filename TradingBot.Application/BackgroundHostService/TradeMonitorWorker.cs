using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;
using TradingBot.Domain.Utilities;

namespace TradingBot.Application.BackgroundHostService;

public class TradeMonitorWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TradeMonitorWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 10;
    private const int DefaultMaxTradeDurationMinutes = 60;
    private const int DefaultCloseOrderMaxRetries = 3;
    private const int DefaultCloseOrderRetryDelayMs = 1000;
    private const decimal DefaultFeeRate = 0.001m;
    private const bool DefaultEnableTimeExit = true;
    private const bool DefaultEnableStopLossExit = true;
    private const bool DefaultEnableTakeProfitExit = true;
    private const bool DefaultEnableTrailingStop = false;
    private const decimal DefaultTrailingStopPercent = 0.5m;
    private const bool DefaultEnableBreakEvenStop = false;
    private const decimal DefaultBreakEvenTriggerPercent = 0.5m;
    private const int DefaultCloseIdempotencyWindowSeconds = 300;
    private static readonly ConcurrentDictionary<long, decimal> HighestPriceSinceEntry = new();
    private static readonly ConcurrentDictionary<long, decimal> LowestPriceSinceEntry = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadSettings();

        logger.LogInformation(
            "TradeMonitorWorker started. Interval={IntervalSeconds}s, MaxDuration={MaxDurationMinutes}m, Retries={Retries}, RetryDelayMs={RetryDelayMs}, FeeRate={FeeRate}, StopLossExit={StopLossExit}, TakeProfitExit={TakeProfitExit}, TimeExit={TimeExit}, TrailingStop={TrailingStop}, BreakEven={BreakEven}",
            settings.IntervalSeconds,
            settings.MaxTradeDurationMinutes,
            settings.CloseOrderMaxRetries,
            settings.CloseOrderRetryDelayMs,
            settings.FeeRate,
            settings.EnableStopLossExit,
            settings.EnableTakeProfitExit,
            settings.EnableTimeExit,
            settings.EnableTrailingStop,
            settings.EnableBreakEvenStop);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorOpenPositionsAsync(settings, stoppingToken);

                await Task.Delay(TimeSpan.FromSeconds(settings.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "TradeMonitorWorker cycle failed at {Time}", DateTime.UtcNow);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        logger.LogInformation("TradeMonitorWorker stopped.");
    }

    private async Task MonitorOpenPositionsAsync(TradeMonitoringSettings settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var priceCacheService = scope.ServiceProvider.GetRequiredService<IPriceCacheService>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var tradeIdempotencyService = scope.ServiceProvider.GetRequiredService<ITradeIdempotencyService>();
        var positionExecutionGuard = scope.ServiceProvider.GetRequiredService<IPositionExecutionGuard>();

        var openPositions = await positionRepository.GetOpenPositionsAsync(cancellationToken);

        foreach (var position in openPositions)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // 🔒 Prevent double closing
            if (!position.IsOpen || position.IsClosing)
                continue;

            var currentPrice = await priceCacheService.GetCachedPriceAsync(position.Symbol, cancellationToken);

            if (!currentPrice.HasValue || currentPrice.Value <= 0)
            {
                logger.LogDebug("No price for {Symbol}, skipping", position.Symbol);
                continue;
            }

            UpdateTrailingAnchors(position, currentPrice.Value);

            var quantity = Math.Abs(position.Quantity);
            if (quantity <= 0m)
            {
                logger.LogWarning(
                    "TradeMonitorWorker skipped position due to invalid quantity. PositionId={PositionId}, Symbol={Symbol}, Quantity={Quantity}",
                    position.Id, position.Symbol, position.Quantity);
                continue;
            }

            var openedAt = position.OpenedAt ?? position.CreatedAt;
            var duration = DateTime.UtcNow - openedAt;
            await ApplyBreakEvenStopIfNeededAsync(position, currentPrice.Value, settings, positionRepository, cancellationToken);

            var reason = EvaluateExitReason(
                position,
                currentPrice.Value,
                duration,
                TimeSpan.FromMinutes(settings.MaxTradeDurationMinutes),
                settings);

            if (reason is null)
                continue;

            var closeSide = position.Side == OrderSide.BUY ? OrderSide.SELL : OrderSide.BUY;
            var closeIntent = position.Side == OrderSide.BUY
                ? TradeExecutionIntent.CloseLong
                : TradeExecutionIntent.CloseShort;

            logger.LogInformation(
                "TradeMonitorWorker exit trigger: PositionId={PositionId}, Symbol={Symbol}, Side={Side}, Quantity={Quantity}, EntryPrice={EntryPrice}, CurrentPrice={CurrentPrice}, StopLoss={StopLossPrice}, TakeProfit={TakeProfitPrice}, ExitReason={ExitReason}, DurationMinutes={DurationMinutes}",
                position.Id,
                position.Symbol,
                position.Side,
                quantity,
                position.AveragePrice,
                currentPrice.Value,
                position.StopLossPrice,
                position.TakeProfitPrice,
                reason,
                duration.TotalMinutes);

            var guardResult = await positionExecutionGuard.EvaluateAsync(
                new PositionExecutionGuardRequest
                {
                    Symbol = position.Symbol,
                    TradingMode = TradingMode.Spot,
                    RawSignal = TradeSignal.Hold,
                    ExecutionIntent = closeIntent,
                    RequestedSide = closeSide,
                    RequestedQuantity = quantity,
                    IsProtectiveExit = true
                },
                cancellationToken);
            if (!guardResult.IsAllowed)
            {
                logger.LogInformation(
                    "TradeMonitorWorker protective close blocked by position guard. PositionId={PositionId}, Symbol={Symbol}, TradingMode={TradingMode}, RawSignal={RawSignal}, ExecutionIntent={ExecutionIntent}, RequestedSide={RequestedSide}, RequestedQuantity={RequestedQuantity}, OpenPositionQuantity={OpenPositionQuantity}, Allowed={Allowed}, Reason={Reason}",
                    position.Id,
                    position.Symbol,
                    TradingMode.Spot,
                    TradeSignal.Hold,
                    closeIntent,
                    closeSide,
                    quantity,
                    guardResult.OpenPositionQuantity,
                    false,
                    guardResult.Reason);
                continue;
            }

            var closeKey = CreateCloseIdempotencyKey(position, closeSide, quantity, reason.Value);
            if (!await tradeIdempotencyService.TryRegisterDecisionAsync(closeKey, settings.CloseIdempotencyWindowSeconds, cancellationToken))
            {
                logger.LogInformation(
                    "TradeMonitorWorker skipped duplicate close intent. PositionId={PositionId}, Symbol={Symbol}, ExitReason={ExitReason}",
                    position.Id,
                    position.Symbol,
                    reason);
                // TODO: If idempotency service is not available in some hosts, inject ITradeIdempotencyService for close-order deduplication.
                continue;
            }

            // TODO: Replace with repository-level atomic operation to prevent cross-process close races:
            // Task<bool> TryMarkPositionClosingAsync(long positionId, CancellationToken cancellationToken)
            position.IsClosing = true;
            await positionRepository.UpsertAsync(position, cancellationToken);

            try
            {
                var closeResult = await ExecuteCloseWithRetryAsync(
                    mediator,
                    position.Symbol,
                    closeSide,
                    quantity,
                    MapCloseReason(reason.Value),
                    position.Id,
                    Guid.NewGuid().ToString("N"),
                    settings.CloseOrderMaxRetries,
                    settings.CloseOrderRetryDelayMs,
                    cancellationToken);

                if (closeResult is null || !closeResult.Success)
                {
                    position.IsClosing = false;
                    await positionRepository.UpsertAsync(position, cancellationToken);
                    continue;
                }

                var exitPrice = ResolveExitPrice(closeResult, currentPrice.Value, position);
                var executedQuantity = ResolveExecutedQuantity(closeResult, quantity, position);
                var pnl = CalculateRealizedPnlWithFees(position, exitPrice, executedQuantity, settings.FeeRate);

                position.IsOpen = false;
                position.IsClosing = false;
                position.ExitPrice = exitPrice;
                position.ExitReason = reason.Value;
                position.ClosedAt = DateTime.UtcNow;
                position.RealizedPnl += pnl;
                position.UnrealizedPnl = 0m;

                await positionRepository.UpsertAsync(position, cancellationToken);
                HighestPriceSinceEntry.TryRemove(position.Id, out _);
                LowestPriceSinceEntry.TryRemove(position.Id, out _);

                logger.LogInformation(
                    "TradeMonitorWorker close success: PositionId={PositionId}, Symbol={Symbol}, Side={Side}, ExitReason={ExitReason}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, ExitPrice={ExitPrice}, ExecutedQuantity={ExecutedQuantity}, PnL={PnL}",
                    position.Id,
                    position.Symbol,
                    closeSide,
                    reason,
                    closeResult.Order?.Id,
                    closeResult.Order?.ExchangeOrderId,
                    exitPrice,
                    executedQuantity,
                    pnl);

                // TODO: Persist additional close artifacts when Position model is extended:
                // ExitOrderId, ExchangeOrderId, ExitExecutedQuantity, ExitFee.
            }
            catch
            {
                position.IsClosing = false;
                await positionRepository.UpsertAsync(position, cancellationToken);
                throw;
            }
        }
    }

    private PositionExitReason? EvaluateExitReason(
        Position position,
        decimal currentPrice,
        TimeSpan duration,
        TimeSpan maxTradeDuration,
        TradeMonitoringSettings settings)
    {
        if (settings.EnableStopLossExit)
        {
            if (position.Side == OrderSide.BUY)
            {
                if (position.StopLossPrice.HasValue && currentPrice <= position.StopLossPrice.Value)
                    return PositionExitReason.StopLoss;
            }
            else if (position.StopLossPrice.HasValue && currentPrice >= position.StopLossPrice.Value)
            {
                return PositionExitReason.StopLoss;
            }
        }

        if (settings.EnableTakeProfitExit)
        {
            if (position.Side == OrderSide.BUY)
            {
                if (position.TakeProfitPrice.HasValue && currentPrice >= position.TakeProfitPrice.Value)
                    return PositionExitReason.TakeProfit;
            }
            else if (position.TakeProfitPrice.HasValue && currentPrice <= position.TakeProfitPrice.Value)
            {
                return PositionExitReason.TakeProfit;
            }
        }

        if (settings.EnableTrailingStop && IsTrailingStopTriggered(position, currentPrice, settings.TrailingStopPercent))
            return PositionExitReason.TrailingStop;

        if (settings.EnableTimeExit && duration > maxTradeDuration)
            return PositionExitReason.Time;

        return null;
    }

    private bool IsTrailingStopTriggered(Position position, decimal currentPrice, decimal trailingStopPercent)
    {
        if (trailingStopPercent <= 0m)
            return false;

        var ratio = trailingStopPercent / 100m;
        if (position.Side == OrderSide.BUY)
        {
            if (!HighestPriceSinceEntry.TryGetValue(position.Id, out var highest))
                return false;
            var triggerPrice = highest * (1m - ratio);
            return currentPrice <= triggerPrice;
        }

        if (!LowestPriceSinceEntry.TryGetValue(position.Id, out var lowest))
            return false;
        var shortTriggerPrice = lowest * (1m + ratio);
        return currentPrice >= shortTriggerPrice;
    }

    private async Task<PlaceSpotOrderResult?> ExecuteCloseWithRetryAsync(
        IMediator mediator,
        TradingSymbol symbol,
        OrderSide closeSide,
        decimal quantity,
        CloseReason closeReason,
        long parentPositionId,
        string correlationId,
        int maxRetries,
        int retryDelayMs,
        CancellationToken cancellationToken)
    {
        PlaceSpotOrderResult? lastResult = null;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await mediator.Send(
                new PlaceSpotOrderCommand(
                    symbol,
                    closeSide,
                    quantity,
                    Price: null,
                    IsLimitOrder: false,
                    OrderSource: OrderSource.TradeMonitorWorker,
                    CloseReason: closeReason,
                    ParentPositionId: parentPositionId,
                    CorrelationId: correlationId),
                cancellationToken);

            if (result.Success)
                return result;

            lastResult = result;
            logger.LogWarning(
                "TradeMonitorWorker close attempt failed: Symbol={Symbol}, Side={Side}, Quantity={Quantity}, Attempt={Attempt}/{MaxRetries}, Error={Error}",
                symbol,
                closeSide,
                quantity,
                attempt,
                maxRetries,
                result.Error);
            if (attempt < maxRetries)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(retryDelayMs), cancellationToken);
            }
        }

        return lastResult;
    }

    private void UpdateTrailingAnchors(Position position, decimal currentPrice)
    {
        if (position.Side == OrderSide.BUY)
        {
            HighestPriceSinceEntry.AddOrUpdate(position.Id, currentPrice, (_, old) => Math.Max(old, currentPrice));
            LowestPriceSinceEntry.TryAdd(position.Id, currentPrice);
            return;
        }

        LowestPriceSinceEntry.AddOrUpdate(position.Id, currentPrice, (_, old) => Math.Min(old, currentPrice));
        HighestPriceSinceEntry.TryAdd(position.Id, currentPrice);
        // TODO: Persist highest/lowest anchors on Position model (HighestPriceSinceEntry/LowestPriceSinceEntry) to survive restarts.
    }

    private async Task ApplyBreakEvenStopIfNeededAsync(
        Position position,
        decimal currentPrice,
        TradeMonitoringSettings settings,
        IPositionRepository positionRepository,
        CancellationToken cancellationToken)
    {
        if (!settings.EnableBreakEvenStop || position.AveragePrice <= 0m || settings.BreakEvenTriggerPercent <= 0m)
            return;

        var triggerRatio = settings.BreakEvenTriggerPercent / 100m;
        var entryPrice = position.AveragePrice;
        var shouldUpdate = false;

        if (position.Side == OrderSide.BUY)
        {
            var triggerPrice = entryPrice * (1m + triggerRatio);
            var currentStop = position.StopLossPrice;
            if (currentPrice >= triggerPrice && (!currentStop.HasValue || currentStop.Value < entryPrice))
            {
                position.StopLossPrice = entryPrice;
                shouldUpdate = true;
            }
        }
        else
        {
            var triggerPrice = entryPrice * (1m - triggerRatio);
            var currentStop = position.StopLossPrice;
            if (currentPrice <= triggerPrice && (!currentStop.HasValue || currentStop.Value > entryPrice))
            {
                position.StopLossPrice = entryPrice;
                shouldUpdate = true;
            }
        }

        if (!shouldUpdate)
            return;

        await positionRepository.UpsertAsync(position, cancellationToken);
        logger.LogInformation(
            "TradeMonitorWorker break-even stop updated. PositionId={PositionId}, Symbol={Symbol}, NewStopLoss={StopLoss}",
            position.Id,
            position.Symbol,
            position.StopLossPrice);
    }

    private decimal ResolveExitPrice(PlaceSpotOrderResult closeResult, decimal fallbackPrice, Position position)
    {
        var orderPrice = closeResult.Order?.Price ?? 0m;
        if (orderPrice > 0m)
            return orderPrice;

        logger.LogWarning(
            "TradeMonitorWorker used cached price as exit fill fallback. PositionId={PositionId}, Symbol={Symbol}, CachedPrice={CachedPrice}",
            position.Id,
            position.Symbol,
            fallbackPrice);
        return fallbackPrice;
    }

    private decimal ResolveExecutedQuantity(PlaceSpotOrderResult closeResult, decimal fallbackQuantity, Position position)
    {
        var executedQuantity = closeResult.Order?.Quantity ?? 0m;
        if (executedQuantity > 0m)
            return executedQuantity;

        logger.LogWarning(
            "TradeMonitorWorker used requested quantity as executed fallback. PositionId={PositionId}, Symbol={Symbol}, Quantity={Quantity}",
            position.Id,
            position.Symbol,
            fallbackQuantity);
        return fallbackQuantity;
    }

    private static string CreateCloseIdempotencyKey(Position position, OrderSide closeSide, decimal quantity, PositionExitReason reason)
    {
        var raw = $"{position.Id}|{position.Symbol}|{closeSide}|{BinanceDecimalFormatter.FormatQuantity(quantity)}|{reason}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes)[..32];
    }

    private static CloseReason MapCloseReason(PositionExitReason reason)
    {
        return reason switch
        {
            PositionExitReason.StopLoss => CloseReason.StopLoss,
            PositionExitReason.TakeProfit => CloseReason.TakeProfit,
            PositionExitReason.Time => CloseReason.MaxDuration,
            PositionExitReason.TrailingStop => CloseReason.RiskExit,
            _ => CloseReason.Unknown
        };
    }

    private static decimal CalculateRealizedPnlWithFees(Position position, decimal exitPrice, decimal quantity, decimal feeRate)
    {
        var rawPnl = position.Side == OrderSide.BUY
            ? (exitPrice - position.AveragePrice) * quantity
            : (position.AveragePrice - exitPrice) * quantity;

        var entryNotional = position.AveragePrice * quantity;
        var exitNotional = exitPrice * quantity;
        var fee = (entryNotional + exitNotional) * feeRate;

        return rawPnl - fee;
    }

    private TradeMonitoringSettings ReadSettings()
    {
        var intervalSeconds = Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:IntervalSeconds") ?? DefaultIntervalSeconds);
        var maxTradeDurationMinutes = Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:MaxTradeDurationMinutes") ?? DefaultMaxTradeDurationMinutes);
        var closeOrderMaxRetries = Math.Max(1, configuration.GetValue<int?>("TradeMonitoring:CloseOrderMaxRetries") ?? DefaultCloseOrderMaxRetries);
        var closeOrderRetryDelayMs = Math.Max(100, configuration.GetValue<int?>("TradeMonitoring:CloseOrderRetryDelayMs") ?? DefaultCloseOrderRetryDelayMs);
        var feeRateRaw = configuration.GetValue<decimal?>("TradeMonitoring:FeeRate") ?? DefaultFeeRate;
        var feeRate = Math.Clamp(feeRateRaw, 0m, 0.05m);
        var enableTimeExit = configuration.GetValue<bool?>("TradeMonitoring:EnableTimeExit") ?? DefaultEnableTimeExit;
        var enableStopLossExit = configuration.GetValue<bool?>("TradeMonitoring:EnableStopLossExit") ?? DefaultEnableStopLossExit;
        var enableTakeProfitExit = configuration.GetValue<bool?>("TradeMonitoring:EnableTakeProfitExit") ?? DefaultEnableTakeProfitExit;
        var enableTrailingStop = configuration.GetValue<bool?>("TradeMonitoring:EnableTrailingStop") ?? DefaultEnableTrailingStop;
        var trailingStopPercentRaw = configuration.GetValue<decimal?>("TradeMonitoring:TrailingStopPercent") ?? DefaultTrailingStopPercent;
        var trailingStopPercent = Math.Clamp(trailingStopPercentRaw, 0m, 50m);
        var enableBreakEvenStop = configuration.GetValue<bool?>("TradeMonitoring:EnableBreakEvenStop") ?? DefaultEnableBreakEvenStop;
        var breakEvenTriggerPercentRaw = configuration.GetValue<decimal?>("TradeMonitoring:BreakEvenTriggerPercent") ?? DefaultBreakEvenTriggerPercent;
        var breakEvenTriggerPercent = Math.Clamp(breakEvenTriggerPercentRaw, 0m, 50m);
        var closeIdempotencyWindowSeconds = Math.Max(10, configuration.GetValue<int?>("TradeMonitoring:CloseIdempotencyWindowSeconds") ?? DefaultCloseIdempotencyWindowSeconds);

        return new TradeMonitoringSettings
        {
            IntervalSeconds = intervalSeconds,
            MaxTradeDurationMinutes = maxTradeDurationMinutes,
            CloseOrderMaxRetries = closeOrderMaxRetries,
            CloseOrderRetryDelayMs = closeOrderRetryDelayMs,
            FeeRate = feeRate,
            EnableTimeExit = enableTimeExit,
            EnableStopLossExit = enableStopLossExit,
            EnableTakeProfitExit = enableTakeProfitExit,
            EnableTrailingStop = enableTrailingStop,
            TrailingStopPercent = trailingStopPercent,
            EnableBreakEvenStop = enableBreakEvenStop,
            BreakEvenTriggerPercent = breakEvenTriggerPercent,
            CloseIdempotencyWindowSeconds = closeIdempotencyWindowSeconds
        };
    }

    private sealed class TradeMonitoringSettings
    {
        public int IntervalSeconds { get; init; }
        public int MaxTradeDurationMinutes { get; init; }
        public int CloseOrderMaxRetries { get; init; }
        public int CloseOrderRetryDelayMs { get; init; }
        public int CloseIdempotencyWindowSeconds { get; init; }
        public decimal FeeRate { get; init; }
        public bool EnableTimeExit { get; init; }
        public bool EnableStopLossExit { get; init; }
        public bool EnableTakeProfitExit { get; init; }
        public bool EnableTrailingStop { get; init; }
        public decimal TrailingStopPercent { get; init; }
        public bool EnableBreakEvenStop { get; init; }
        public decimal BreakEvenTriggerPercent { get; init; }
    }
}