namespace TradingBot.Backtest;

public static class RobustnessSummaryAggregator
{
    public static RobustnessWindowDetailRow BuildWindowDetail(
        string profileName,
        string interval,
        RobustnessWindow window,
        IReadOnlyList<SimulatedTrade> trades,
        ProfileSignalStats? signalStats = null)
    {
        var captured = CapturedMfeCalculator.Compute(trades);
        var netPnlBySymbol = trades
            .GroupBy(t => t.Symbol.ToString())
            .ToDictionary(g => g.Key, g => g.Sum(t => t.NetPnlQuote), StringComparer.OrdinalIgnoreCase);

        return new RobustnessWindowDetailRow
        {
            ProfileName = profileName,
            Interval = interval,
            WindowLabel = window.Label,
            WindowStartUtc = window.StartUtc,
            WindowEndUtc = window.EndUtc,
            TradesCount = trades.Count,
            EstimatedNetPnlQuote = trades.Sum(t => t.NetPnlQuote),
            NetPnlBySymbol = netPnlBySymbol,
            ProfitLockExitTrades = trades.Count(t => string.Equals(t.ExitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase)),
            OppositeSignalExitTrades = trades.Count(t => string.Equals(t.ExitReason, "OppositeSignal", StringComparison.OrdinalIgnoreCase)),
            AvgMfePercent = trades.Count == 0 ? 0m : trades.Average(t => t.MfePercent ?? 0m),
            AvgMaePercent = trades.Count == 0 ? 0m : trades.Average(t => t.MaePercent ?? 0m),
            AvgGivebackFromMfePercent = trades.Count == 0 ? 0m : trades.Average(t => t.GivebackFromMfePercent ?? 0m),
            AvgCapturedMfePercent = captured.AvgCapturedMfePercentPositiveOnly,
            CapturedMfeCalculationMode = captured.CalculationMode,
            AvgCapturedMfeIncludingNegativeRatio = captured.AvgCapturedMfeIncludingNegativeRatio,
            NegativeCaptureTradeCount = captured.NegativeCaptureTradeCount,
            BnbPullbackGuardEnabled = trades.Any(t => t.BnbPullbackGuardEnabled) || signalStats?.BnbPullbackGuardBlockedSignals > 0,
            BnbPullbackGuardBlockedSignals = signalStats?.BnbPullbackGuardBlockedSignals ?? 0,
            BnbPullbackGuardBlockedByReason = signalStats is null
                ? new Dictionary<string, int>()
                : new Dictionary<string, int>(signalStats.BnbPullbackGuardBlockedByReason, StringComparer.OrdinalIgnoreCase)
        };
    }

