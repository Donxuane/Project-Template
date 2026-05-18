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
using TradingBot.Shared.Configuration;

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
    private const bool DefaultEnableTimeExit = true;
    private const bool DefaultEnableStopLossExit = true;
    private const bool DefaultEnableTakeProfitExit = true;
    private const bool DefaultEnableTrailingStop = false;
    private const decimal DefaultTrailingStopPercent = 0.5m;
    private const bool DefaultEnableBreakEvenStop = false;
    private const decimal DefaultBreakEvenTriggerPercent = 0.5m;
    private const int DefaultCloseIdempotencyWindowSeconds = 300;
    private const bool DefaultEnableDynamicTimeExit = false;
    private const int DefaultFirstReviewMinutes = 10;
    private const int DefaultCloseEarlyIfLossAfterMinutes = 12;
    private const decimal DefaultEarlyExitLossPercent = 0.10m;
    private const decimal DefaultNearFlatProfitPercent = 0.05m;
    private const int DefaultExtensionMinutes = 10;
    private const int DefaultMaxExtendedTradeDurationMinutes = 60;
    private const decimal DefaultMinUnrealizedProfitPercentToExtend = 0.15m;
    private const bool DefaultRequireBullishTrendToExtend = false;
    private static readonly ConcurrentDictionary<long, decimal> HighestPriceSinceEntry = new();
    private static readonly ConcurrentDictionary<long, decimal> LowestPriceSinceEntry = new();
    private static readonly ConcurrentDictionary<long, int> ExtensionLogBuckets = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = ReadSettings();

        logger.LogInformation(
            "TradeMonitorWorker started. Interval={IntervalSeconds}s, MaxDuration={MaxDurationMinutes}m, Retries={Retries}, RetryDelayMs={RetryDelayMs}, StopLossExit={StopLossExit}, TakeProfitExit={TakeProfitExit}, TimeExit={TimeExit}, TrailingStop={TrailingStop}, BreakEven={BreakEven}, DynamicTimeExit={DynamicTimeExit}, FirstReviewMinutes={FirstReviewMinutes}, CloseEarlyLossMinutes={CloseEarlyLossMinutes}, EarlyExitLossPercent={EarlyExitLossPercent}, ExtensionMinutes={ExtensionMinutes}, MaxExtendedDurationMinutes={MaxExtendedDurationMinutes}, MinUnrealizedProfitPercentToExtend={MinUnrealizedProfitPercentToExtend}, RequireBullishTrendToExtend={RequireBullishTrendToExtend}",
            settings.IntervalSeconds,
            settings.MaxTradeDurationMinutes,
            settings.CloseOrderMaxRetries,
            settings.CloseOrderRetryDelayMs,
            settings.EnableStopLossExit,
            settings.EnableTakeProfitExit,
            settings.EnableTimeExit,
            settings.EnableTrailingStop,
            settings.EnableBreakEvenStop,
            settings.EnableDynamicTimeExit,
            settings.FirstReviewMinutes,
            settings.CloseEarlyIfLossAfterMinutes,
            settings.EarlyExitLossPercent,
            settings.ExtensionMinutes,
            settings.MaxExtendedTradeDurationMinutes,
            settings.MinUnrealizedProfitPercentToExtend,
            settings.RequireBullishTrendToExtend);

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
        var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
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
            if ((settings.EnableStopLossExit && !position.StopLossPrice.HasValue) ||
                (settings.EnableTakeProfitExit && !position.TakeProfitPrice.HasValue))
            {
                logger.LogWarning(
                    "TradeMonitorWorker open position is missing protection targets while protection monitoring is enabled. PositionId={PositionId}, Symbol={Symbol}, StopLossExists={StopLossExists}, TakeProfitExists={TakeProfitExists}, StopLossExitEnabled={StopLossExitEnabled}, TakeProfitExitEnabled={TakeProfitExitEnabled}",
                    position.Id,
                    position.Symbol,
                    position.StopLossPrice.HasValue,
                    position.TakeProfitPrice.HasValue,
                    settings.EnableStopLossExit,
                    settings.EnableTakeProfitExit);
            }

            var evaluation = EvaluateExitReason(
                position,
                currentPrice.Value,
                duration,
                TimeSpan.FromMinutes(settings.MaxTradeDurationMinutes),
                settings);

            if (evaluation.IsExtended)
            {
                var extensionBucket = Math.Max(
                    1,
                    (int)Math.Ceiling(
                        Math.Max(0d, duration.TotalMinutes - settings.MaxTradeDurationMinutes)
                        / Math.Max(1, settings.ExtensionMinutes)));
                var previousBucket = ExtensionLogBuckets.GetOrAdd(position.Id, 0);
                if (extensionBucket > previousBucket && ExtensionLogBuckets.TryUpdate(position.Id, extensionBucket, previousBucket))
                {
                    logger.LogInformation(
                        "TradeMonitorWorker dynamic time extension applied. PositionId={PositionId}, Symbol={Symbol}, DurationMinutes={DurationMinutes}, MaxTradeDurationMinutes={MaxTradeDurationMinutes}, MaxExtendedTradeDurationMinutes={MaxExtendedTradeDurationMinutes}, ExtensionMinutes={ExtensionMinutes}, UnrealizedPnlPercent={UnrealizedPnlPercent}, MinUnrealizedProfitPercentToExtend={MinUnrealizedProfitPercentToExtend}",
                        position.Id,
                        position.Symbol,
                        duration.TotalMinutes,
                        settings.MaxTradeDurationMinutes,
                        settings.MaxExtendedTradeDurationMinutes,
                        settings.ExtensionMinutes,
                        evaluation.UnrealizedPnlPercent,
                        settings.MinUnrealizedProfitPercentToExtend);
                }
            }

            var reason = evaluation.Reason;
            if (reason is null)
                continue;

            if (evaluation.IsEarlyLossExit)
            {
                logger.LogInformation(
                    "TradeMonitorWorker dynamic early stale/loss exit triggered. PositionId={PositionId}, Symbol={Symbol}, DurationMinutes={DurationMinutes}, CloseEarlyIfLossAfterMinutes={CloseEarlyIfLossAfterMinutes}, UnrealizedPnlPercent={UnrealizedPnlPercent}, EarlyExitLossPercent={EarlyExitLossPercent}",
                    position.Id,
                    position.Symbol,
                    duration.TotalMinutes,
                    settings.CloseEarlyIfLossAfterMinutes,
                    evaluation.UnrealizedPnlPercent,
                    settings.EarlyExitLossPercent);
            }
            else if (evaluation.IsHardCapExit)
            {
                logger.LogInformation(
                    "TradeMonitorWorker hard max duration reached. PositionId={PositionId}, Symbol={Symbol}, DurationMinutes={DurationMinutes}, MaxExtendedTradeDurationMinutes={MaxExtendedTradeDurationMinutes}, UnrealizedPnlPercent={UnrealizedPnlPercent}",
                    position.Id,
                    position.Symbol,
                    duration.TotalMinutes,
                    settings.MaxExtendedTradeDurationMinutes,
                    evaluation.UnrealizedPnlPercent);
            }
            else if (reason == PositionExitReason.Time)
            {
                logger.LogInformation(
                    "TradeMonitorWorker time exit triggered. PositionId={PositionId}, Symbol={Symbol}, DurationMinutes={DurationMinutes}, MaxTradeDurationMinutes={MaxTradeDurationMinutes}, UnrealizedPnlPercent={UnrealizedPnlPercent}, MinUnrealizedProfitPercentToExtend={MinUnrealizedProfitPercentToExtend}, NearFlatProfitPercent={NearFlatProfitPercent}",
                    position.Id,
                    position.Symbol,
                    duration.TotalMinutes,
                    settings.MaxTradeDurationMinutes,
                    evaluation.UnrealizedPnlPercent,
                    settings.MinUnrealizedProfitPercentToExtend,
                    settings.NearFlatProfitPercent);
            }

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

            if (await orderRepository.HasActiveCloseOrderForPositionAsync(position.Id, cancellationToken))
            {
                logger.LogInformation(
                    "TradeMonitorWorker skipped close request because an active close order already exists. PositionId={PositionId}, Symbol={Symbol}, ExitReason={ExitReason}",
                    position.Id,
                    position.Symbol,
                    reason);
                continue;
            }

            var closeLockAcquired = await positionRepository.TryMarkPositionClosingAsync(position.Id, cancellationToken);
            if (!closeLockAcquired)
            {
                logger.LogInformation(
                    "TradeMonitorWorker skipped close request because position is already closing or unavailable. PositionId={PositionId}, Symbol={Symbol}, ExitReason={ExitReason}",
                    position.Id,
                    position.Symbol,
                    reason);
                continue;
            }

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
                    await positionRepository.ClearPositionClosingAsync(position.Id, cancellationToken);
                    continue;
                }

                logger.LogInformation(
                    "TradeMonitorWorker close order placed. Final position accounting is deferred to PositionWorker. PositionId={PositionId}, Symbol={Symbol}, Side={Side}, ExitReason={ExitReason}, LocalOrderId={LocalOrderId}, ExchangeOrderId={ExchangeOrderId}, RequestedQuantity={RequestedQuantity}",
                    position.Id,
                    position.Symbol,
                    closeSide,
                    reason,
                    closeResult.Order?.Id,
                    closeResult.Order?.ExchangeOrderId,
                    quantity);
                HighestPriceSinceEntry.TryRemove(position.Id, out _);
                LowestPriceSinceEntry.TryRemove(position.Id, out _);
                ExtensionLogBuckets.TryRemove(position.Id, out _);
            }
            catch
            {
                await positionRepository.ClearPositionClosingAsync(position.Id, cancellationToken);
                throw;
            }
        }
    }

    private ExitEvaluationResult EvaluateExitReason(
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
                    return ExitEvaluationResult.WithReason(PositionExitReason.StopLoss);
            }
            else if (position.StopLossPrice.HasValue && currentPrice >= position.StopLossPrice.Value)
            {
                return ExitEvaluationResult.WithReason(PositionExitReason.StopLoss);
            }
        }

        if (settings.EnableTakeProfitExit)
        {
            if (position.Side == OrderSide.BUY)
            {
                if (position.TakeProfitPrice.HasValue && currentPrice >= position.TakeProfitPrice.Value)
                    return ExitEvaluationResult.WithReason(PositionExitReason.TakeProfit);
            }
            else if (position.TakeProfitPrice.HasValue && currentPrice <= position.TakeProfitPrice.Value)
            {
                return ExitEvaluationResult.WithReason(PositionExitReason.TakeProfit);
            }
        }

        if (settings.EnableTrailingStop && IsTrailingStopTriggered(position, currentPrice, settings.TrailingStopPercent))
            return ExitEvaluationResult.WithReason(PositionExitReason.TrailingStop);

        if (!settings.EnableTimeExit)
            return ExitEvaluationResult.None;

        if (settings.EnableDynamicTimeExit &&
            position.Side == OrderSide.BUY &&
            position.AveragePrice > 0m &&
            duration >= TimeSpan.FromMinutes(settings.FirstReviewMinutes))
        {
            var unrealizedPnlPercent = CalculateUnrealizedPnlPercent(position.AveragePrice, currentPrice);
            var closeEarlyLossDuration = TimeSpan.FromMinutes(settings.CloseEarlyIfLossAfterMinutes);
            var maxExtendedDuration = TimeSpan.FromMinutes(settings.MaxExtendedTradeDurationMinutes);
            var canApplyEarlyLossRule = duration >= closeEarlyLossDuration && settings.EarlyExitLossPercent > 0m;
            if (canApplyEarlyLossRule && unrealizedPnlPercent <= -settings.EarlyExitLossPercent)
            {
                return ExitEvaluationResult.WithReason(
                    PositionExitReason.RiskExit,
                    unrealizedPnlPercent,
                    isEarlyLossExit: true);
            }

            if (duration >= maxExtendedDuration)
            {
                return ExitEvaluationResult.WithReason(
                    PositionExitReason.Time,
                    unrealizedPnlPercent,
                    isHardCapExit: true);
            }

            if (duration >= maxTradeDuration)
            {
                if (unrealizedPnlPercent < settings.MinUnrealizedProfitPercentToExtend)
                {
                    return ExitEvaluationResult.WithReason(
                        PositionExitReason.Time,
                        unrealizedPnlPercent);
                }

                if (settings.RequireBullishTrendToExtend)
                {
                    // TODO: Confirm bullish trend/momentum before extending dynamic time exit.
                    return ExitEvaluationResult.WithReason(
                        PositionExitReason.Time,
                        unrealizedPnlPercent);
                }

                return ExitEvaluationResult.Extended(unrealizedPnlPercent);
            }

            return ExitEvaluationResult.NoneWithUnrealized(unrealizedPnlPercent);
        }

        if (duration > maxTradeDuration)
            return ExitEvaluationResult.WithReason(PositionExitReason.Time);

        return ExitEvaluationResult.None;
    }

    private static decimal CalculateUnrealizedPnlPercent(decimal averagePrice, decimal currentPrice)
    {
        if (averagePrice <= 0m)
            return 0m;

        return ((currentPrice - averagePrice) / averagePrice) * 100m;
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
            PositionExitReason.RiskExit => CloseReason.RiskExit,
            _ => CloseReason.Unknown
        };
    }

    private TradeMonitoringSettings ReadSettings()
    {
        var resolved = RuntimeTradingConfigResolver.ResolveTradeMonitoring(configuration);
        var intervalSeconds = resolved.IntervalSeconds;
        var maxTradeDurationMinutes = resolved.MaxTradeDurationMinutes;
        var closeOrderMaxRetries = resolved.CloseOrderMaxRetries;
        var closeOrderRetryDelayMs = resolved.CloseOrderRetryDelayMs;
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
        var enableDynamicTimeExit = resolved.EnableDynamicTimeExit;
        var firstReviewMinutes = resolved.FirstReviewMinutes;
        var closeEarlyIfLossAfterMinutes = resolved.CloseEarlyIfLossAfterMinutes;
        var earlyExitLossPercent = resolved.EarlyExitLossPercent;
        var nearFlatProfitPercentRaw = configuration.GetValue<decimal?>("TradeMonitoring:NearFlatProfitPercent") ?? DefaultNearFlatProfitPercent;
        var nearFlatProfitPercent = Math.Clamp(nearFlatProfitPercentRaw, 0m, 50m);
        var extensionMinutes = resolved.ExtensionMinutes;
        var maxExtendedTradeDurationMinutes = Math.Max(
            maxTradeDurationMinutes,
            resolved.MaxExtendedTradeDurationMinutes);
        var minUnrealizedProfitPercentToExtend = resolved.MinUnrealizedProfitPercentToExtend;
        var requireBullishTrendToExtend = configuration.GetValue<bool?>("TradeMonitoring:RequireBullishTrendToExtend") ?? DefaultRequireBullishTrendToExtend;

        return new TradeMonitoringSettings
        {
            IntervalSeconds = intervalSeconds,
            MaxTradeDurationMinutes = maxTradeDurationMinutes,
            CloseOrderMaxRetries = closeOrderMaxRetries,
            CloseOrderRetryDelayMs = closeOrderRetryDelayMs,
            EnableTimeExit = enableTimeExit,
            EnableStopLossExit = enableStopLossExit,
            EnableTakeProfitExit = enableTakeProfitExit,
            EnableTrailingStop = enableTrailingStop,
            TrailingStopPercent = trailingStopPercent,
            EnableBreakEvenStop = enableBreakEvenStop,
            BreakEvenTriggerPercent = breakEvenTriggerPercent,
            CloseIdempotencyWindowSeconds = closeIdempotencyWindowSeconds,
            EnableDynamicTimeExit = enableDynamicTimeExit,
            FirstReviewMinutes = firstReviewMinutes,
            CloseEarlyIfLossAfterMinutes = closeEarlyIfLossAfterMinutes,
            EarlyExitLossPercent = earlyExitLossPercent,
            NearFlatProfitPercent = nearFlatProfitPercent,
            ExtensionMinutes = extensionMinutes,
            MaxExtendedTradeDurationMinutes = maxExtendedTradeDurationMinutes,
            MinUnrealizedProfitPercentToExtend = minUnrealizedProfitPercentToExtend,
            RequireBullishTrendToExtend = requireBullishTrendToExtend
        };
    }

    private sealed class TradeMonitoringSettings
    {
        public int IntervalSeconds { get; init; }
        public int MaxTradeDurationMinutes { get; init; }
        public int CloseOrderMaxRetries { get; init; }
        public int CloseOrderRetryDelayMs { get; init; }
        public int CloseIdempotencyWindowSeconds { get; init; }
        public bool EnableTimeExit { get; init; }
        public bool EnableStopLossExit { get; init; }
        public bool EnableTakeProfitExit { get; init; }
        public bool EnableTrailingStop { get; init; }
        public decimal TrailingStopPercent { get; init; }
        public bool EnableBreakEvenStop { get; init; }
        public decimal BreakEvenTriggerPercent { get; init; }
        public bool EnableDynamicTimeExit { get; init; }
        public int FirstReviewMinutes { get; init; }
        public int CloseEarlyIfLossAfterMinutes { get; init; }
        public decimal EarlyExitLossPercent { get; init; }
        public decimal NearFlatProfitPercent { get; init; }
        public int ExtensionMinutes { get; init; }
        public int MaxExtendedTradeDurationMinutes { get; init; }
        public decimal MinUnrealizedProfitPercentToExtend { get; init; }
        public bool RequireBullishTrendToExtend { get; init; }
    }

    private sealed class ExitEvaluationResult
    {
        public PositionExitReason? Reason { get; init; }
        public decimal? UnrealizedPnlPercent { get; init; }
        public bool IsExtended { get; init; }
        public bool IsEarlyLossExit { get; init; }
        public bool IsHardCapExit { get; init; }

        public static ExitEvaluationResult None { get; } = new();

        public static ExitEvaluationResult WithReason(
            PositionExitReason reason,
            decimal? unrealizedPnlPercent = null,
            bool isEarlyLossExit = false,
            bool isHardCapExit = false)
            => new()
            {
                Reason = reason,
                UnrealizedPnlPercent = unrealizedPnlPercent,
                IsEarlyLossExit = isEarlyLossExit,
                IsHardCapExit = isHardCapExit
            };

        public static ExitEvaluationResult Extended(decimal? unrealizedPnlPercent)
            => new()
            {
                IsExtended = true,
                UnrealizedPnlPercent = unrealizedPnlPercent
            };

        public static ExitEvaluationResult NoneWithUnrealized(decimal? unrealizedPnlPercent)
            => new()
            {
                UnrealizedPnlPercent = unrealizedPnlPercent
            };
    }
}