using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class RobustnessApplication(BacktestSettings settings)
{
    public async Task<RobustnessRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var profiles = BacktestApplication.BuildRobustnessCandidateProfiles();
        var allSymbols = profiles.SelectMany(p => p.Symbols).Distinct().ToArray();

        var dataLoader = new HistoricalKlineDataLoader(settings);
        var validatedDataBySymbol = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in allSymbols)
        {
            validatedDataBySymbol[symbol] = await dataLoader.LoadAndValidateAsync(symbol, cancellationToken);
        }

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validatedDataBySymbol);
        var windows = RobustnessWindowResolver.Resolve(
            dataStartUtc,
            dataEndUtc,
            settings.RobustnessWindows,
            settings.RobustnessWindowStartUtc,
            settings.RobustnessWindowEndUtc);

        var runnableWindows = windows.Where(w => !w.SkippedInsufficientData).ToArray();
        var isMultiIntervalRun = settings.Intervals.Count > 1;

        var allWindowDetails = new List<RobustnessWindowDetailRow>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();

        foreach (var window in runnableWindows)
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            foreach (var interval in settings.Intervals)
            {
                var intervalTrades = new List<SimulatedTrade>();
                var intervalBlockedEntries = new List<BlockedEntryRecord>();
                var intervalWindowDetails = new List<RobustnessWindowDetailRow>();

                foreach (var profile in profiles)
                {
                    StrategyStaticStateResetter.ResetMovingAverageTrendStrategyState();
                    var replayContext = BacktestApplication.BuildProfileReplayContext(settings, profile);
                    var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                    var profileTrades = new List<SimulatedTrade>();

                    foreach (var symbol in profile.Symbols)
                    {
                        if (!windowDataBySymbol.TryGetValue(symbol, out var windowCandles) || windowCandles.Count == 0)
                            continue;

                        var aggregate = CandleAggregator.Aggregate(symbol, windowCandles, "1m", interval);
                        if (aggregate.Candles.Count == 0)
                            continue;

                        var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                        var replayTrades = BacktestApplication.RunSymbolReplay(
                            interval,
                            profile.ProfileName,
                            profileSymbols,
                            replayContext.Strategy,
                            symbol,
                            aggregate.Candles,
                            quantity,
                            settings.ForceCloseAtEnd,
                            replayContext.Guard,
                            replayContext.PullbackV2Filter,
                            replayContext.Simulator,
                            replayContext.SignalStats,
                            intervalBlockedEntries,
                            cancellationToken,
                            replayContext.BnbPullbackGuard,
                            replayContext.RuntimeSnapshot.ProfitLockThresholdPercent,
                            replayContext.RuntimeSnapshot.EnablePullbackFollowThroughV2,
                            replayContext.RetestContinuationModel);
                        profileTrades.AddRange(replayTrades);
                    }

                    intervalTrades.AddRange(profileTrades);
                    intervalWindowDetails.Add(RobustnessSummaryAggregator.BuildWindowDetail(
                        profile.ProfileName,
                        interval,
                        window,
                        profileTrades,
                        replayContext.SignalStats));
                }

                allWindowDetails.AddRange(intervalWindowDetails);
                allTrades.AddRange(intervalTrades);
                allBlockedEntries.AddRange(intervalBlockedEntries);

                var windowIntervalOutput = ResolveRobustnessOutputDirectory(
                    settings.OutputDirectory,
                    window.Label,
                    interval,
                    isMultiIntervalRun);
                var reportWriter = new ReplayReportWriter(windowIntervalOutput);
                var summaries = intervalWindowDetails.Select(d => new ReplaySummaryRow
                {
                    Interval = d.Interval,
                    ProfileName = d.ProfileName,
                    TradesCount = d.TradesCount,
                    EstimatedNetPnlQuote = d.EstimatedNetPnlQuote,
                    ProfitLockExitTrades = d.ProfitLockExitTrades,
                    OppositeSignalExitTrades = d.OppositeSignalExitTrades,
                    AverageMfePercent = d.AvgMfePercent,
                    AverageMaePercent = d.AvgMaePercent,
                    AvgGivebackFromMfePercent = d.AvgGivebackFromMfePercent,
                    AvgCapturedMfePercent = d.AvgCapturedMfePercent,
                    CapturedMfeCalculationMode = d.CapturedMfeCalculationMode,
                    AvgCapturedMfeIncludingNegativeRatio = d.AvgCapturedMfeIncludingNegativeRatio,
                    NegativeCaptureTradeCount = d.NegativeCaptureTradeCount
                }).ToArray();
                await reportWriter.WriteAsync(summaries, intervalTrades, intervalBlockedEntries, [], cancellationToken);
            }
        }

        var robustnessSummaries = RobustnessSummaryAggregator.BuildSummaries(allWindowDetails);
        var robustnessReportWriter = new RobustnessReportWriter(settings.OutputDirectory);
        await robustnessReportWriter.WriteAsync(allWindowDetails, robustnessSummaries, cancellationToken);

        return new RobustnessRunResult(
            allWindowDetails,
            robustnessSummaries,
            allTrades,
            allBlockedEntries,
            windows,
            profiles.Count);
    }

    public static string ResolveRobustnessOutputDirectory(
        string baseOutputDirectory,
        string windowLabel,
        string interval,
        bool multiIntervalRun)
    {
        var safeWindowLabel = windowLabel.Replace(":", "-", StringComparison.Ordinal);
        var windowDir = Path.Combine(baseOutputDirectory, safeWindowLabel);
        return multiIntervalRun ? Path.Combine(windowDir, interval) : windowDir;
    }
}