    public static IReadOnlyList<RobustnessSummaryRow> BuildSummaries(IReadOnlyList<RobustnessWindowDetailRow> windowDetails)
    {
        return windowDetails
            .GroupBy(x => (x.ProfileName, x.Interval))
            .Select(group => BuildSummary(group.Key.ProfileName, group.Key.Interval, group.ToArray()))
            .OrderBy(x => x.ProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Interval, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static RobustnessSummaryRow BuildSummary(
        string profileName,
        string interval,
        IReadOnlyList<RobustnessWindowDetailRow> windowsForProfile)
    {
        var allTradesPnl = windowsForProfile.Select(w => w.EstimatedNetPnlQuote).ToArray();
        var positiveWindows = windowsForProfile.Count(w => w.EstimatedNetPnlQuote > 0m);
        var negativeWindows = windowsForProfile.Count(w => w.EstimatedNetPnlQuote < 0m);
        var totalTrades = windowsForProfile.Sum(w => w.TradesCount);
        var totalNet = windowsForProfile.Sum(w => w.EstimatedNetPnlQuote);

        var netPnlBySymbol = windowsForProfile
            .SelectMany(w => w.NetPnlBySymbol)
            .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value), StringComparer.OrdinalIgnoreCase);

        var perTradePnls = windowsForProfile
            .Where(w => w.TradesCount > 0)
            .SelectMany(w => Enumerable.Repeat(w.EstimatedNetPnlQuote / w.TradesCount, w.TradesCount))
            .OrderBy(x => x)
            .ToArray();

        var medianPerTrade = perTradePnls.Length == 0
            ? 0m
            : perTradePnls.Length % 2 == 1
                ? perTradePnls[perTradePnls.Length / 2]
                : (perTradePnls[perTradePnls.Length / 2 - 1] + perTradePnls[perTradePnls.Length / 2]) / 2m;

        var weightedCaptured = WeightedAverage(
            windowsForProfile,
            w => w.AvgCapturedMfePercent,
            w => w.TradesCount);
        var weightedCapturedIncludingNegative = WeightedNullableAverage(
            windowsForProfile,
            w => w.AvgCapturedMfeIncludingNegativeRatio,
            w => w.TradesCount);

        var oneTradeWarning = totalTrades <= 1
            || windowsForProfile.Any(w => w.TradesCount == 1 && w.EstimatedNetPnlQuote > 0m);

        return new RobustnessSummaryRow
        {
            ProfileName = profileName,
            Interval = interval,
            WindowStartUtc = windowsForProfile.Min(w => w.WindowStartUtc),
            WindowEndUtc = windowsForProfile.Max(w => w.WindowEndUtc),
            WindowCount = windowsForProfile.Count,
            TradesCount = totalTrades,
            EstimatedNetPnlQuote = totalNet,
            NetPnlBySymbol = netPnlBySymbol,
            ProfitLockExitTrades = windowsForProfile.Sum(w => w.ProfitLockExitTrades),
            OppositeSignalExitTrades = windowsForProfile.Sum(w => w.OppositeSignalExitTrades),
            AvgMfePercent = WeightedAverage(windowsForProfile, w => w.AvgMfePercent, w => w.TradesCount),
            AvgMaePercent = WeightedAverage(windowsForProfile, w => w.AvgMaePercent, w => w.TradesCount),
            AvgGivebackFromMfePercent = WeightedAverage(windowsForProfile, w => w.AvgGivebackFromMfePercent, w => w.TradesCount),
            AvgCapturedMfePercent = weightedCaptured,
            CapturedMfeCalculationMode = CapturedMfeCalculator.CalculationMode,
            AvgCapturedMfeIncludingNegativeRatio = weightedCapturedIncludingNegative,
            NegativeCaptureTradeCount = windowsForProfile.Sum(w => w.NegativeCaptureTradeCount),
            PositiveWindowsCount = positiveWindows,
            NegativeWindowsCount = negativeWindows,
            MedianNetPnlPerTrade = medianPerTrade,
            MinWindowNetPnl = allTradesPnl.Length == 0 ? 0m : allTradesPnl.Min(),
            OneTradeProfileWarning = oneTradeWarning,
            BnbPullbackGuardEnabled = windowsForProfile.Any(w => w.BnbPullbackGuardEnabled),
            BnbPullbackGuardBlockedSignals = windowsForProfile.Sum(w => w.BnbPullbackGuardBlockedSignals),
            BnbPullbackGuardBlockedByReason = windowsForProfile
                .SelectMany(w => w.BnbPullbackGuardBlockedByReason)
                .GroupBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static decimal WeightedAverage(
        IReadOnlyList<RobustnessWindowDetailRow> rows,
        Func<RobustnessWindowDetailRow, decimal> selector,
        Func<RobustnessWindowDetailRow, int> weightSelector)
    {
        var totalWeight = rows.Sum(weightSelector);
        if (totalWeight == 0)
            return 0m;

        return rows.Sum(r => selector(r) * weightSelector(r)) / totalWeight;
    }

    private static decimal? WeightedNullableAverage(
        IReadOnlyList<RobustnessWindowDetailRow> rows,
        Func<RobustnessWindowDetailRow, decimal?> selector,
        Func<RobustnessWindowDetailRow, int> weightSelector)
    {
        var eligible = rows.Where(r => selector(r).HasValue && weightSelector(r) > 0).ToArray();
        if (eligible.Length == 0)
            return null;

        var totalWeight = eligible.Sum(weightSelector);
        return eligible.Sum(r => selector(r)!.Value * weightSelector(r)) / totalWeight;
    }
}
