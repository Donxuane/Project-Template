using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Commands;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.BackgroundHostService;

public class TradeMonitorWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TradeMonitorWorker> logger) : BackgroundService
{
    private const int DefaultIntervalSeconds = 10;
    private const int DefaultMaxTradeDurationMinutes = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = configuration.GetValue<int?>("TradeMonitoring:IntervalSeconds") ?? DefaultIntervalSeconds;
        var maxTradeDurationMinutes = configuration.GetValue<int?>("TradeMonitoring:MaxTradeDurationMinutes") ?? DefaultMaxTradeDurationMinutes;

        logger.LogInformation(
            "TradeMonitorWorker started. Interval={IntervalSeconds}s, MaxDuration={MaxDurationMinutes}m",
            intervalSeconds, maxTradeDurationMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MonitorOpenPositionsAsync(
                    TimeSpan.FromMinutes(maxTradeDurationMinutes),
                    stoppingToken);

                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
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

    private async Task MonitorOpenPositionsAsync(
        TimeSpan maxTradeDuration,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var positionRepository = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
        var priceCacheService = scope.ServiceProvider.GetRequiredService<IPriceCacheService>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

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

            var openedAt = position.OpenedAt ?? position.CreatedAt;
            var duration = DateTime.UtcNow - openedAt;

            var reason = EvaluateExitReason(position, currentPrice.Value, duration, maxTradeDuration);

            if (reason is null)
                continue;

            // 🔒 Mark as closing BEFORE execution
            position.IsClosing = true;
            await positionRepository.UpsertAsync(position, cancellationToken);

            var closeSide = position.Side == OrderSide.BUY ? OrderSide.SELL : OrderSide.BUY;
            var quantity = Math.Abs(position.Quantity);

            if (quantity <= 0)
                continue;

            logger.LogInformation(
                "Exit triggered: {Reason} | {Symbol} | Entry={Entry} | Current={Current}",
                reason, position.Symbol, position.AveragePrice, currentPrice.Value);

            // 🔁 Retry logic
            var maxRetries = 3;
            var attempt = 0;
            bool success = false;
            PlaceSpotOrderResult? finalResult = null;

            while (attempt < maxRetries && !success)
            {
                attempt++;

                var result = await mediator.Send(
                    new PlaceSpotOrderCommand(
                        position.Symbol,
                        closeSide,
                        quantity,
                        Price: null,
                        IsLimitOrder: false),
                    cancellationToken);

                if (result.Success)
                {
                    success = true;
                    finalResult = result;
                    break;
                }

                logger.LogWarning("Close attempt {Attempt} failed for {Symbol}: {Error}",
                    attempt, position.Symbol, result.Error);

                await Task.Delay(1000, cancellationToken);
            }

            if (!success)
            {
                position.IsClosing = false;
                await positionRepository.UpsertAsync(position, cancellationToken);
                continue;
            }

            // 💰 Use currentPrice (or replace later with filled price)
            var exitPrice = currentPrice.Value;

            var pnl = CalculateRealizedPnlWithFees(position, exitPrice, quantity);

            position.IsOpen = false;
            position.IsClosing = false;
            position.ExitPrice = exitPrice;
            position.ExitReason = reason.Value;
            position.ClosedAt = DateTime.UtcNow;
            position.RealizedPnl += pnl;
            position.UnrealizedPnl = 0m;

            await positionRepository.UpsertAsync(position, cancellationToken);

            logger.LogInformation(
                "Position closed: {Symbol} | Reason={Reason} | Entry={Entry} | Exit={Exit} | PnL={PnL}",
                position.Symbol, reason, position.AveragePrice, exitPrice, pnl);
        }
    }

    private static PositionExitReason? EvaluateExitReason(
        Position position,
        decimal currentPrice,
        TimeSpan duration,
        TimeSpan maxTradeDuration)
    {
        if (position.Side == OrderSide.BUY)
        {
            if (position.StopLossPrice.HasValue && currentPrice <= position.StopLossPrice)
                return PositionExitReason.StopLoss;

            if (position.TakeProfitPrice.HasValue && currentPrice >= position.TakeProfitPrice)
                return PositionExitReason.TakeProfit;
        }
        else
        {
            if (position.StopLossPrice.HasValue && currentPrice >= position.StopLossPrice)
                return PositionExitReason.StopLoss;

            if (position.TakeProfitPrice.HasValue && currentPrice <= position.TakeProfitPrice)
                return PositionExitReason.TakeProfit;
        }

        if (duration > maxTradeDuration)
            return PositionExitReason.Time;

        return null;
    }

    private static decimal CalculateRealizedPnlWithFees(Position position, decimal exitPrice, decimal quantity)
    {
        var rawPnl = position.Side == OrderSide.BUY
            ? (exitPrice - position.AveragePrice) * quantity
            : (position.AveragePrice - exitPrice) * quantity;

        var feeRate = 0.001m; // 0.1%
        var fee = (position.AveragePrice + exitPrice) * quantity * feeRate;

        return rawPnl - fee;
    }
}