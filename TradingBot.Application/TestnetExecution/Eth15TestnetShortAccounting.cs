using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Models.Trading;

namespace TradingBot.Application.TestnetExecution;

/// <summary>
/// Short-aware realized PnL accounting for the ETH15 testnet path. The shared
/// PositionAccountingService is spot-long-only (it ignores shorts), so this service computes
/// linear USD-M short PnL and persists it into the same positions table, tagged with the
/// testnet execution environment so live workers never touch it.
/// </summary>
public sealed class Eth15TestnetShortAccounting(
    IPositionRepository positionRepository,
    ILogger<Eth15TestnetShortAccounting> logger)
{
    /// <summary>Linear short realized PnL: (entry - exit) * qty - fees. Quote currency is USDT.</summary>
    public static decimal ComputeShortRealizedPnl(decimal entryPrice, decimal exitPrice, decimal quantity, decimal totalFeesUsdt)
        => ((entryPrice - exitPrice) * quantity) - totalFeesUsdt;

    public async Task<Position> OpenShortAsync(
        TradingSymbol symbol,
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
            Side = OrderSide.SELL,
            Quantity = quantity,
            AveragePrice = entryPrice,
            StopLossPrice = stopLossPrice,
            TakeProfitPrice = takeProfitPrice,
            OpenedAt = openedAtUtc,
            RealizedPnl = -entryFeeUsdt,
            UnrealizedPnl = 0m,
            IsOpen = true,
            IsClosing = false,
            ExecutionEnvironment = ExecutionEnvironments.BinanceFuturesTestnet,
            CreatedAt = now,
            UpdatedAt = now
        };

        await positionRepository.UpsertAsync(position, cancellationToken);
        logger.LogInformation(
            "ETH15 testnet short opened. PositionId={PositionId} Symbol={Symbol} Quantity={Quantity} EntryPrice={EntryPrice} EntryFee={EntryFee} StopLoss={StopLoss} TakeProfit={TakeProfit}",
            position.Id, symbol, quantity, entryPrice, entryFeeUsdt, stopLossPrice, takeProfitPrice);
        return position;
    }

    public async Task<Position> CloseShortAsync(
        Position position,
        decimal exitPrice,
        decimal exitFeeUsdt,
        PositionExitReason exitReason,
        DateTime closedAtUtc,
        CancellationToken cancellationToken = default)
    {
        // RealizedPnl already holds the negative entry fee; fold in gross PnL and exit fee.
        var grossPnl = (position.AveragePrice - exitPrice) * position.Quantity;
        position.RealizedPnl += grossPnl - exitFeeUsdt;
        position.ExitPrice = exitPrice;
        position.ExitReason = exitReason;
        position.ClosedAt = closedAtUtc;
        position.UnrealizedPnl = 0m;
        position.IsOpen = false;
        position.IsClosing = false;
        position.UpdatedAt = DateTime.UtcNow;
        position.ExecutionEnvironment = ExecutionEnvironments.BinanceFuturesTestnet;

        await positionRepository.UpsertAsync(position, cancellationToken);
        logger.LogInformation(
            "ETH15 testnet short closed. PositionId={PositionId} Symbol={Symbol} EntryPrice={EntryPrice} ExitPrice={ExitPrice} Quantity={Quantity} ExitReason={ExitReason} RealizedPnl={RealizedPnl}",
            position.Id, position.Symbol, position.AveragePrice, exitPrice, position.Quantity, exitReason, position.RealizedPnl);
        return position;
    }

    public Task UpdateUnrealizedAsync(Position position, decimal markPrice, CancellationToken cancellationToken = default)
    {
        position.UnrealizedPnl = (position.AveragePrice - markPrice) * position.Quantity;
        position.UpdatedAt = DateTime.UtcNow;
        return positionRepository.UpsertAsync(position, cancellationToken);
    }
}
