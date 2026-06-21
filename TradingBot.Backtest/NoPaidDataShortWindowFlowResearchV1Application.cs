using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// NoPaidDataShortWindowFlowResearchV1 — backtest/data-research only.
/// Free data only (Binance public REST + optional free Coinalyze key via env var).
/// No production/live changes, no live Futures, no order placement, no API keys in source control.
/// </summary>
public sealed class NoPaidDataShortWindowFlowResearchV1Application(BacktestSettings settings)
{
    private const int MaxCostCheckedRules = 60;
    private const int MaxReportedRules = 30;

    public async Task<NoPaidDataShortWindowFlowResearchV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var downloadOutcomes = new List<ShortWindowDownloadOutcome>();
        var loader = new HistoricalKlineDataLoader(settings);

        // Optional free-data refresh (public endpoints only, merge-on-disk so history accumulates).
        if (settings.BootstrapFuturesData)
        {
            await RefreshCandlesAsync(loader, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            var nowUtc = DateTime.UtcNow;
            downloadOutcomes.AddRange(await flowDownloader.DownloadAllAsync(
                settings.DataDirectory, FuturesMarketDataCatalog.Symbols, nowUtc.AddDays(-365), nowUtc, cancellationToken));
        }

        var bnb = await loader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (bnb.Candles.Count == 0 || btc.Candles.Count == 0)
            throw new InvalidOperationException("BNBUSDT and BTCUSDT local data required for NoPaidDataShortWindowFlowResearchV1.");

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [TradingSymbol.BNBUSDT] = bnb,
            [TradingSymbol.BTCUSDT] = btc
        };
        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var windowBnb = CandleWindowSlicer.Slice(bnb.Candles, windowStart, dataEndUtc);
        var windowBtc = CandleWindowSlicer.Slice(btc.Candles, windowStart, dataEndUtc);
        var intervalCandles = CandleAggregator.Aggregate(TradingSymbol.BNBUSDT, windowBnb, "1m", "5m").Candles;
        if (intervalCandles.Count == 0)
            throw new InvalidOperationException("No interval candles for BNB 5m profile.");

        var btcContext = new BtcContextIndex(windowBtc);
        var marketWideContext = new MarketWideContextIndex(
            new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
            {
                [TradingSymbol.BNBUSDT] = windowBnb,
                [TradingSymbol.BTCUSDT] = windowBtc
            },
            includeBtcInProxy: true);

        // Part A — data availability (Binance free endpoints + Coinalyze free-tier probe).
        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var availability = new List<ShortWindowDataAvailabilityRow>();
        availability.AddRange(BuildBinanceAvailability(futuresLoader));
        var coinalyzeProbe = new CoinalyzeFeasibilityProbe();
        availability.AddRange(await coinalyzeProbe.ProbeAsync(cancellationToken));

