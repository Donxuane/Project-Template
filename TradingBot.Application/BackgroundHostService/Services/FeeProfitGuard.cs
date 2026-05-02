using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Binance;
using TradingBot.Domain.Interfaces.Repositories;
using TradingBot.Domain.Interfaces.Services;

namespace TradingBot.Application.BackgroundHostService.Services;

public class FeeProfitGuard(
    IConfiguration configuration,
    IPositionRepository positionRepository,
    IPriceCacheService priceCacheService,
    ILogger<FeeProfitGuard> logger) : IFeeProfitGuard
{
    public async Task<FeeProfitGuardResult> EvaluateAsync(FeeProfitGuardRequest request, CancellationToken cancellationToken = default)
    {
        var useFeeGuard = configuration.GetValue<bool?>("Trading:UseFeeGuard") ?? true;
        var feeRatePercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:FeeRatePercent") ?? 0.1m);
        var estimatedSpreadPercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:EstimatedSpreadPercent") ?? 0.05m);
        var minExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinExpectedMovePercent") ?? 0.3m);
        var minNetProfitPercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinNetProfitPercent") ?? 0.15m);

        if (!useFeeGuard || request.IsProtectiveExit)
        {
            var bypass = BuildResult(
                true,
                !useFeeGuard
                    ? "Fee guard disabled by configuration."
                    : "Protective exit bypasses fee/profit guard.",
                request.EntryPrice ?? 0m,
                request.TargetPrice ?? 0m,
                request.StopLossPrice,
                0m,
                0m,
                0m,
                estimatedSpreadPercent,
                0m,
                0m);
            LogResult(request, bypass);
            return bypass;
        }

        if (request.TradingMode == TradingMode.Futures)
        {
            var futuresUnsupported = BuildResult(
                false,
                "Futures execution intent is not supported by the current spot execution pipeline.",
                request.EntryPrice ?? 0m,
                request.TargetPrice ?? 0m,
                request.StopLossPrice,
                0m,
                feeRatePercent,
                feeRatePercent,
                estimatedSpreadPercent,
                feeRatePercent + feeRatePercent + estimatedSpreadPercent,
                0m);
            LogResult(request, futuresUnsupported);
            return futuresUnsupported;
        }

        if (request.TradingMode != TradingMode.Spot)
        {
            var unsupported = BuildResult(false, "Trading mode is not supported by fee/profit guard.", 0m, 0m, request.StopLossPrice, 0m, 0m, 0m, estimatedSpreadPercent, 0m, 0m);
            LogResult(request, unsupported);
            return unsupported;
        }

        if (request.ExecutionIntent == TradeExecutionIntent.OpenLong)
        {
            var entryPrice = request.EntryPrice
                ?? await priceCacheService.GetCachedPriceAsync(request.Symbol, cancellationToken)
                ?? 0m;
            var targetPrice = request.TargetPrice ?? 0m;
            if (entryPrice <= 0m || targetPrice <= 0m)
            {
                var invalid = BuildResult(
                    false,
                    "Skipped because expected profitability cannot be evaluated due to missing entry/target price.",
                    entryPrice,
                    targetPrice,
                    request.StopLossPrice,
                    0m,
                    feeRatePercent,
                    feeRatePercent,
                    estimatedSpreadPercent,
                    feeRatePercent + feeRatePercent + estimatedSpreadPercent,
                    0m);
                LogResult(request, invalid);
                return invalid;
            }

            var gross = ((targetPrice - entryPrice) / entryPrice) * 100m;
            var estimatedEntryFeePercent = feeRatePercent;
            var estimatedExitFeePercent = feeRatePercent;
            var totalCost = estimatedEntryFeePercent + estimatedExitFeePercent + estimatedSpreadPercent;
            var net = gross - totalCost;

            if (gross < minExpectedMovePercent)
            {
                var blocked = BuildResult(
                    false,
                    "Skipped because expected gross move is below minimum threshold.",
                    entryPrice,
                    targetPrice,
                    request.StopLossPrice,
                    gross,
                    estimatedEntryFeePercent,
                    estimatedExitFeePercent,
                    estimatedSpreadPercent,
                    totalCost,
                    net);
                LogResult(request, blocked);
                return blocked;
            }

            if (net < minNetProfitPercent)
            {
                var blocked = BuildResult(
                    false,
                    "Skipped because expected net profit after fees/spread is below minimum threshold.",
                    entryPrice,
                    targetPrice,
                    request.StopLossPrice,
                    gross,
                    estimatedEntryFeePercent,
                    estimatedExitFeePercent,
                    estimatedSpreadPercent,
                    totalCost,
                    net);
                LogResult(request, blocked);
                return blocked;
            }

            var allowed = BuildResult(
                true,
                "Fee/profit guard passed.",
                entryPrice,
                targetPrice,
                request.StopLossPrice,
                gross,
                estimatedEntryFeePercent,
                estimatedExitFeePercent,
                estimatedSpreadPercent,
                totalCost,
                net);
            LogResult(request, allowed);
            return allowed;
        }

        if (request.ExecutionIntent == TradeExecutionIntent.CloseLong)
        {
            var openPosition = await positionRepository.GetOpenPositionAsync(request.Symbol, cancellationToken);
            var positionEntryPrice = openPosition?.AveragePrice ?? 0m;
            var closePrice = request.EntryPrice
                             ?? await priceCacheService.GetCachedPriceAsync(request.Symbol, cancellationToken)
                             ?? 0m;
            if (positionEntryPrice <= 0m || closePrice <= 0m)
            {
                var invalid = BuildResult(
                    false,
                    "Skipped because close profitability cannot be evaluated due to missing position entry/close price.",
                    positionEntryPrice,
                    closePrice,
                    request.StopLossPrice,
                    0m,
                    0m,
                    feeRatePercent,
                    estimatedSpreadPercent,
                    feeRatePercent + estimatedSpreadPercent,
                    0m);
                LogResult(request, invalid);
                return invalid;
            }

            var gross = ((closePrice - positionEntryPrice) / positionEntryPrice) * 100m;
            var estimatedEntryFeePercent = 0m;
            var estimatedExitFeePercent = feeRatePercent;
            var totalCost = estimatedExitFeePercent + estimatedSpreadPercent;
            var net = gross - totalCost;
            var allowed = BuildResult(
                true,
                "Fee/profit guard passed for spot close-long.",
                positionEntryPrice,
                closePrice,
                request.StopLossPrice,
                gross,
                estimatedEntryFeePercent,
                estimatedExitFeePercent,
                estimatedSpreadPercent,
                totalCost,
                net);
            LogResult(request, allowed);
            return allowed;
        }

        var passthrough = BuildResult(
            true,
            "Fee/profit guard skipped for unsupported/non-entry intent.",
            request.EntryPrice ?? 0m,
            request.TargetPrice ?? 0m,
            request.StopLossPrice,
            0m,
            0m,
            0m,
            estimatedSpreadPercent,
            0m,
            0m);
        LogResult(request, passthrough);
        return passthrough;
    }

    private void LogResult(FeeProfitGuardRequest request, FeeProfitGuardResult result)
    {
        logger.LogInformation(
            "FeeProfitGuard evaluated: Symbol={Symbol}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, EntryPrice={EntryPrice}, TargetPrice={TargetPrice}, StopLossPrice={StopLossPrice}, GrossExpectedProfitPercent={GrossExpectedProfitPercent}, EstimatedEntryFeePercent={EstimatedEntryFeePercent}, EstimatedExitFeePercent={EstimatedExitFeePercent}, EstimatedSpreadPercent={EstimatedSpreadPercent}, EstimatedTotalCostPercent={EstimatedTotalCostPercent}, NetExpectedProfitPercent={NetExpectedProfitPercent}, Allowed={Allowed}, Reason={Reason}",
            request.Symbol,
            request.TradingMode,
            request.ExecutionIntent,
            request.Side,
            request.Quantity,
            result.EntryPrice,
            result.TargetPrice,
            result.StopLossPrice,
            result.GrossExpectedProfitPercent,
            result.EstimatedEntryFeePercent,
            result.EstimatedExitFeePercent,
            result.EstimatedSpreadPercent,
            result.EstimatedTotalCostPercent,
            result.NetExpectedProfitPercent,
            result.IsAllowed,
            result.Reason);
    }

    private static FeeProfitGuardResult BuildResult(
        bool allowed,
        string reason,
        decimal entryPrice,
        decimal targetPrice,
        decimal? stopLossPrice,
        decimal gross,
        decimal entryFee,
        decimal exitFee,
        decimal spread,
        decimal totalCost,
        decimal net)
    {
        return new FeeProfitGuardResult
        {
            IsAllowed = allowed,
            Reason = reason,
            EntryPrice = entryPrice,
            TargetPrice = targetPrice,
            StopLossPrice = stopLossPrice,
            GrossExpectedProfitPercent = gross,
            EstimatedEntryFeePercent = entryFee,
            EstimatedExitFeePercent = exitFee,
            EstimatedSpreadPercent = spread,
            EstimatedTotalCostPercent = totalCost,
            NetExpectedProfitPercent = net
        };
    }
}
