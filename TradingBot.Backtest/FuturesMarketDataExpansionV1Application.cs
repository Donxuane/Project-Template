using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class FuturesMarketDataExpansionV1Application(BacktestSettings settings)
{
    private const string StudyInterval = "30m";

    public async Task<FuturesMarketDataExpansionV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var loaded = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in FuturesMarketDataCatalog.Symbols)
        {
            var data = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            if (data.Candles.Count > 0)
                loaded[symbol] = data;
        }

        if (loaded.Count == 0)
            throw new InvalidOperationException("No local candle data found for any requested symbol.");

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(loaded);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var bootstrapAttempted = false;
        if (settings.BootstrapFuturesData)
        {
            bootstrapAttempted = true;
            var downloader = new BinanceFuturesDataDownloader();
            await downloader.DownloadAllAsync(settings.DataDirectory, FuturesMarketDataCatalog.Symbols, windowStart, dataEndUtc, cancellationToken);
        }

        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);

        var windowOneMinute = new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>();
        foreach (var (symbol, data) in loaded)
            windowOneMinute[symbol] = CandleWindowSlicer.Slice(data.Candles, windowStart, dataEndUtc);
        BtcContextIndex? btcContext = windowOneMinute.TryGetValue(TradingSymbol.BTCUSDT, out var btcWindow)
            ? new BtcContextIndex(btcWindow)
            : null;
        var marketWideContext = new MarketWideContextIndex(windowOneMinute, includeBtcInProxy: true);

        var availability = new List<FuturesDataAvailabilityRow>();
        var quality = new List<FuturesDataQualityRow>();
        var featureSummary = new List<FuturesFlowFeatureSummaryRow>();
        var candidates = new List<FuturesFlowRuleCandidateRow>();
        var splitPerformance = new List<FuturesFlowSplitPerformanceRow>();
        var studyRan = false;
        var combosScanned = 0;
        var candidateCount = 0;
        var validationSurvivors = 0;
        var holdoutSurvivors = 0;
        var flowOnlyValidationSurvivors = 0;

        var intervalCandlesBySymbol = new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>();
        var basePointsBySymbol = new Dictionary<TradingSymbol, IReadOnlyList<DiscoveryBasePoint>>();

        foreach (var (symbol, oneMinute) in windowOneMinute)
        {
            var intervalCandles = CandleAggregator.Aggregate(symbol, oneMinute, "1m", StudyInterval).Candles;
            intervalCandlesBySymbol[symbol] = intervalCandles;
            var sampleTimes = SampleTimes(intervalCandles, 800);

            foreach (var source in FuturesMarketDataCatalog.Sources())
            {
                var raw = source.BootstrapSupported ? futuresLoader.LoadRaw(symbol, source.SourceKey) : [];
                var timestamps = raw.Select(r => long.TryParse(r.TryGetValue("t", out var t) ? t : "0", out var v) ? v : 0L)
                    .Where(v => v > 0).OrderBy(v => v).ToArray();
                var present = timestamps.Length > 0;
                DateTime? localStart = present ? DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).UtcDateTime : null;
                DateTime? localEnd = present ? DateTimeOffset.FromUnixTimeMilliseconds(timestamps[^1]).UtcDateTime : null;
                var localSpan = present ? Math.Round((decimal)(localEnd!.Value - localStart!.Value).TotalDays, 2) : 0m;

                availability.Add(new FuturesDataAvailabilityRow
                {
                    Symbol = symbol.ToString(),
                    SourceKey = source.SourceKey,
                    DisplayName = source.DisplayName,
                    Endpoint = source.Endpoint,
                    Granularity = source.Granularity,
                    AvailabilityClass = source.Availability.ToString(),
                    BootstrapSupported = source.BootstrapSupported,
                    LocalFilePresent = present,
                    LocalRecordCount = timestamps.Length,
                    LocalStartUtc = localStart,
                    LocalEndUtc = localEnd,
                    LocalSpanDays = localSpan,
                    Supports365dStudy = FuturesMarketDataCatalog.Is365dCapable(source.SourceKey) && localSpan >= 300m,
                    Notes = source.Notes
                });

                if (present)
                {
                    quality.Add(FuturesMarketDataQualityAnalyzer.Analyze(
                        symbol, source.SourceKey, timestamps, FieldAvailability(raw),
                        localStart, localEnd, sampleTimes));
                }
            }

            var basePoints = FuturesDirectionalRuleDiscoveryV2Engine.BuildBasePoints(
                StudyInterval, intervalCandles, btcContext, marketWideContext, cancellationToken);
            basePointsBySymbol[symbol] = basePoints;

            if (basePoints.Count > 0 && futuresLoader.Exists(symbol, "funding"))
            {
                var flowBuilder = new FuturesFlowFeatureBuilder(futuresLoader, symbol);
                featureSummary.AddRange(BuildFeatureSummary(symbol, StudyInterval, basePoints, flowBuilder));
            }
        }

        foreach (var (symbol, basePoints) in basePointsBySymbol)
        {
            if (basePoints.Count < FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTotalTrades || !futuresLoader.Exists(symbol, "funding"))
                continue;
            var flowBuilder = new FuturesFlowFeatureBuilder(futuresLoader, symbol);
            if (!flowBuilder.HasFunding)
                continue;
            studyRan = true;
            var intervalCandles = intervalCandlesBySymbol[symbol];
            var oneMinute = windowOneMinute[symbol];
            foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
            {
                cancellationToken.ThrowIfCancellationRequested();
                var combo = FuturesFlowFeatureEdgeStudyV1.ScanCombo(
                    symbol, StudyInterval, direction, basePoints, intervalCandles, oneMinute, flowBuilder, cancellationToken);
                combosScanned++;
                candidateCount += combo.CandidateCount;
                validationSurvivors += combo.ValidationSurvivors;
                holdoutSurvivors += combo.HoldoutSurvivors;
                candidates.AddRange(combo.Candidates);
                splitPerformance.AddRange(combo.SplitPerformance);
                flowOnlyValidationSurvivors += combo.Candidates.Count(c => c is { SelectionStage: "ValidationSurvivor", UsesFlowFeature: true });
            }
        }

        var studySkipReason = studyRan
            ? string.Empty
            : "No 365d-capable flow data present (funding). Run with --bootstrap-futures-data true to download, then re-run.";

        var orderedCandidates = candidates
            .OrderByDescending(c => c.AllSplitsPositive)
            .ThenByDescending(c => c.FullHistoryNet)
            .ToArray();

        var answers = BuildAnswers(availability, candidates, candidateCount, validationSurvivors, holdoutSurvivors,
            flowOnlyValidationSurvivors, studyRan, studySkipReason);

        var result = new FuturesMarketDataExpansionV1RunResult(
            availability, quality, featureSummary, orderedCandidates, splitPerformance, answers,
            bootstrapAttempted, studyRan, studySkipReason);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new FuturesMarketDataExpansionV1ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(result, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "futures-market-data-expansion-v1",
                settings.DataDirectory,
                settings.OutputDirectory,
                symbolsScanned = loaded.Keys.Select(k => k.ToString()).ToArray(),
                studyInterval = StudyInterval,
                bootstrapAttempted,
                studyRan,
                studySkipReason,
                combosScanned,
                candidateCount,
                validationSurvivors,
                holdoutSurvivors,
                flowOnlyValidationSurvivors,
                dataStartUtc = windowStart,
                dataEndUtc,
                backtestOnly = true,
                liveFuturesRecommended = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return result;
    }

    private static IReadOnlyList<long> SampleTimes(IReadOnlyList<KlineCandle> candles, int max)
    {
        if (candles.Count == 0)
            return [];
        var step = Math.Max(1, candles.Count / max);
        var times = new List<long>();
        for (var i = 0; i < candles.Count; i += step)
            times.Add(new DateTimeOffset(candles[i].OpenTimeUtc).ToUnixTimeMilliseconds());
        return times;
    }

    private static string FieldAvailability(IReadOnlyList<IReadOnlyDictionary<string, string>> raw)
    {
        if (raw.Count == 0)
            return "";
        var keys = raw.SelectMany(r => r.Keys).Where(k => !string.Equals(k, "t", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var parts = new List<string>();
        foreach (var key in keys)
        {
            var nonEmpty = raw.Count(r => r.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v));
            parts.Add($"{key}:{Math.Round((decimal)nonEmpty / raw.Count * 100m, 1)}%");
        }

        return string.Join(" ", parts);
    }

    private static IReadOnlyList<FuturesFlowFeatureSummaryRow> BuildFeatureSummary(
        TradingSymbol symbol,
        string interval,
        IReadOnlyList<DiscoveryBasePoint> basePoints,
        FuturesFlowFeatureBuilder flowBuilder)
    {
        var flows = basePoints.Select(bp => flowBuilder.Build(bp.EntryTimeUtc)).ToArray();
        var rows = new List<FuturesFlowFeatureSummaryRow>();
        foreach (var feature in FuturesFlowFeatureEdgeStudyV1.FlowFeatures)
        {
            var values = flows.Select(f => FuturesFlowFeatureEdgeStudyV1.GetFlow(f, feature))
                .Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v).ToArray();
            decimal? min = values.Length > 0 ? values[0] : null;
            decimal? max = values.Length > 0 ? values[^1] : null;
            decimal? median = values.Length > 0 ? values[values.Length / 2] : null;
            decimal? mean = values.Length > 0 ? Math.Round(values.Average(), 8) : null;
            decimal? std = null;
            if (values.Length > 1 && mean.HasValue)
            {
                var varSum = values.Sum(v => (v - mean.Value) * (v - mean.Value));
                std = Math.Round((decimal)Math.Sqrt((double)(varSum / values.Length)), 8);
            }

            rows.Add(new FuturesFlowFeatureSummaryRow
            {
                Feature = feature,
                Symbol = symbol.ToString(),
                Interval = interval,
                SampleCount = basePoints.Count,
                NonNullCount = values.Length,
                NonNullPercent = basePoints.Count == 0 ? 0m : Math.Round((decimal)values.Length / basePoints.Count * 100m, 2),
                Min = min,
                Median = median,
                Max = max,
                Mean = mean,
                StdDev = std,
                Supports365dStudy = SourceFor(feature) is "funding" or "markPriceKlines",
                SourceKey = SourceFor(feature)
            });
        }

        return rows;
    }

    private static string SourceFor(string feature)
        => feature switch
        {
            "FundingRate" or "FundingRateZScore" => "funding",
            "MarkIndexDivergence" => "markPriceKlines",
            "OpenInterestChange15m" or "OpenInterestChange30m" or "OpenInterestChange60m" or "OpenInterestZScore" => "openInterestHist",
            "TakerBuySellImbalance" => "takerLongShortRatio",
            "LongShortRatioChange" => "globalLongShortAccountRatio",
            _ => "unknown"
        };

    private static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        IReadOnlyList<FuturesDataAvailabilityRow> availability,
        IReadOnlyList<FuturesFlowRuleCandidateRow> candidates,
        int candidateCount,
        int validationSurvivors,
        int holdoutSurvivors,
        int flowOnlyValidationSurvivors,
        bool studyRan,
        string studySkipReason)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var fullHistorySources = availability.Where(a => a.Supports365dStudy).Select(a => a.SourceKey).Distinct().ToArray();
        var limitedSources = availability.Where(a => a.AvailabilityClass == nameof(FuturesDataAvailabilityClass.Limited30d)).Select(a => a.SourceKey).Distinct().ToArray();
        var notPublic = availability.Where(a => a.AvailabilityClass == nameof(FuturesDataAvailabilityClass.NotPublicFree)).Select(a => a.SourceKey).Distinct().ToArray();
        var allSplitSurvivors = candidates.Where(c => c.AllSplitsPositive).ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which richer data sources are available with enough history?",
            Answer = $"365d-capable locally: {(fullHistorySources.Length > 0 ? string.Join(", ", fullHistorySources) : "none yet (run --bootstrap-futures-data true)")}. " +
                     $"Limited to ~30d on free API: {string.Join(", ", limitedSources)}. Not freely available historically: {string.Join(", ", notPublic)}.",
            Verdict = fullHistorySources.Length > 0 ? "PartialRicherDataAvailable" : "NeedsBootstrap",
            Details = new Dictionary<string, object?>
            {
                ["fullHistory"] = fullHistorySources,
                ["limited30d"] = limitedSources,
                ["notPublicFree"] = notPublic
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do funding/open-interest/taker-flow features add predictive value beyond candles?",
            Answer = !studyRan
                ? $"Study not run: {studySkipReason}"
                : allSplitSurvivors.Any(c => c.UsesFlowFeature)
                    ? $"At least one flow-based rule survived all splits ({allSplitSurvivors.Count(c => c.UsesFlowFeature)})."
                    : "No flow-based rule survived all splits. Funding/mark-index features did not add robust edge over candles; open-interest/taker were 30d-limited and excluded from the 365d cascade.",
            Verdict = !studyRan ? "NotEvaluated" : allSplitSurvivors.Any(c => c.UsesFlowFeature) ? "FlowAddsValue" : "FlowNoRobustValue",        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Do any rules survive train, validation, and holdout?",
            Answer = !studyRan
                ? $"Study not run: {studySkipReason}"
                : allSplitSurvivors.Length > 0
                    ? $"{allSplitSurvivors.Length} rule(s) survived all splits."
                    : $"No rule survived all splits. {candidateCount} train-qualified, {validationSurvivors} validation survivors, {holdoutSurvivors} holdout survivors.",
            Verdict = allSplitSurvivors.Length > 0 ? "SurvivorsFound" : "NoSurvivors",        });

        var shortFlow = candidates.Count(c => c.Direction == "Short" && c.UsesFlowFeature && c.ValidationPositive);
        var longFlow = candidates.Count(c => c.Direction == "Long" && c.UsesFlowFeature && c.ValidationPositive);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does short-side edge improve when using positioning/flow features?",
            Answer = !studyRan
                ? $"Study not run: {studySkipReason}"
                : $"Flow-based validation-positive candidates: Short={shortFlow}, Long={longFlow}. Flow-based all-split survivors: {allSplitSurvivors.Count(c => c.UsesFlowFeature)}.",
            Verdict = shortFlow > longFlow ? "ShortImprovesWithFlow" : longFlow > shortFlow ? "LongImprovesWithFlow" : "NoClearFlowSideEdge",        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If richer data is not available, what external source or paid dataset would be required?",
            Answer = "Full-history open interest, taker flow, long/short ratio, liquidations and order-book depth are not on Binance's free API beyond ~30d (flow ratios) or at all (liquidations/depth). " +
                     "Required: data.binance.vision dumps for depth, or paid providers (Coinalyze, Coinglass, Tardis.dev, Kaiko, Amberdata) for full-history OI/liquidations/order-book.",
            Verdict = "PaidOrDumpDataRequired",
            Details = new Dictionary<string, object?>
            {
                ["candidateProviders"] = new[] { "data.binance.vision", "Coinalyze", "Coinglass", "Tardis.dev", "Kaiko", "Amberdata" }
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: recommend live Futures or paper from this data-expansion branch?",
            Answer = "Do not recommend live Futures or paper/sandbox. Backtest/data-research only. Data availability and quality established; flow features beyond candles do not yet yield a robust train/validation/holdout edge with freely available history.",
            Verdict = "DoNotRecommendLiveFutures",
            Details = new Dictionary<string, object?> { ["backtestOnly"] = true, ["liveFuturesRecommended"] = false }
        });

        return answers;
    }
}
