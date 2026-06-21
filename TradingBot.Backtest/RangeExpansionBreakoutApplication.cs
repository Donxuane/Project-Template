using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class RangeExpansionBreakoutApplication(BacktestSettings settings)
{
    public async Task<RangeExpansionBreakoutRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var profiles = BacktestApplication.ResolveRangeExpansionProfiles(settings);
        var allSymbols = profiles.SelectMany(p => p.Symbols).Distinct().ToArray();
        var dataLoader = new HistoricalKlineDataLoader(settings);
        var validatedDataBySymbol = new Dictionary<TradingSymbol, SymbolValidationResult>();

        foreach (var symbol in allSymbols)
            validatedDataBySymbol[symbol] = await dataLoader.LoadAndValidateAsync(symbol, cancellationToken);

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validatedDataBySymbol);
        var windows = RobustnessWindowResolver.Resolve(
            dataStartUtc,
            dataEndUtc,
            settings.RobustnessWindows,
            settings.RobustnessWindowStartUtc,
            settings.RobustnessWindowEndUtc);

        var runnableWindows = windows.Where(w => !w.SkippedInsufficientData).ToArray();
        var isMultiIntervalRun = settings.Intervals.Count > 1;
        var allCandidates = new List<RangeExpansionCandidateRecord>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();
        var allSummaries = new List<RangeExpansionSummaryRow>();

        foreach (var window in runnableWindows)
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            foreach (var interval in settings.Intervals)
            {
                var intervalCandidates = new List<RangeExpansionCandidateRecord>();
                var intervalTrades = new List<SimulatedTrade>();
                var intervalBlockedEntries = new List<BlockedEntryRecord>();

                foreach (var profile in profiles)
                {
                    if (!string.Equals(BacktestApplication.ResolveRangeExpansionProfileInterval(profile), interval, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var replayContext = BacktestApplication.BuildRangeExpansionReplayContext(settings, profile);
                    var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                    var profileCandidates = new List<RangeExpansionCandidateRecord>();

                    foreach (var symbol in profile.Symbols)
                    {
                        if (!windowDataBySymbol.TryGetValue(symbol, out var windowCandles) || windowCandles.Count == 0)
                            continue;

                        var aggregate = CandleAggregator.Aggregate(symbol, windowCandles, "1m", interval);
                        if (aggregate.Candles.Count == 0)
                            continue;

                        replayContext.Model.Reset();
                        var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                        var replayTrades = RangeExpansionBreakoutReplay.RunSymbolReplay(
                            interval,
                            profile.ProfileName,
                            profileSymbols,
                            replayContext,
                            symbol,
                            aggregate.Candles,
                            quantity,
                            settings.ForceCloseAtEnd,
                            intervalBlockedEntries,
                            profileCandidates,
                            windowCandles,
                            window.Label,
                            cancellationToken);
                        intervalTrades.AddRange(replayTrades);
                    }

                    intervalCandidates.AddRange(profileCandidates);
                }

                var windowIntervalSummaries = RangeExpansionBreakoutAggregator.BuildSummaries(
                    window.Label,
                    intervalCandidates,
                    intervalTrades);

                allCandidates.AddRange(intervalCandidates);
                allTrades.AddRange(intervalTrades);
                allBlockedEntries.AddRange(intervalBlockedEntries);
                allSummaries.AddRange(windowIntervalSummaries);

                var windowOutput = RobustnessApplication.ResolveRobustnessOutputDirectory(
                    settings.OutputDirectory,
                    window.Label,
                    interval,
                    isMultiIntervalRun);
                var windowDiagnostics = RangeExpansionDiagnosticsAggregator.Build(intervalCandidates, intervalTrades);
                var reportWriter = new RangeExpansionBreakoutReportWriter(windowOutput);
                await reportWriter.WriteAsync(
                    windowIntervalSummaries,
                    intervalCandidates,
                    intervalTrades,
                    intervalBlockedEntries,
                    [],
                    [],
                    cancellationToken,
                    windowDiagnostics);
            }
        }

        var robustnessSummaries = RangeExpansionBreakoutAggregator.BuildRobustnessSummaries(allSummaries);
        var researchAnswers = RangeExpansionBreakoutAggregator.BuildResearchAnswers(allCandidates, robustnessSummaries);
        var diagnostics = RangeExpansionDiagnosticsAggregator.Build(allCandidates, allTrades);
        var allAnswers = researchAnswers.Concat(diagnostics.DiagnosticAnswers).ToArray();
        Directory.CreateDirectory(settings.OutputDirectory);

        var rootWriter = new RangeExpansionBreakoutReportWriter(settings.OutputDirectory);
        await rootWriter.WriteAsync(
            allSummaries,
            allCandidates,
            allTrades,
            allBlockedEntries,
            robustnessSummaries,
            allAnswers,
            cancellationToken,
            diagnostics);

        return new RangeExpansionBreakoutRunResult(
            allCandidates,
            allTrades,
            allBlockedEntries,
            allSummaries,
            robustnessSummaries,
            allAnswers,
            diagnostics,
            profiles.Count);
    }
}
