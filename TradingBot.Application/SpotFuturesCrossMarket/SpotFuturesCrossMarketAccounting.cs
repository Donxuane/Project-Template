using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// Long- and short-aware linear USD-M PnL accounting for the SpotFuturesCrossMarketTestnetV1
/// strategy. The shared PositionAccountingService is spot-long-only, so this service computes
/// both directions and persists into the shared positions table tagged with the strategy's
/// execution environment so live Spot workers and the ETH15 worker never touch these rows.
/// </summary>
public sealed class SpotFuturesCrossMarketAccounting(
    IPositionRepository positionRepository,
    ILogger<SpotFuturesCrossMarketAccounting> logger)
{
    public async Task<Position> OpenAsync(
        TradingSymbol symbol,
        OrderSide side,
        decimal quantity,
        decimal entryPrice,
        decimal entryFeeUsdt,
        decimal? stopLossPrice,
        decimal? takeProfitPrice,
        DateTime openedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var position = new Position
        {
            Symbol = symbol,
            Side = side,
            Quantity = quantity,
            AveragePrice = entryPrice,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            OpenedAt = openedAtUtc,
            RealizedPnl = -entryFeeUsdt,
            UnrealizedPnl = 0m,
            IsOpen = true,
            IsClosing = false,
            ExecutionEnvironment = SpotFuturesCrossMarketSettings.ExecutionEnvironment,
            CreatedAt = now,
            UpdatedAt = now
        };

        await positionRepository.UpsertAsync(position, cancellationToken);
        logger.LogInformation(
            "SpotFuturesCrossMarket position opened. PositionId={PositionId} Symbol={Symbol} Side={Side} Quantity={Quantity} EntryPrice={EntryPrice} EntryFee={EntryFee} StopLoss={StopLoss} TakeProfit={TakeProfit}",
            position.Id, symbol, side, quantity, entryPrice, entryFeeUsdt, stopLossPrice, takeProfitPrice);
        return position;
    }

    public async Task<Position> CloseAsync(
        Position position,
        decimal exitPrice,
        decimal exitFeeUsdt,
        PositionExitReason exitReason,
        DateTime closedAtUtc,
        CancellationToken cancellationToken = default)
    {
        // RealizedPnl already carries the negative entry fee; fold in gross PnL and exit fee.
        var grossPnl = GrossPnl(position.Side, position.AveragePrice, exitPrice, position.Quantity);
        position.RealizedPnl += grossPnl - exitFeeUsdt;
        position.ExitPrice = exitPrice;
        position.ExitReason = exitReason;
        position.ClosedAt = closedAtUtc;
        position.UnrealizedPnl = 0m;
        position.IsOpen = false;
        position.IsClosing = false;
        position.UpdatedAt = DateTime.UtcNow;
        position.ExecutionEnvironment = SpotFuturesCrossMarketSettings.ExecutionEnvironment;

        await positionRepository.UpsertAsync(position, cancellationToken);
        logger.LogInformation(
            "SpotFuturesCrossMarket position closed. PositionId={PositionId} Symbol={Symbol} Side={Side} EntryPrice={EntryPrice} ExitPrice={ExitPrice} Quantity={Quantity} ExitReason={ExitReason} RealizedPnl={RealizedPnl}",
            position.Id, position.Symbol, position.Side, position.AveragePrice, exitPrice, position.Quantity, exitReason, position.RealizedPnl);
        return position;
    }

    public Task UpdateUnrealizedAsync(Position position, decimal markPrice, CancellationToken cancellationToken = default)
    {
        position.UnrealizedPnl = GrossPnl(position.Side, position.AveragePrice, markPrice, position.Quantity);
        position.UpdatedAt = DateTime.UtcNow;
        return positionRepository.UpsertAsync(position, cancellationToken);
    }

    /// <summary>Linear USD-M gross PnL. Long: (exit - entry) * qty. Short: (entry - exit) * qty.</summary>
    public static decimal GrossPnl(OrderSide side, decimal entryPrice, decimal exitPrice, decimal quantity)
        => side == OrderSide.BUY
            ? (exitPrice - entryPrice) * quantity
            : (entryPrice - exitPrice) * quantity;
}