        // Part B — short-window flow features for BNB (BTC context included per row).
        var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, TradingSymbol.BNBUSDT, intervalCandles, windowBtc);
        var flowStart = flowIndex.FlowCoverageStartUtc;
        var flowEnd = flowIndex.FlowCoverageEndUtc;

        var studyEnd = dataEndUtc;
        var studyStart = flowStart.HasValue && flowStart.Value > windowStart
            ? flowStart.Value
            : studyEnd.AddDays(-30);
        if (studyStart < windowStart)
            studyStart = windowStart;
        if (studyStart >= studyEnd)
            studyStart = studyEnd.AddDays(-1);

        var featureSamples = BuildFeatureSamples(flowIndex, studyStart, studyEnd);

        // Base Rule01 short BNB 5m trades over the full 365d window (one scan; activation never re-trades).
        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Rule01 not found.");
        var profile = NoPaidDataShortWindowFlowResearchV1Catalog.BuildProfile(rule);
        var v2Profile = DirectionalRuleFuturesValidationV31Catalog.ToV2Profile(profile);
        var scan = DirectionalRuleFuturesValidationV2Simulator.ScanProfile(
            v2Profile, "365d", intervalCandles, windowBnb, btcContext, marketWideContext, cancellationToken);

        var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.Trades, NoPaidDataShortWindowFlowResearchV1Catalog.PrimaryCostScenario, btcContext, dataEndUtc);

        // Part C — walk-forward activation simulations.
        var configs = NoPaidDataShortWindowFlowResearchV1Catalog.BuildActivationConfigs();
        var simByRule = new Dictionary<string, ShortWindowSimResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            simByRule[config.ActivationRuleName] = NoPaidDataShortWindowFlowResearchV1Engine.Simulate(
                config, moderateTrades, studyStart, studyEnd, flowIndex,
                NoPaidDataShortWindowFlowResearchV1Catalog.PrimaryCostScenario);
        }

        var baselineSim = simByRule["Baseline_AlwaysOn"];

        // Cost sensitivity for the baseline plus the strongest candidates.
        var costCheckCandidates = simByRule.Values
            .Where(s => s.Config.PerfCondition != ShortWindowPerfCondition.AlwaysOn)
            .Where(s => s.Summary.NetPositive && s.Summary.TotalTrades > 0)
            .OrderByDescending(s => s.Summary.MeetsMinExecutedTrades)
            .ThenByDescending(s => s.Summary.NetPnlQuote)
            .Take(MaxCostCheckedRules)
            .Select(s => s.Config)
            .ToList();
        costCheckCandidates.Insert(0, baselineSim.Config);

        var costSensitivity = new List<ShortWindowCostSensitivityRow>();
        foreach (var config in costCheckCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            costSensitivity.AddRange(NoPaidDataShortWindowFlowResearchV1Aggregator.BuildCostSensitivity(
                config, scan.Trades, btcContext, studyStart, studyEnd, dataEndUtc, flowIndex));
        }

        var latency002ByRule = costSensitivity
            .Where(c => string.Equals(c.CostScenario, NoPaidDataShortWindowFlowResearchV1Catalog.ModerateSlippageScenario, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(c => c.ActivationRuleName, c => c.NetPnlQuote, StringComparer.OrdinalIgnoreCase);

        var finalSummaries = simByRule.Values
            .Select(s => NoPaidDataShortWindowFlowResearchV1Aggregator.FinalizeSummary(
                s.Summary,
                latency002ByRule.TryGetValue(s.Config.ActivationRuleName, out var net002) ? net002 : null))
            .OrderByDescending(s => s.PassesSuccessCriteria)
            .ThenByDescending(s => s.NetPnlQuote)
            .ToArray();

        var baselineSummary = finalSummaries.First(s => s.PerfCondition == nameof(ShortWindowPerfCondition.AlwaysOn));

        // Trade and period detail reports: baseline + the strongest rules only (keeps files reviewable).
        var reportedRuleNames = finalSummaries
            .Where(s => s.PerfCondition != nameof(ShortWindowPerfCondition.AlwaysOn))
            .OrderByDescending(s => s.PassesSuccessCriteria)
            .ThenByDescending(s => s.NetPnlQuote)
            .Take(MaxReportedRules)
            .Select(s => s.ActivationRuleName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        reportedRuleNames.Add(baselineSim.Config.ActivationRuleName);

        var trades = new List<ShortWindowTradeRow>();
        var periods = new List<ShortWindowPeriodRow>();
        foreach (var name in reportedRuleNames)
        {
            if (!simByRule.TryGetValue(name, out var sim))
                continue;
            trades.AddRange(sim.Trades);
            periods.AddRange(sim.Periods);
        }

        var answers = NoPaidDataShortWindowFlowResearchV1Aggregator.BuildAnswers(
            availability, finalSummaries, simByRule, costSensitivity, baselineSummary,
            studyStart, studyEnd, flowStart);

        var result = new NoPaidDataShortWindowFlowResearchV1RunResult(
            availability, featureSamples, finalSummaries, trades, periods, costSensitivity, answers,
            baselineSummary.TotalTrades, baselineSummary.NetPnlQuote, studyStart, studyEnd, flowStart, flowEnd);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new NoPaidDataShortWindowFlowResearchV1ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(result, cancellationToken);

        var qualifying = finalSummaries.Count(s => s.PassesSuccessCriteria);
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "no-paid-short-window-flow-research-v1",
                settings.DataDirectory,
                settings.OutputDirectory,
                rule = rule.RuleName,
                profile = profile.VariantLabel,
                costScenarioPrimary = NoPaidDataShortWindowFlowResearchV1Catalog.PrimaryCostScenario,
                activationConfigCount = configs.Count,
                costCheckedRuleCount = costCheckCandidates.Count,
                qualifyingRuleCount = qualifying,
                baselineStudyWindowTrades = baselineSummary.TotalTrades,
                baselineStudyWindowNetPnl = baselineSummary.NetPnlQuote,
                studyStartUtc = studyStart,
                studyEndUtc = studyEnd,
                flowCoverageStartUtc = flowStart,
                flowCoverageEndUtc = flowEnd,
                fullWindowStartUtc = windowStart,
                dataEndUtc,
                bootstrapAttempted = settings.BootstrapFuturesData,
                downloadOutcomes = downloadOutcomes.Select(o => new { o.Symbol, o.SourceKey, o.Success, o.AddedCount, o.TotalCount, o.Message }),
                coinalyzeKeyConfigured = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CoinalyzeFeasibilityProbe.ApiKeyEnvVar)),
                backtestOnly = true,
                liveFuturesRecommended = false,
                paidDataUsed = false,
                realOrdersPlaced = false
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return result;
    }

    private async Task RefreshCandlesAsync(HistoricalKlineDataLoader loader, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var symbol in new[] { TradingSymbol.BNBUSDT, TradingSymbol.BTCUSDT })
        {
            var existing = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            var lastUtc = existing.Candles.Count > 0 ? existing.Candles[^1].OpenTimeUtc : DateTime.UtcNow.AddDays(-35);
            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - lastUtc) < TimeSpan.FromMinutes(30))
                continue;
            var path = Path.Combine(settings.DataDirectory, $"{symbol}-1m.json");
            await downloader.DownloadAndMergeToJsonAsync(
                symbol.ToString(), path, 1000, lastUtc.AddHours(-2), nowUtc, cancellationToken);
        }
    }

    private static IReadOnlyList<ShortWindowDataAvailabilityRow> BuildBinanceAvailability(FuturesMarketDataLoader futuresLoader)
    {
        const string limitedIntervals = "5m,15m,30m,1h,2h,4h,6h,12h,1d";
        const string rateLimits = "Public fapi REST, no API key. IP weight limit 2400/min; futures/data/* endpoints additionally throttled on bursts (HTTP 429).";
        const string allUsdtPerps = "All Binance USDT-M perpetuals (incl. BTC/ETH/BNB/SOL USDT).";

        var sources = new (string SourceKey, string DisplayName, string Endpoint, string Intervals, string RequestedInterval, string DocLookback, bool U7, bool U14, bool U30, bool U365, string Notes)[]
        {
            ("funding", "Funding Rate", "/fapi/v1/fundingRate", "8h funding events", "8h", "Full history (365d+).", true, true, true, true,
                "Full funding history available for free."),
            ("markPriceKlines", "Mark Price Klines", "/fapi/v1/markPriceKlines", "1m..1M kline intervals", "30m", "Full history (365d+).", true, true, true, true,
                "Used for mark/index divergence."),
            ("indexPriceKlines", "Index Price Klines", "/fapi/v1/indexPriceKlines", "1m..1M kline intervals", "30m", "Full history (365d+).", true, true, true, true,
                "Used for mark/index divergence."),
            ("openInterestHist", "Open Interest History (30m)", "/futures/data/openInterestHist", limitedIntervals, "30m", "~30d retained server-side.", true, true, true, false,
                "Free API exposes ~latest 30d only. Local file accumulates across runs (merge-on-download)."),
            ("openInterestHist5m", "Open Interest History (5m)", "/futures/data/openInterestHist", limitedIntervals, "5m", "~30d retained server-side.", true, true, true, false,
                "5m granularity for OIChange5m/15m features. Local file accumulates across runs."),
            ("takerLongShortRatio", "Taker Buy/Sell Volume (30m)", "/futures/data/takerlongshortRatio", limitedIntervals, "30m", "~30d retained server-side.", true, true, true, false,
                "Taker buy/sell volume ratio."),
            ("takerLongShortRatio5m", "Taker Buy/Sell Volume (5m)", "/futures/data/takerlongshortRatio", limitedIntervals, "5m", "~30d retained server-side.", true, true, true, false,
                "5m granularity for TakerBuySellImbalance."),
            ("globalLongShortAccountRatio", "Global Long/Short Account Ratio (30m)", "/futures/data/globalLongShortAccountRatio", limitedIntervals, "30m", "~30d retained server-side.", true, true, true, false,
                "Global account long/short ratio."),
            ("globalLongShortAccountRatio5m", "Global Long/Short Account Ratio (5m)", "/futures/data/globalLongShortAccountRatio", limitedIntervals, "5m", "~30d retained server-side.", true, true, true, false,
                "5m granularity for LongShortRatioChange."),
            ("topLongShortPositionRatio", "Top Trader Long/Short Position Ratio (30m)", "/futures/data/topLongShortPositionRatio", limitedIntervals, "30m", "~30d retained server-side.", true, true, true, false,
                "Top trader positions long/short ratio."),
            ("topLongShortPositionRatio5m", "Top Trader Long/Short Position Ratio (5m)", "/futures/data/topLongShortPositionRatio", limitedIntervals, "5m", "~30d retained server-side.", true, true, true, false,
                "5m granularity for top-trader stretch."),
            ("liquidations", "Liquidations", "(websocket forceOrder only)", "n/a", "n/a", "Not available on free REST.", false, false, false, false,
                "No free historical REST endpoint. Coinalyze free API or live capture required."),
            ("depthSnapshots", "Order Book Depth", "(no historical REST)", "n/a", "n/a", "Not available on free REST.", false, false, false, false,
                "Requires data.binance.vision dumps or paid feeds.")
        };

        var rows = new List<ShortWindowDataAvailabilityRow>();
        foreach (var symbol in FuturesMarketDataCatalog.Symbols)
        foreach (var s in sources)
        {
            var raw = s.SourceKey is "liquidations" or "depthSnapshots"
                ? []
                : futuresLoader.LoadRaw(symbol, s.SourceKey);
            var timestamps = raw
                .Select(r => long.TryParse(r.TryGetValue("t", out var t) ? t : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0L)
                .Where(v => v > 0).OrderBy(v => v).ToArray();
            var present = timestamps.Length > 0;
            DateTime? localStart = present ? DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).UtcDateTime : null;
            DateTime? localEnd = present ? DateTimeOffset.FromUnixTimeMilliseconds(timestamps[^1]).UtcDateTime : null;
            var localSpan = present ? Math.Round((decimal)(localEnd!.Value - localStart!.Value).TotalDays, 2) : 0m;

            rows.Add(new ShortWindowDataAvailabilityRow
            {
                Provider = "binance-futures-free",
                Symbol = symbol.ToString(),
                SourceKey = s.SourceKey,
                DisplayName = s.DisplayName,
                Endpoint = FuturesMarketDataCatalog.FuturesBaseUrl + s.Endpoint,
                IntervalOptions = s.Intervals,
                RequestedInterval = s.RequestedInterval,
                MaxLookbackDocumented = s.DocLookback,
                MaxLookbackDaysObserved = present ? localSpan : null,
                RateLimitNotes = rateLimits,
                SymbolsSupported = allUsdtPerps,
                LocalFilePresent = present,
                LocalRecordCount = timestamps.Length,
                LocalStartUtc = localStart,
                LocalEndUtc = localEnd,
                LocalSpanDays = localSpan,
                UsefulFor7d = s.U7,
                UsefulFor14d = s.U14,
                UsefulFor30d = s.U30,
                UsefulFor365d = s.U365,
                ProbeStatus = present ? "LocalDataPresent" : "NoLocalData",
                Notes = s.Notes
            });
        }

        return rows;
    }

    private static IReadOnlyList<ShortWindowFeatureSampleRow> BuildFeatureSamples(
        ShortWindowFlowFeatureIndex flowIndex, DateTime startUtc, DateTime endUtc)
    {
        var rows = new List<ShortWindowFeatureSampleRow>();
        for (var t = RoundUpToHour(startUtc); t <= endUtc; t = t.AddHours(1))
        {
            var s = flowIndex.Snapshot(t);
            rows.Add(new ShortWindowFeatureSampleRow
            {
                Symbol = TradingSymbol.BNBUSDT.ToString(),
                TimestampUtc = t,
                OiChange5mPercent = s.OiChange5mPercent,
                OiChange15mPercent = s.OiChange15mPercent,
                OiChange30mPercent = s.OiChange30mPercent,
                OiChange60mPercent = s.OiChange60mPercent,
                OiZScoreRecent = s.OiZScoreRecent,
                TakerBuySellImbalance = s.TakerBuySellImbalance,
                TakerImbalance1h = s.TakerImbalance1h,
                GlobalLongShortRatio = s.GlobalLongShortRatio,
                GlobalLongShortRatioChange1hPercent = s.GlobalLongShortRatioChange1hPercent,
                GlobalLongShortZScore = s.GlobalLongShortZScore,
                TopLongShortRatio = s.TopLongShortRatio,
                TopLongShortZScore = s.TopLongShortZScore,
                FundingRate = s.FundingRate,
                FundingZScore = s.FundingZScore,
                MarkIndexDivergencePercent = s.MarkIndexDivergencePercent,
                BtcReturn30mPercent = s.BtcReturn30mPercent,
                BtcReturn60mPercent = s.BtcReturn60mPercent,
                BtcTrendSlopePercentPerHour = s.BtcTrendSlopePercentPerHour,
                VolatilityRegime = s.VolatilityRegime,
                AtrPercent = s.AtrPercent,
                DistanceFromRecentHighPercent = s.DistanceFromRecentHighPercent,
                DistanceFromRecentLowPercent = s.DistanceFromRecentLowPercent
            });
        }

        return rows;
    }

    private static DateTime RoundUpToHour(DateTime value)
        => value.Minute == 0 && value.Second == 0 && value.Millisecond == 0
            ? value
            : new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc).AddHours(1);

    private string ResolveDiscoveryJsonPath()
    {
        if (!string.IsNullOrWhiteSpace(settings.DirectionalRuleDiscoveryJsonPath)
            && File.Exists(settings.DirectionalRuleDiscoveryJsonPath))
        {
            return settings.DirectionalRuleDiscoveryJsonPath;
        }

        var repoRoot = Directory.GetCurrentDirectory();
        return Path.Combine(
            repoRoot,
            "TradingBot.Backtest",
            "output",
            "long-short-futures-feasibility-v1-run",
            "long-short-entry-time-rule-discovery.json");
    }
}
