using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Shared.Configuration;

namespace TradingBot.Application.BackgroundHostService.Services;

public class PositionExecutionGuard(
    IConfiguration configuration,
    IPositionRepository positionRepository,
    IOrderRepository orderRepository,
    ILogger<PositionExecutionGuard> logger) : IPositionExecutionGuard
{
    private readonly TradingRuntimeSettings _trading = RuntimeTradingConfigResolver.ResolveTrading(configuration);
    private readonly OpenLongCircuitBreakerSettings _openLongCircuitBreaker = new(
        configuration.GetValue<bool?>("Trading:OpenLongCircuitBreaker:Enabled") ?? false,
        Math.Max(1, configuration.GetValue<int?>("Trading:OpenLongCircuitBreaker:LookbackHours") ?? 24),
        Math.Max(1, configuration.GetValue<int?>("Trading:OpenLongCircuitBreaker:MaxConsecutiveRealizedLosingTrades") ?? 3),
        Math.Max(0m, configuration.GetValue<decimal?>("Trading:OpenLongCircuitBreaker:SessionRealizedLossLimitQuote") ?? 1.00m));

    public async Task<PositionExecutionGuardResult> EvaluateAsync(PositionExecutionGuardRequest request, CancellationToken cancellationToken = default)
    {
        var allowAddToPosition = _trading.AllowAddToPosition;
        var maxOpenPositionsPerSymbol = _trading.MaxOpenPositionsPerSymbol;

        var openPosition = await positionRepository.GetOpenPositionAsync(request.Symbol, cancellationToken);
        var openQuantity = openPosition?.IsOpen == true ? Math.Max(0m, openPosition.Quantity) : 0m;
        var hasOpenLong = openPosition is not null
                          && openPosition.IsOpen
                          && openPosition.Side == OrderSide.BUY
                          && openPosition.Quantity > 0m;

        var blocked = EvaluateBlockReason(request, allowAddToPosition, maxOpenPositionsPerSymbol, hasOpenLong, openQuantity);
        if (string.IsNullOrWhiteSpace(blocked)
            && request.TradingMode == TradingMode.Spot
            && request.ExecutionIntent == TradeExecutionIntent.CloseLong)
        {
            if (openPosition is null || openPosition.Id <= 0)
            {
                blocked = "Execution skipped - close position id is unavailable for Spot CloseLong.";
            }
            else if (await orderRepository.HasInFlightClosingOrderForPositionAsync(openPosition.Id, cancellationToken))
            {
                blocked = "Execution skipped - close order already in-flight for position.";
            }
        }
        if (string.IsNullOrWhiteSpace(blocked))
        {
            blocked = await EvaluateOpenLongCircuitBreakerBlockReasonAsync(request, cancellationToken);
        }

        var allowed = string.IsNullOrWhiteSpace(blocked);
        var reason = allowed ? "Position execution guard passed." : blocked!;

        logger.LogInformation(
            "PositionExecutionGuard evaluated: Symbol={Symbol}, TradingMode={TradingMode}, RawSignal={RawSignal}, ExecutionIntent={ExecutionIntent}, RequestedSide={RequestedSide}, RequestedQuantity={RequestedQuantity}, OpenPositionQuantity={OpenPositionQuantity}, AllowAddToPosition={AllowAddToPosition}, MaxOpenPositionsPerSymbol={MaxOpenPositionsPerSymbol}, IsProtectiveExit={IsProtectiveExit}, Allowed={Allowed}, Reason={Reason}",
            request.Symbol,
            request.TradingMode,
            request.RawSignal,
            request.ExecutionIntent,
            request.RequestedSide,
            request.RequestedQuantity,
            openQuantity,
            allowAddToPosition,
            maxOpenPositionsPerSymbol,
            request.IsProtectiveExit,
            allowed,
            reason);

        return new PositionExecutionGuardResult
        {
            IsAllowed = allowed,
            Reason = reason,
            OpenPositionQuantity = openQuantity
        };
    }

    private async Task<string?> EvaluateOpenLongCircuitBreakerBlockReasonAsync(
        PositionExecutionGuardRequest request,
        CancellationToken cancellationToken)
    {
        var isSpotOpenLong = request.TradingMode == TradingMode.Spot
                             && request.ExecutionIntent == TradeExecutionIntent.OpenLong
                             && request.RequestedSide == OrderSide.BUY;
        if (!isSpotOpenLong || !_openLongCircuitBreaker.Enabled)
            return null;

        var closedPositions = await positionRepository.GetClosedPositionsAsync(cancellationToken);
        var lookbackStartUtc = DateTime.UtcNow.AddHours(-_openLongCircuitBreaker.LookbackHours);
        var positionsInWindow = closedPositions
            .Where(p => ResolveClosedAtUtc(p) >= lookbackStartUtc)
            .OrderByDescending(ResolveClosedAtUtc)
            .ToList();

        var consecutiveLosses = 0;
        foreach (var position in positionsInWindow)
        {
            if (position.RealizedPnl < 0m)
            {
                consecutiveLosses++;
                continue;
            }

            break;
        }

        var sessionRealizedPnl = positionsInWindow.Sum(p => p.RealizedPnl);
        string? blockedReason = null;
        if (consecutiveLosses >= _openLongCircuitBreaker.MaxConsecutiveRealizedLosingTrades)
        {
            blockedReason = "OpenLong blocked by circuit breaker due to consecutive realized losses.";
        }
        else if (_openLongCircuitBreaker.SessionRealizedLossLimitQuote > 0m
                 && sessionRealizedPnl <= -_openLongCircuitBreaker.SessionRealizedLossLimitQuote)
        {
            blockedReason = "OpenLong blocked by circuit breaker due to session realized loss limit.";
        }

        if (blockedReason is null)
            return null;

        logger.LogWarning(
            "OpenLong circuit breaker blocked entry: Symbol={Symbol}, Enabled={Enabled}, LookbackHours={LookbackHours}, ConsecutiveLosses={ConsecutiveLosses}, MaxConsecutiveRealizedLosingTrades={MaxConsecutiveRealizedLosingTrades}, SessionRealizedPnl={SessionRealizedPnl}, SessionRealizedLossLimitQuote={SessionRealizedLossLimitQuote}, BlockedReason={BlockedReason}",
            request.Symbol,
            _openLongCircuitBreaker.Enabled,
            _openLongCircuitBreaker.LookbackHours,
            consecutiveLosses,
            _openLongCircuitBreaker.MaxConsecutiveRealizedLosingTrades,
            sessionRealizedPnl,
            _openLongCircuitBreaker.SessionRealizedLossLimitQuote,
            blockedReason);

        return blockedReason;
    }

    private static DateTime ResolveClosedAtUtc(Domain.Models.Trading.Position position)
    {
        if (position.ClosedAt.HasValue)
            return position.ClosedAt.Value;

        return position.UpdatedAt != default ? position.UpdatedAt : position.CreatedAt;
    }

    private static string? EvaluateBlockReason(
        PositionExecutionGuardRequest request,
        bool allowAddToPosition,
        int maxOpenPositionsPerSymbol,
        bool hasOpenLong,
        decimal openQuantity)
    {
        if (request.RequestedQuantity <= 0m)
            return "Execution skipped because requested quantity must be greater than zero.";

        if (request.TradingMode == TradingMode.Futures)
            return "Futures execution intent is not supported by the current spot execution pipeline.";

        if (request.TradingMode != TradingMode.Spot)
            return "Execution skipped because trading mode is not supported.";

        if (request.ExecutionIntent == TradeExecutionIntent.OpenLong)
        {
            if (maxOpenPositionsPerSymbol <= 0)
                return "Spot BUY skipped because max open positions per symbol is configured to zero.";

            if (hasOpenLong && !allowAddToPosition)
                return "Spot BUY skipped because an open long position already exists and add-to-position is disabled.";

            return null;
        }

        if (request.ExecutionIntent == TradeExecutionIntent.CloseLong)
        {
            if (!hasOpenLong || openQuantity <= 0m)
                return "Spot SELL skipped because no open long position exists.";

            if (request.RequestedQuantity > openQuantity)
                return $"Spot SELL skipped because requested quantity {request.RequestedQuantity} exceeds open long quantity {openQuantity}.";

            return null;
        }

        return "Execution skipped because execution intent is not supported by the current spot execution pipeline.";
    }

    private sealed record OpenLongCircuitBreakerSettings(
        bool Enabled,
        int LookbackHours,
        int MaxConsecutiveRealizedLosingTrades,
        decimal SessionRealizedLossLimitQuote);
}
