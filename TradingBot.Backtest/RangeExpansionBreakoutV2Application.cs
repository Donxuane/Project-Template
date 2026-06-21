using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class RangeExpansionBreakoutV2Application(BacktestSettings settings)
{
    public async Task<RangeExpansionV2RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var isV2Feasibility = settings.RunRangeExpansionV2Feasibility;
        var isV24 = settings.RunRangeExpansionV24 && !isV2Feasibility;
        var isV23 = settings.RunRangeExpansionV23 && !isV24 && !isV2Feasibility;
        var isV22 = settings.RunRangeExpansionV22 && !isV23 && !isV24 && !isV2Feasibility;
        var isV21Fast = settings.RunRangeExpansionV21Fast && !isV22 && !isV23 && !isV24 && !isV2Feasibility;
        var profiles = isV2Feasibility
            ? BacktestApplication.BuildRangeExpansionV2FeasibilityProfiles(settings.RunRangeExpansionV2FeasibilityIncludeComparison)
            : isV24
                ? BacktestApplication.BuildRangeExpansionV24Profiles()
                : isV23
                    ? BacktestApplication.BuildRangeExpansionV23Profiles()
                    : isV22
                        ? BacktestApplication.BuildRangeExpansionV22Profiles()
                        : isV21Fast
                            ? BacktestApplication.BuildRangeExpansionV21FastProfiles()
                            : BacktestApplication.BuildRangeExpansionBreakoutV2Profiles(settings.RunRangeExpansionV2IncludeComparison);
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
        var allCandidates = new List<RangeExpansionV2CandidateRecord>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();
        var allSummaries = new List<RangeExpansionV2SummaryRow>();

        foreach (var window in runnableWindows)
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));

            foreach (var interval in settings.Intervals)
            {
                var intervalCandidates = new List<RangeExpansionV2CandidateRecord>();
                var intervalTrades = new List<SimulatedTrade>();
                var intervalBlockedEntries = new List<BlockedEntryRecord>();

                foreach (var profile in profiles)
                {
                    if (!string.Equals(BacktestApplication.ResolveRangeExpansionV2ProfileInterval(profile), interval, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var replayContext = BacktestApplication.BuildRangeExpansionV2ReplayContext(settings, profile);
                    var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                    var profileCandidates = new List<RangeExpansionV2CandidateRecord>();

                    foreach (var symbol in profile.Symbols)
                    {
                        if (!windowDataBySymbol.TryGetValue(symbol, out var windowCandles) || windowCandles.Count == 0)
                            continue;

                        var aggregate = CandleAggregator.Aggregate(symbol, windowCandles, "1m", interval);
                        if (aggregate.Candles.Count == 0)
                            continue;

                        replayContext.Model.Reset();
                        var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                        var replayTrades = RangeExpansionBreakoutV2Replay.RunSymbolReplay(
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

                var windowSummaries = RangeExpansionBreakoutV2Aggregator.BuildSummaries(
                    window.Label, intervalCandidates, intervalTrades);

                allCandidates.AddRange(intervalCandidates);
                allTrades.AddRange(intervalTrades);
                allBlockedEntries.AddRange(intervalBlockedEntries);
                allSummaries.AddRange(windowSummaries);

                var windowOutput = RobustnessApplication.ResolveRobustnessOutputDirectory(
                    settings.OutputDirectory, window.Label, interval, multiIntervalRun: false);
                var exitBreakdown = RangeExpansionBreakoutV2Aggregator.BuildExitBreakdown(intervalTrades);
                var costAnalysis = RangeExpansionBreakoutV2Aggregator.BuildCostAnalysis(intervalCandidates);
                var answers = RangeExpansionBreakoutV2Aggregator.BuildResearchAnswers(
                    intervalCandidates, intervalTrades, windowSummaries, exitBreakdown);
                var writer = new RangeExpansionBreakoutV2ReportWriter(windowOutput);
                await writer.WriteAsync(
                    windowSummaries, intervalCandidates, intervalTrades, intervalBlockedEntries,
                    answers, exitBreakdown, costAnalysis, cancellationToken);
            }
        }

        var exitBreakdownAll = RangeExpansionBreakoutV2Aggregator.BuildExitBreakdown(allTrades);
        var costAnalysisAll = RangeExpansionBreakoutV2Aggregator.BuildCostAnalysis(allCandidates);
        var researchAnswers = RangeExpansionBreakoutV2Aggregator.BuildResearchAnswers(
            allCandidates, allTrades, allSummaries, exitBreakdownAll);

        var extendedDiagnostics = RangeExpansionV2DiagnosticsAggregator.BuildExtended(
            allCandidates, allTrades, includeV21Summary: isV21Fast);

        var v22Diagnostics = isV22
            ? RangeExpansionV22DiagnosticsAggregator.Build(allCandidates, allTrades)
            : null;

        var v23Diagnostics = isV23
            ? RangeExpansionV23DiagnosticsAggregator.Build(
                allCandidates,
                allTrades,
                settings.FeeRatePercent,
                settings.EstimatedSpreadPercent)
            : null;

        var v24Diagnostics = isV24
            ? RangeExpansionV24DiagnosticsAggregator.Build(
                allCandidates,
                allTrades,
                settings.FeeRatePercent,
                settings.EstimatedSpreadPercent)
            : null;

        var feasibilityDiagnostics = isV2Feasibility
            ? RangeExpansionV2FeasibilityDiagnosticsAggregator.Build(
                allTrades,
                settings.FeeRatePercent,
                settings.EstimatedSpreadPercent,
                settings.SlippagePercent)
            : null;

        Directory.CreateDirectory(settings.OutputDirectory);
        var rootWriter = new RangeExpansionBreakoutV2ReportWriter(settings.OutputDirectory);
        await rootWriter.WriteAsync(
            allSummaries, allCandidates, allTrades, allBlockedEntries,
            researchAnswers, exitBreakdownAll, costAnalysisAll, cancellationToken);
        await rootWriter.WriteExtendedDiagnosticsAsync(extendedDiagnostics, isV21Fast, cancellationToken);
        if (v22Diagnostics is not null)
            await RangeExpansionV22ReportWriter.WriteAsync(settings.OutputDirectory, v22Diagnostics, cancellationToken);
        if (v23Diagnostics is not null)
            await RangeExpansionV23ReportWriter.WriteAsync(settings.OutputDirectory, v23Diagnostics, cancellationToken);
        if (v24Diagnostics is not null)
            await RangeExpansionV24ReportWriter.WriteAsync(settings.OutputDirectory, v24Diagnostics, cancellationToken);
        if (feasibilityDiagnostics is not null)
            await RangeExpansionV2FeasibilityReportWriter.WriteAsync(settings.OutputDirectory, feasibilityDiagnostics, cancellationToken);

        return new RangeExpansionV2RunResult(
            allCandidates, allTrades, allBlockedEntries, allSummaries,
            researchAnswers, exitBreakdownAll, extendedDiagnostics, profiles.Count);
    }
}
