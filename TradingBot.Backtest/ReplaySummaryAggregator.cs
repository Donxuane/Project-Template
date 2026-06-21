using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class ReplaySummaryAggregator
{
    public static ReplaySummaryRow BuildSummary(
        string interval,
        string profileName,
        string symbolsText,
        IReadOnlyList<SimulatedTrade> trades,
        ProfileSignalStats signalStats,
        ProfileRuntimeSnapshot profileRuntime)
    {
        var grossWins = trades.Count(x => x.GrossPnlQuote > 0m);
        var netWins = trades.Count(x => x.NetPnlQuote > 0m);
        var wins = netWins;
        var losses = trades.Count - netWins;
        var gross = trades.Sum(x => x.GrossPnlQuote);
        var net = trades.Sum(x => x.NetPnlQuote);
        var feeSpread = trades.Sum(x => x.FeeAndSpreadEstimateQuote);
        var avgWin = wins == 0 ? 0m : trades.Where(x => x.NetPnlQuote > 0m).Average(x => x.NetPnlQuote);
        var avgLoss = losses == 0 ? 0m : trades.Where(x => x.NetPnlQuote <= 0m).Average(x => x.NetPnlQuote);
        var maxConsecutiveLosses = CalculateMaxConsecutiveLosses(trades);
        var duration = trades.Count == 0 ? 0m : trades.Average(x => x.DurationMinutes);
        var winRate = trades.Count == 0 ? 0m : (wins * 100m) / trades.Count;
        var grossWinRate = trades.Count == 0 ? 0m : (grossWins * 100m) / trades.Count;
        var netWinRate = trades.Count == 0 ? 0m : (netWins * 100m) / trades.Count;
        var expectedTargetTouchTrades = trades.Count(x => x.TouchedExpectedTarget);
        var expectedTargetTouchRate = trades.Count == 0 ? 0m : (expectedTargetTouchTrades * 100m) / trades.Count;
        var averageMfe = trades.Count == 0 ? 0m : trades.Average(x => x.MfePercent ?? 0m);
        var averageMae = trades.Count == 0 ? 0m : trades.Average(x => x.MaePercent ?? 0m);
        var expectedTargetCounterfactualNet = trades
            .Where(x => x.CounterfactualExitAtExpectedTargetNetPnlQuote.HasValue)
            .Sum(x => x.CounterfactualExitAtExpectedTargetNetPnlQuote!.Value);
        var expectedTargetCounterfactualDelta = trades
            .Where(x => x.CounterfactualDeltaVsActualNetPnlQuote.HasValue)
            .Sum(x => x.CounterfactualDeltaVsActualNetPnlQuote!.Value);
        var profitCapture90TouchTrades = trades.Count(x => x.ProfitCapture90Touched);
        var profitCapture95TouchTrades = trades.Count(x => x.ProfitCapture95Touched);
        var profitCapture98TouchTrades = trades.Count(x => x.ProfitCapture98Touched);
        var profitCapture90CounterfactualNet = trades
            .Where(x => x.ProfitCapture90CounterfactualNetPnlQuote.HasValue)
            .Sum(x => x.ProfitCapture90CounterfactualNetPnlQuote!.Value);
        var profitCapture95CounterfactualNet = trades
            .Where(x => x.ProfitCapture95CounterfactualNetPnlQuote.HasValue)
            .Sum(x => x.ProfitCapture95CounterfactualNetPnlQuote!.Value);
        var profitCapture98CounterfactualNet = trades
            .Where(x => x.ProfitCapture98CounterfactualNetPnlQuote.HasValue)
            .Sum(x => x.ProfitCapture98CounterfactualNetPnlQuote!.Value);
        var profitCaptureDeltaVsOpposite = trades
            .Where(x => x.ProfitCaptureDeltaVsOppositeSignalExitQuote.HasValue)
            .Sum(x => x.ProfitCaptureDeltaVsOppositeSignalExitQuote!.Value);
        var profitLockExitTrades = trades.Count(x => string.Equals(x.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase));
        var oppositeSignalExitTrades = trades.Count(x => string.Equals(x.ExitReason, "OppositeSignal", StringComparison.OrdinalIgnoreCase));
        var breakevenExitTrades = trades.Count(x => string.Equals(x.ExitReason, "Breakeven", StringComparison.OrdinalIgnoreCase));
        var trailingExitTrades = trades.Count(x => string.Equals(x.ExitReason, "TrailingStop", StringComparison.OrdinalIgnoreCase));
        var avgGivebackFromMfe = trades.Count == 0 ? 0m : trades.Average(x => x.GivebackFromMfePercent ?? 0m);
        var capturedMfe = CapturedMfeCalculator.Compute(trades);
        var netPnlByExitPolicy = trades
            .GroupBy(x => x.ExitPolicyName)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.NetPnlQuote), StringComparer.OrdinalIgnoreCase);

        var symbolBreakdown = trades
            .GroupBy(x => x.Symbol)
            .ToDictionary(g => g.Key, g => g.Count());

        var exitReasonBreakdown = trades
            .GroupBy(x => x.ExitReason)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new ReplaySummaryRow
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            TradesCount = trades.Count,
            Wins = wins,
            Losses = losses,
            WinRatePercent = winRate,
            GrossPnlQuote = gross,
            EstimatedNetPnlQuote = net,
            TotalFeeAndSpreadEstimateQuote = feeSpread,
            AverageWinQuote = avgWin,
            AverageLossQuote = avgLoss,
            MaxConsecutiveLosses = maxConsecutiveLosses,
            AverageTradeDurationMinutes = duration,
            RawBuySignals = signalStats.RawBuySignals,
            ExecutedBuySignals = signalStats.ExecutedBuySignals,
            BlockedBuySignals = signalStats.BlockedBuySignals,
            StrategyRejectedBuySignals = signalStats.StrategyRejectedBuySignals,
            GrossWinningTrades = grossWins,
            GrossWinRatePercent = grossWinRate,
            NetWinningTrades = netWins,
            NetWinRatePercent = netWinRate,
            ExpectedTargetTouchTrades = expectedTargetTouchTrades,
            ExpectedTargetTouchRatePercent = expectedTargetTouchRate,
            AverageMfePercent = averageMfe,
            AverageMaePercent = averageMae,
            ExpectedTargetCounterfactualNetPnlQuote = expectedTargetCounterfactualNet,
            ExpectedTargetCounterfactualDeltaQuote = expectedTargetCounterfactualDelta,
            ProfitCapture90TouchTrades = profitCapture90TouchTrades,
            ProfitCapture95TouchTrades = profitCapture95TouchTrades,
            ProfitCapture98TouchTrades = profitCapture98TouchTrades,
            ProfitCapture90CounterfactualNetPnlQuote = profitCapture90CounterfactualNet,
            ProfitCapture95CounterfactualNetPnlQuote = profitCapture95CounterfactualNet,
            ProfitCapture98CounterfactualNetPnlQuote = profitCapture98CounterfactualNet,
            ProfitCaptureDeltaVsOppositeSignalExitQuote = profitCaptureDeltaVsOpposite,
            ExitPolicyName = profileRuntime.ExitPolicyName,
            ProfitLockThresholdPercent = profileRuntime.ProfitLockThresholdPercent,
            ProfitLockExitTrades = profitLockExitTrades,
            OppositeSignalExitTrades = oppositeSignalExitTrades,
            BreakevenExitTrades = breakevenExitTrades,
            TrailingExitTrades = trailingExitTrades,
            AvgGivebackFromMfePercent = avgGivebackFromMfe,
            AvgCapturedMfePercent = capturedMfe.AvgCapturedMfePercentPositiveOnly,
            CapturedMfeCalculationMode = capturedMfe.CalculationMode,
            AvgCapturedMfeIncludingNegativeRatio = capturedMfe.AvgCapturedMfeIncludingNegativeRatio,
            NegativeCaptureTradeCount = capturedMfe.NegativeCaptureTradeCount,
            BnbPullbackGuardEnabled = profileRuntime.BnbPullbackGuardEnabled,
            BnbPullbackGuardBlockedSignals = signalStats.BnbPullbackGuardBlockedSignals,
            BnbPullbackGuardBlockedByReason = new Dictionary<string, int>(signalStats.BnbPullbackGuardBlockedByReason, StringComparer.OrdinalIgnoreCase),
            NetPnlByExitPolicy = netPnlByExitPolicy,
            BlockedByReason = new Dictionary<string, int>(signalStats.BlockedByReason, StringComparer.OrdinalIgnoreCase),
            StrategyRejectedByReason = new Dictionary<string, int>(signalStats.StrategyRejectedByReason, StringComparer.OrdinalIgnoreCase),
            EnableLowVolatilityBreakoutEntry = profileRuntime.EnableLowVolatilityBreakoutEntry,
            EnableNormalTrendPullbackContinuationOverride = profileRuntime.EnableNormalTrendPullbackContinuationOverride,
            NormalTrendPullbackMinExpectedRewardRisk = profileRuntime.NormalTrendPullbackMinExpectedRewardRisk,
            EnableNormalTrendMinDistanceToInvalidationFilter = profileRuntime.EnableNormalTrendMinDistanceToInvalidationFilter,
            NormalTrendMinDistanceToInvalidationPercent = profileRuntime.NormalTrendMinDistanceToInvalidationPercent,
            EnableNormalTrendNearRecentHighRejection = profileRuntime.EnableNormalTrendNearRecentHighRejection,
            NormalTrendNearRecentHighRequiresRewardRisk = profileRuntime.NormalTrendNearRecentHighRequiresRewardRisk,
            NormalTrendNearRecentHighRequiresTrendStrengthPercent = profileRuntime.NormalTrendNearRecentHighRequiresTrendStrengthPercent,
            EnablePullbackOverrideHighVolatilityBlock = profileRuntime.EnablePullbackOverrideHighVolatilityBlock,
            EnableNormalTrendPullbackReclaimConfirmationFilter = profileRuntime.EnableNormalTrendPullbackReclaimConfirmationFilter,
            NormalTrendPullbackReclaimMode = profileRuntime.NormalTrendPullbackReclaimMode,
            EnablePullbackFollowThroughV2 = profileRuntime.EnablePullbackFollowThroughV2,
            EnablePullbackFollowThroughV3 = profileRuntime.EnablePullbackFollowThroughV3,
            PullbackV3MinResidualExpectedMovePercent = profileRuntime.PullbackV3MinResidualExpectedMovePercent,
            PullbackV3MinResidualNetMovePercent = profileRuntime.PullbackV3MinResidualNetMovePercent,
            PullbackV3MinResidualRewardRisk = profileRuntime.PullbackV3MinResidualRewardRisk,
            PullbackV3RejectIfTargetAlreadyMostlyConsumed = profileRuntime.PullbackV3RejectIfTargetAlreadyMostlyConsumed,
            PullbackV3MaxTargetConsumedPercent = profileRuntime.PullbackV3MaxTargetConsumedPercent,
            BreakoutLookbackCandles = profileRuntime.BreakoutLookbackCandles,
            BreakoutBufferPercent = profileRuntime.BreakoutBufferPercent,
            BreakoutConfirmationCandles = profileRuntime.BreakoutConfirmationCandles,
            MinBreakoutSlopePercent = profileRuntime.MinBreakoutSlopePercent,
            UseConfirmedClosedCandlesForLowVolBreakout = profileRuntime.UseConfirmedClosedCandlesForLowVolBreakout,
            SymbolBreakdown = symbolBreakdown,
            ExitReasonBreakdown = exitReasonBreakdown
        };
    }

    public static int CalculateMaxConsecutiveLosses(IReadOnlyList<SimulatedTrade> trades)
    {
        var max = 0;
        var current = 0;
        foreach (var trade in trades.OrderBy(x => x.EntryTimeUtc))
        {
            if (trade.NetPnlQuote <= 0m)
            {
                current++;
                if (current > max)
                    max = current;
            }
            else
            {
                current = 0;
            }
        }

        return max;
    }
}

