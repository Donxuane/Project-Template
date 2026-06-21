using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class RegimeGatedLongEdgeV1Application(BacktestSettings settings)
{
    public async Task<RegimeGatedLongEdgeV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        if (settings.RunRegimeGatedLongEdgeV1BtcContext)
        {
            var bootstrapDays = settings.BootstrapDays ?? settings.RobustnessWindows.DefaultIfEmpty(90).Max();
            await BtcContextDataBootstrap.EnsureBtcUsdtDataAsync(settings, bootstrapDays, cancellationToken);
        }

        var profiles = settings.RunRegimeGatedLongEdgeV1BtcContext
            ? BacktestApplication.BuildRegimeGatedLongEdgeV1BtcContextProfiles()
            : BacktestApplication.BuildRegimeGatedLongEdgeV1Profiles(
                settings.RunRegimeGatedLongEdgeV1IncludeResearchVariants);
        var allSymbols = profiles.SelectMany(p => p.Symbols).Distinct().ToArray();
        if (settings.RunRegimeGatedLongEdgeV1BtcContext)
            allSymbols = allSymbols.Append(TradingSymbol.BTCUSDT).Distinct().ToArray();
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
        var symbolOneMinute = validatedDataBySymbol.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<KlineCandle>)kv.Value.Candles);

        var allTrades = new List<RegimeGatedLongEdgeV1TradeRecord>();
        var allBlockedSignals = new List<RegimeGatedLongEdgeV1BlockedSignalRecord>();
        var allSummaries = new List<RegimeGatedLongEdgeV1SummaryRow>();

        foreach (var window in runnableWindows)
        {
            var windowDataBySymbol = validatedDataBySymbol.ToDictionary(
                kv => kv.Key,
                kv => CandleWindowSlicer.Slice(kv.Value.Candles, window.StartUtc, window.EndUtc));
            BtcContextIndex? btcContext = windowDataBySymbol.TryGetValue(TradingSymbol.BTCUSDT, out var windowBtcCandles) && windowBtcCandles.Count > 0
                ? new BtcContextIndex(windowBtcCandles)
                : null;
            var marketWideContext = new MarketWideContextIndex(
                windowDataBySymbol.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<KlineCandle>)kv.Value),
                includeBtcInProxy: settings.RunRegimeGatedLongEdgeV1BtcContext);

            foreach (var interval in settings.Intervals)
            {
                var intervalTrades = new List<RegimeGatedLongEdgeV1TradeRecord>();
                var intervalBlockedSignals = new List<RegimeGatedLongEdgeV1BlockedSignalRecord>();

                foreach (var profile in profiles)
                {
                    if (!string.Equals(BacktestApplication.ResolveRegimeGatedLongEdgeV1ProfileInterval(profile), interval, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var replayContext = BacktestApplication.BuildRegimeGatedLongEdgeV1ReplayContext(
                        settings, profile, btcContext, marketWideContext);
                    var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                    var ruleName = BacktestApplication.ResolveRegimeGatedLongEdgeV1RuleName(profile);

                    foreach (var symbol in profile.Symbols)
                    {
                        if (!windowDataBySymbol.TryGetValue(symbol, out var windowCandles) || windowCandles.Count == 0)
                            continue;

                        var aggregate = CandleAggregator.Aggregate(symbol, windowCandles, "1m", interval);
                        if (aggregate.Candles.Count == 0)
                            continue;

                        replayContext.Model.Reset();
                        var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                        RegimeGatedLongEdgeV1Replay.RunSymbolReplay(
                            interval,
                            profile.ProfileName,
                            ruleName,
                            profileSymbols,
                            replayContext,
                            symbol,
                            aggregate.Candles,
                            quantity,
                            settings.ForceCloseAtEnd,
                            intervalBlockedSignals,
                            intervalTrades,
                            window.Label,
                            cancellationToken);
                    }
                }

                var windowSummaries = RegimeGatedLongEdgeV1Aggregator.BuildSummaries(
                    window.Label, intervalTrades, intervalBlockedSignals);

                allTrades.AddRange(intervalTrades);
                allBlockedSignals.AddRange(intervalBlockedSignals);
                allSummaries.AddRange(windowSummaries);

                var windowOutput = RobustnessApplication.ResolveRobustnessOutputDirectory(
                    settings.OutputDirectory, window.Label, interval, multiIntervalRun: settings.Intervals.Count > 1);
                var rulePerformance = RegimeGatedLongEdgeV1Aggregator.BuildRulePerformance(intervalTrades);
                var windowRobustness = RegimeGatedLongEdgeV1Aggregator.BuildWindowRobustness(windowSummaries);
                var roundTripCost = RangeExpansionCostModel.ComputeRoundTripCostPercent(
                    new ExecutionCostSettings(settings.FeeRatePercent, settings.EstimatedSpreadPercent, settings.SlippagePercent));
                var gateThresholds = BacktestApplication.ResolveRegimeGatedLongEdgeV1GateThresholds();
                var answers = RegimeGatedLongEdgeV1Aggregator.BuildResearchAnswers(
                    intervalTrades, windowSummaries, rulePerformance, windowRobustness,
                    intervalBlockedSignals, roundTripCost, gateThresholds);

                var writer = new RegimeGatedLongEdgeV1ReportWriter(windowOutput);
                await writer.WriteAsync(
                    windowSummaries, intervalTrades, intervalBlockedSignals,
                    rulePerformance, windowRobustness, answers, cancellationToken);
            }
        }

        var rulePerformanceAll = RegimeGatedLongEdgeV1Aggregator.BuildRulePerformance(allTrades);
        var windowRobustnessAll = RegimeGatedLongEdgeV1Aggregator.BuildWindowRobustness(allSummaries);
        var roundTripCostAll = RangeExpansionCostModel.ComputeRoundTripCostPercent(
            new ExecutionCostSettings(settings.FeeRatePercent, settings.EstimatedSpreadPercent, settings.SlippagePercent));
        var gateThresholdsAll = BacktestApplication.ResolveRegimeGatedLongEdgeV1GateThresholds();
        var researchAnswers = RegimeGatedLongEdgeV1Aggregator.BuildResearchAnswers(
            allTrades, allSummaries, rulePerformanceAll, windowRobustnessAll,
            allBlockedSignals, roundTripCostAll, gateThresholdsAll);

        Directory.CreateDirectory(settings.OutputDirectory);
        var rootWriter = new RegimeGatedLongEdgeV1ReportWriter(settings.OutputDirectory);
        await rootWriter.WriteAsync(
            allSummaries, allTrades, allBlockedSignals,
            rulePerformanceAll, windowRobustnessAll, researchAnswers, cancellationToken);

        return new RegimeGatedLongEdgeV1RunResult(
            allTrades, allBlockedSignals, allSummaries, rulePerformanceAll,
            windowRobustnessAll, researchAnswers, profiles.Count);
    }
}
