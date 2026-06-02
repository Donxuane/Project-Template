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
            BlockedByReason = new Dictionary<string, int>(signalStats.BlockedByReason, StringComparer.OrdinalIgnoreCase),
            EnableLowVolatilityBreakoutEntry = profileRuntime.EnableLowVolatilityBreakoutEntry,
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
    int BreakoutLookbackCandles,
    decimal BreakoutBufferPercent,
    int BreakoutConfirmationCandles,
    decimal MinBreakoutSlopePercent,
    bool UseConfirmedClosedCandlesForLowVolBreakout);