public sealed record ProfileRuntimeSnapshot(
    bool EnableLowVolatilityBreakoutEntry,
    bool EnableNormalTrendPullbackContinuationOverride,
    decimal NormalTrendPullbackMinExpectedRewardRisk,
    bool EnableNormalTrendMinDistanceToInvalidationFilter,
    decimal NormalTrendMinDistanceToInvalidationPercent,
    bool EnableNormalTrendNearRecentHighRejection,
    decimal NormalTrendNearRecentHighRequiresRewardRisk,
    decimal? NormalTrendNearRecentHighRequiresTrendStrengthPercent,
    bool EnablePullbackOverrideHighVolatilityBlock,
    bool EnableNormalTrendPullbackReclaimConfirmationFilter,
    string NormalTrendPullbackReclaimMode,
    bool EnablePullbackFollowThroughV2,
    bool EnablePullbackFollowThroughV3,
    decimal PullbackV3MinResidualExpectedMovePercent,
    decimal PullbackV3MinResidualNetMovePercent,
    decimal PullbackV3MinResidualRewardRisk,
    bool PullbackV3RejectIfTargetAlreadyMostlyConsumed,
    decimal PullbackV3MaxTargetConsumedPercent,
    string ExitPolicyName,
    decimal? ProfitLockThresholdPercent,
    int BreakoutLookbackCandles,
    decimal BreakoutBufferPercent,
    int BreakoutConfirmationCandles,
    decimal MinBreakoutSlopePercent,
    bool UseConfirmedClosedCandlesForLowVolBreakout,
    bool BnbPullbackGuardEnabled,
    string BnbPullbackGuardMode);
