using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class ImpulseContinuationV1Application(BacktestSettings settings)
{
    public async Task<ImpulseContinuationV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var profiles = BacktestApplication.BuildImpulseContinuationV1Profiles(settings.RunImpulseContinuationV1IncludeResearchVariants);
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
        var allCandidates = new List<ImpulseContinuationV1CandidateRecord>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();
        var allSummaries = new List<ImpulseContinuationV1SummaryRow>();

        foreach (var window in runnableWindows)
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            foreach (var interval in settings.Intervals)
            {
                var intervalCandidates = new List<ImpulseContinuationV1CandidateRecord>();
                var intervalTrades = new List<SimulatedTrade>();
                var intervalBlockedEntries = new List<BlockedEntryRecord>();

                foreach (var profile in profiles)
                {
                    if (!string.Equals(BacktestApplication.ResolveImpulseContinuationV1ProfileInterval(profile), interval, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var replayContext = BacktestApplication.BuildImpulseContinuationV1ReplayContext(settings, profile);
                    var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                    var profileCandidates = new List<ImpulseContinuationV1CandidateRecord>();

                    foreach (var symbol in profile.Symbols)
                    {
                        if (!windowDataBySymbol.TryGetValue(symbol, out var windowCandles) || windowCandles.Count == 0)
                            continue;

                        var aggregate = CandleAggregator.Aggregate(symbol, windowCandles, "1m", interval);
                        if (aggregate.Candles.Count == 0)
                            continue;

                        replayContext.Model.Reset();
                        var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                        var replayTrades = ImpulseContinuationV1Replay.RunSymbolReplay(
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

                var windowSummaries = ImpulseContinuationV1Aggregator.BuildSummaries(
                    window.Label, intervalCandidates, intervalTrades);

                allCandidates.AddRange(intervalCandidates);
                allTrades.AddRange(intervalTrades);
                allBlockedEntries.AddRange(intervalBlockedEntries);
                allSummaries.AddRange(windowSummaries);

                var windowOutput = RobustnessApplication.ResolveRobustnessOutputDirectory(
                    settings.OutputDirectory, window.Label, interval, multiIntervalRun: false);
                var exitBreakdown = ImpulseContinuationV1Aggregator.BuildExitBreakdown(intervalTrades);
                var reachability = ImpulseContinuationV1Aggregator.BuildReachabilityAnalysis(intervalCandidates);
                var windowRobustness = ImpulseContinuationV1Aggregator.BuildWindowRobustness(windowSummaries, intervalTrades);
                var answers = ImpulseContinuationV1Aggregator.BuildResearchAnswers(
                    intervalCandidates, intervalTrades, windowSummaries, exitBreakdown, windowRobustness);
                var writer = new ImpulseContinuationV1ReportWriter(windowOutput);
                await writer.WriteAsync(
                    windowSummaries, intervalCandidates, intervalTrades, intervalBlockedEntries,
                    answers, exitBreakdown, windowRobustness, reachability, cancellationToken);
            }
        }

        var exitBreakdownAll = ImpulseContinuationV1Aggregator.BuildExitBreakdown(allTrades);
        var reachabilityAll = ImpulseContinuationV1Aggregator.BuildReachabilityAnalysis(allCandidates);
        var windowRobustnessAll = ImpulseContinuationV1Aggregator.BuildWindowRobustness(allSummaries, allTrades);
        var researchAnswers = ImpulseContinuationV1Aggregator.BuildResearchAnswers(
            allCandidates, allTrades, allSummaries, exitBreakdownAll, windowRobustnessAll);

        Directory.CreateDirectory(settings.OutputDirectory);
        var rootWriter = new ImpulseContinuationV1ReportWriter(settings.OutputDirectory);
        await rootWriter.WriteAsync(
            allSummaries, allCandidates, allTrades, allBlockedEntries,
            researchAnswers, exitBreakdownAll, windowRobustnessAll, reachabilityAll, cancellationToken);

        return new ImpulseContinuationV1RunResult(
            allCandidates, allTrades, allBlockedEntries, allSummaries,
            researchAnswers, exitBreakdownAll, windowRobustnessAll, profiles.Count);
    }
}
