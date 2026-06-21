using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class HigherTimeframeMomentumPullbackV1Application(BacktestSettings settings)
{
    public async Task<HigherTimeframeMomentumPullbackV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var profiles = BacktestApplication.BuildHigherTimeframeMomentumPullbackV1Profiles(
            settings.RunHigherTimeframeMomentumPullbackV1IncludeResearchVariants);
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
        var allCandidates = new List<HigherTimeframeMomentumPullbackV1CandidateRecord>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();
        var allSummaries = new List<HigherTimeframeMomentumPullbackV1SummaryRow>();

        foreach (var window in runnableWindows)
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            foreach (var interval in settings.Intervals)
            {
                var intervalCandidates = new List<HigherTimeframeMomentumPullbackV1CandidateRecord>();
                var intervalTrades = new List<SimulatedTrade>();
                var intervalBlockedEntries = new List<BlockedEntryRecord>();

                foreach (var profile in profiles)
                {
                    if (!string.Equals(BacktestApplication.ResolveHigherTimeframeMomentumPullbackV1ProfileInterval(profile), interval, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var replayContext = BacktestApplication.BuildHigherTimeframeMomentumPullbackV1ReplayContext(settings, profile);
                    var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                    var profileCandidates = new List<HigherTimeframeMomentumPullbackV1CandidateRecord>();

                    foreach (var symbol in profile.Symbols)
                    {
                        if (!windowDataBySymbol.TryGetValue(symbol, out var windowCandles) || windowCandles.Count == 0)
                            continue;

                        var aggregate = CandleAggregator.Aggregate(symbol, windowCandles, "1m", interval);
                        if (aggregate.Candles.Count == 0)
                            continue;

                        replayContext.Model.Reset();
                        var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                        var replayTrades = HigherTimeframeMomentumPullbackV1Replay.RunSymbolReplay(
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

                var windowSummaries = HigherTimeframeMomentumPullbackV1Aggregator.BuildSummaries(
                    window.Label, intervalCandidates, intervalTrades);

                allCandidates.AddRange(intervalCandidates);
                allTrades.AddRange(intervalTrades);
                allBlockedEntries.AddRange(intervalBlockedEntries);
                allSummaries.AddRange(windowSummaries);

                var windowOutput = RobustnessApplication.ResolveRobustnessOutputDirectory(
                    settings.OutputDirectory, window.Label, interval, multiIntervalRun: false);
                var exitBreakdown = HigherTimeframeMomentumPullbackV1Aggregator.BuildExitBreakdown(intervalTrades);
                var windowRobustness = HigherTimeframeMomentumPullbackV1Aggregator.BuildWindowRobustness(windowSummaries);
                var answers = HigherTimeframeMomentumPullbackV1Aggregator.BuildResearchAnswers(
                    intervalCandidates, intervalTrades, windowSummaries, exitBreakdown, windowRobustness);
                var writer = new HigherTimeframeMomentumPullbackV1ReportWriter(windowOutput);
                await writer.WriteAsync(
                    windowSummaries, intervalCandidates, intervalTrades, intervalBlockedEntries,
                    answers, exitBreakdown, windowRobustness, cancellationToken);
            }
        }

        var exitBreakdownAll = HigherTimeframeMomentumPullbackV1Aggregator.BuildExitBreakdown(allTrades);
        var windowRobustnessAll = HigherTimeframeMomentumPullbackV1Aggregator.BuildWindowRobustness(allSummaries);
        var researchAnswers = HigherTimeframeMomentumPullbackV1Aggregator.BuildResearchAnswers(
            allCandidates, allTrades, allSummaries, exitBreakdownAll, windowRobustnessAll);

        Directory.CreateDirectory(settings.OutputDirectory);
        var rootWriter = new HigherTimeframeMomentumPullbackV1ReportWriter(settings.OutputDirectory);
        await rootWriter.WriteAsync(
            allSummaries, allCandidates, allTrades, allBlockedEntries,
            researchAnswers, exitBreakdownAll, windowRobustnessAll, cancellationToken);

        return new HigherTimeframeMomentumPullbackV1RunResult(
            allCandidates, allTrades, allBlockedEntries, allSummaries,
            researchAnswers, exitBreakdownAll, windowRobustnessAll, profiles.Count);
    }
}
