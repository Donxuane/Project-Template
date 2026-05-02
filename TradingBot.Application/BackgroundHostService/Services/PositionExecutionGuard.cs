using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService.Services;

public class PositionExecutionGuard(
    IConfiguration configuration,
    IPositionRepository positionRepository,
    ILogger<PositionExecutionGuard> logger) : IPositionExecutionGuard
{
    public async Task<PositionExecutionGuardResult> EvaluateAsync(PositionExecutionGuardRequest request, CancellationToken cancellationToken = default)
    {
        var allowAddToPosition = configuration.GetValue<bool?>("Trading:AllowAddToPosition") ?? false;
        var maxOpenPositionsPerSymbol = Math.Max(1, configuration.GetValue<int?>("Trading:MaxOpenPositionsPerSymbol") ?? 1);

        var openPosition = await positionRepository.GetOpenPositionAsync(request.Symbol, cancellationToken);
        var openQuantity = openPosition?.IsOpen == true ? Math.Max(0m, openPosition.Quantity) : 0m;
        var hasOpenLong = openPosition is not null
                          && openPosition.IsOpen
                          && openPosition.Side == OrderSide.BUY
                          && openPosition.Quantity > 0m;

        var blocked = EvaluateBlockReason(request, allowAddToPosition, maxOpenPositionsPerSymbol, hasOpenLong, openQuantity);
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
}
