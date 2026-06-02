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
    ISpotCommissionRateResolver spotCommissionRateResolver,
    IFeeProfitGuardExpectedMoveBlockObservability expectedMoveBlockObservability,
    ILogger<FeeProfitGuard> logger) : IFeeProfitGuard
{
    public async Task<FeeProfitGuardResult> EvaluateAsync(FeeProfitGuardRequest request, CancellationToken cancellationToken = default)
    {
        var useFeeGuard = configuration.GetValue<bool?>("Trading:UseFeeGuard") ?? true;
        var commissionResolution = await spotCommissionRateResolver.ResolveFeeRatePercentAsync(request.Symbol, cancellationToken);
        var feeRatePercent = Math.Max(0m, commissionResolution.FeeRatePercent);
        var feeRateSource = string.IsNullOrWhiteSpace(commissionResolution.FeeRateSource)
            ? "UnknownFallback"
            : commissionResolution.FeeRateSource;
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
                0m,
                feeRateSource);
            LogResult(request, bypass);
            LogSpotOpenLongEvaluationIfApplicable(request, bypass, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
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
                0m,
                feeRateSource);
            LogResult(request, futuresUnsupported);
            LogSpotOpenLongEvaluationIfApplicable(request, futuresUnsupported, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
            return futuresUnsupported;
        }

        if (request.TradingMode != TradingMode.Spot)
        {
            var unsupported = BuildResult(false, "Trading mode is not supported by fee/profit guard.", 0m, 0m, request.StopLossPrice, 0m, 0m, 0m, estimatedSpreadPercent, 0m, 0m, feeRateSource);
            LogResult(request, unsupported);
            LogSpotOpenLongEvaluationIfApplicable(request, unsupported, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
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
                    0m,
                    feeRateSource);
                LogResult(request, invalid);
                LogSpotOpenLongEvaluationIfApplicable(request, invalid, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
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
                    net,
                    feeRateSource);
                LogResult(request, blocked);
                LogSpotOpenLongEvaluationIfApplicable(request, blocked, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
                RecordExpectedMoveBlockIfApplicable(request, blocked);
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
                    net,
                    feeRateSource);
                LogResult(request, blocked);
                LogSpotOpenLongEvaluationIfApplicable(request, blocked, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
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
                net,
                feeRateSource);
            LogResult(request, allowed);
            LogSpotOpenLongEvaluationIfApplicable(request, allowed, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
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
                    0m,
                    feeRateSource);
                LogResult(request, invalid);
                LogSpotOpenLongEvaluationIfApplicable(request, invalid, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
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
                net,
                feeRateSource);
            LogResult(request, allowed);
            LogSpotOpenLongEvaluationIfApplicable(request, allowed, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
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
            0m,
            feeRateSource);
        LogResult(request, passthrough);
        LogSpotOpenLongEvaluationIfApplicable(request, passthrough, feeRatePercent, minExpectedMovePercent, minNetProfitPercent);
        return passthrough;
    }

    private void LogResult(FeeProfitGuardRequest request, FeeProfitGuardResult result)
    {
        logger.LogInformation(
            "FeeProfitGuard evaluated: Symbol={Symbol}, TradingMode={TradingMode}, ExecutionIntent={ExecutionIntent}, Side={Side}, Quantity={Quantity}, EntryPrice={EntryPrice}, TargetPrice={TargetPrice}, StopLossPrice={StopLossPrice}, GrossExpectedProfitPercent={GrossExpectedProfitPercent}, EstimatedEntryFeePercent={EstimatedEntryFeePercent}, EstimatedExitFeePercent={EstimatedExitFeePercent}, EstimatedSpreadPercent={EstimatedSpreadPercent}, EstimatedTotalCostPercent={EstimatedTotalCostPercent}, NetExpectedProfitPercent={NetExpectedProfitPercent}, FeeRateSource={FeeRateSource}, Allowed={Allowed}, Reason={Reason}",
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
            result.FeeRateSource,
            result.IsAllowed,
            result.Reason);
    }

    private void LogSpotOpenLongEvaluationIfApplicable(
        FeeProfitGuardRequest request,
        FeeProfitGuardResult result,
        decimal feeRatePercent,
        decimal minExpectedMovePercent,
        decimal minNetProfitPercent)
    {
        if (request.TradingMode != TradingMode.Spot || request.ExecutionIntent != TradeExecutionIntent.OpenLong)
            return;

        var targetSource = string.IsNullOrWhiteSpace(request.TargetSource)
            ? "Unknown"
            : request.TargetSource;
        var caller = string.IsNullOrWhiteSpace(request.Caller)
            ? "Unknown"
            : request.Caller;
        var rejectionReason = result.IsAllowed ? "None" : result.Reason;
        var outcome = result.IsAllowed ? "Allowed" : "Blocked";

        logger.LogInformation(
            "FeeProfitGuard Spot OpenLong evaluation {Outcome}: Symbol={Symbol}, EntryPrice={EntryPrice}, TargetPrice={TargetPrice}, ExpectedTargetPrice={ExpectedTargetPrice}, ExpectedMovePercent={ExpectedMovePercent}, ExpectedTargetSource={ExpectedTargetSource}, RecentSwingHigh={RecentSwingHigh}, RecentSwingLow={RecentSwingLow}, RangeOrAtrExtensionUsed={RangeOrAtrExtensionUsed}, AtrUsed={AtrUsed}, TargetSource={TargetSource}, GrossExpectedMovePercent={GrossExpectedMovePercent}, FeeRatePercent={FeeRatePercent}, FeeRateSource={FeeRateSource}, EstimatedSpreadPercent={EstimatedSpreadPercent}, SpreadPercent={SpreadPercent}, EstimatedRoundTripCostPercent={EstimatedRoundTripCostPercent}, MinExpectedMovePercent={MinExpectedMovePercent}, MinNetProfitPercent={MinNetProfitPercent}, ExpectedNetProfitPercent={ExpectedNetProfitPercent}, Allowed={Allowed}, RejectionReason={RejectionReason}, Caller={Caller}",
            outcome,
            request.Symbol,
            result.EntryPrice,
            result.TargetPrice,
            request.TargetPrice,
            request.ExpectedMovePercent,
            targetSource,
            request.RecentSwingHigh,
            request.RecentSwingLow,
            request.RangeOrAtrExtensionUsed,
            request.AtrUsed,
            targetSource,
            result.GrossExpectedProfitPercent,
            feeRatePercent,
            result.FeeRateSource,
            result.EstimatedSpreadPercent,
            result.EstimatedSpreadPercent,
            result.EstimatedTotalCostPercent,
            minExpectedMovePercent,
            minNetProfitPercent,
            result.NetExpectedProfitPercent,
            result.IsAllowed,
            rejectionReason,
            caller);

        if (!result.IsAllowed)
        {
            logger.LogInformation(
                "Spot OpenLong candidate rejected: Symbol={Symbol}, EntryPrice={EntryPrice}, ExpectedTargetPrice={ExpectedTargetPrice}, ExpectedMovePercent={ExpectedMovePercent}, ExpectedTargetSource={ExpectedTargetSource}, RecentSwingHigh={RecentSwingHigh}, RecentSwingLow={RecentSwingLow}, RangeOrAtrExtensionUsed={RangeOrAtrExtensionUsed}, AtrUsed={AtrUsed}, MinExpectedMovePercent={MinExpectedMovePercent}, MinNetProfitPercent={MinNetProfitPercent}, FeeRatePercent={FeeRatePercent}, SpreadPercent={SpreadPercent}, GrossExpectedMovePercent={GrossExpectedMovePercent}, ExpectedNetProfitPercent={ExpectedNetProfitPercent}, RejectionReason={RejectionReason}, Caller={Caller}",
                request.Symbol,
                result.EntryPrice,
                request.TargetPrice,
                request.ExpectedMovePercent,
                targetSource,
                request.RecentSwingHigh,
                request.RecentSwingLow,
                request.RangeOrAtrExtensionUsed,
                request.AtrUsed,
                minExpectedMovePercent,
                minNetProfitPercent,
                feeRatePercent,
                result.EstimatedSpreadPercent,
                result.GrossExpectedProfitPercent,
                result.NetExpectedProfitPercent,
                rejectionReason,
                caller);
        }
    }

    private void RecordExpectedMoveBlockIfApplicable(FeeProfitGuardRequest request, FeeProfitGuardResult result)
    {
        if (request.TradingMode != TradingMode.Spot || request.ExecutionIntent != TradeExecutionIntent.OpenLong || result.IsAllowed)
            return;

        if (!string.Equals(
                result.Reason,
                "Skipped because expected gross move is below minimum threshold.",
                StringComparison.Ordinal))
            return;

        expectedMoveBlockObservability.RecordExpectedMoveBlock(new FeeProfitGuardExpectedMoveBlockObservation
        {
            Symbol = request.Symbol,
            ExpectedMovePercent = request.ExpectedMovePercent ?? result.GrossExpectedProfitPercent,
            ExpectedNetProfitPercent = result.NetExpectedProfitPercent,
            ExpectedTargetSource = string.IsNullOrWhiteSpace(request.TargetSource) ? "Unknown" : request.TargetSource,
            Confidence = null,
            RejectionReason = result.Reason
        });
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
        decimal net,
        string feeRateSource)
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
            NetExpectedProfitPercent = net,
            FeeRateSource = feeRateSource
        };
    }
}
