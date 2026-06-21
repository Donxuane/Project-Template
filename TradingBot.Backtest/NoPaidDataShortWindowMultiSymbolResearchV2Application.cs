using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// NoPaidDataShortWindowMultiSymbolResearchV2 — research/backtest only, free data only.
/// Searches short-window opportunities on BTC/ETH/BNB/SOL across 6 interpretable rule families
/// with honest discovery/validation/holdout splits and walk-forward activation.
/// The frozen V1 BNB incubation candidate, its state file, and its history are never read,
/// modified, or regenerated here. No live trading, no real orders, no production changes.
/// </summary>
public sealed class NoPaidDataShortWindowMultiSymbolResearchV2Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed record TradeStats(
        int Count, decimal Net, decimal WinRate, decimal ProfitFactor, decimal MaxDrawdown, int MaxConsecutiveLosses);

    private sealed record SegmentBounds(DateTime DiscoveryEnd, DateTime ValidationEnd);

    public async Task<NoPaidDataShortWindowMultiSymbolResearchV2RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var loader = new HistoricalKlineDataLoader(settings);
        var downloadOutcomes = new List<ShortWindowDownloadOutcome>();

        if (settings.BootstrapFuturesData)
        {
            await RefreshCandlesAsync(loader, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            downloadOutcomes.AddRange(await flowDownloader.DownloadAllAsync(
                settings.DataDirectory, FuturesMarketDataCatalog.Symbols, runAtUtc.AddDays(-365), runAtUtc, cancellationToken));
        }

        // Load candles for every core symbol.
        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols)
        {
            var symbolData = await loader.LoadAndValidateAsync(symbol, cancellationToken);
            if (symbolData.Candles.Count == 0)
                throw new InvalidOperationException($"{symbol} local 1m data required for multi-symbol research.");
            validated[symbol] = symbolData;
        }

        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var windowed1m = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols.ToDictionary(
            s => s,
            s => CandleWindowSlicer.Slice(validated[s].Candles, windowStart, dataEndUtc));

        var btcContext = new BtcContextIndex(windowed1m[TradingSymbol.BTCUSDT]);
        var marketWideContext = new MarketWideContextIndex(
            windowed1m.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<KlineCandle>)kv.Value),
            includeBtcInProxy: true);

        var intervalCandles = new Dictionary<(TradingSymbol, string), IReadOnlyList<KlineCandle>>();
        foreach (var symbol in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols)
        foreach (var interval in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.Intervals)
        {
            intervalCandles[(symbol, interval)] = CandleAggregator
                .Aggregate(symbol, windowed1m[symbol], "1m", interval).Candles;
        }

        // One flow index per symbol (5m candles give the most granular candle-derived snapshot features).
        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var flowIndexes = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols.ToDictionary(
            s => s,
            s => new ShortWindowFlowFeatureIndex(
                futuresLoader, s, intervalCandles[(s, "5m")], windowed1m[TradingSymbol.BTCUSDT]));

        // Global study window: the latest flow-coverage start across symbols keeps splits aligned.
        var flowStarts = flowIndexes.Values
            .Select(f => f.FlowCoverageStartUtc)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToArray();
        var studyEnd = dataEndUtc;
        var studyStart = flowStarts.Length > 0 ? flowStarts.Max() : studyEnd.AddDays(-30);
        if (studyStart < windowStart)
            studyStart = windowStart;
        if (studyStart >= studyEnd)
            studyStart = studyEnd.AddDays(-1);
        var studySpanDays = (decimal)(studyEnd - studyStart).TotalDays;

        // Part A — coverage and eligibility.
        var coverage = BuildCoverage(futuresLoader, validated, studyStart, studyEnd);

        // Part B — base rule family scans (one pass per symbol/interval).
        var scans = new List<MultiSymbolComboScanResult>();
        foreach (var symbol in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols)
        foreach (var interval in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.Intervals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scans.AddRange(NoPaidDataShortWindowMultiSymbolResearchV2Simulator.ScanSymbolInterval(
                symbol, interval, intervalCandles[(symbol, interval)], windowed1m[symbol],
                flowIndexes[symbol], btcContext, marketWideContext, studyStart, studyEnd, cancellationToken));
        }

        // Cost-mapped trades (futures-moderate) per combo, used by splits and activation.
        var moderateByCombo = scans.ToDictionary(
            s => s.Key,
            s => (IReadOnlyList<RegimeDriftDiagnosticTrade>)NoPaidDataShortWindowFlowResearchV1Aggregator
                .MapCostScenario(s.BaseTrades, NoPaidDataShortWindowMultiSymbolResearchV2Catalog.PrimaryCostScenario, btcContext, dataEndUtc));

        // Part C — split validation.
        var primaryBounds = ResolveSegmentBounds(studyStart, studyEnd, 0.40m, 0.20m);
        var splitRows = new List<MultiSymbolSplitValidationRow>();
        var selectedByScheme = new Dictionary<string, HashSet<MultiSymbolComboKey>>();
        foreach (var (scheme, discoveryFraction, validationFraction) in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.SplitSchemes())
        {
            var bounds = ResolveSegmentBounds(studyStart, studyEnd, discoveryFraction, validationFraction);
            var selected = new HashSet<MultiSymbolComboKey>();
            foreach (var scan in scans)
            {
                var trades = moderateByCombo[scan.Key];
                var discovery = Stats(SegmentTrades(trades, studyStart, bounds.DiscoveryEnd));
                var validation = Stats(SegmentTrades(trades, bounds.DiscoveryEnd, bounds.ValidationEnd));
                var holdout = Stats(SegmentTrades(trades, bounds.ValidationEnd, studyEnd));
                var isSelected = discovery.Count >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.DiscoveryMinTrades
                                 && discovery.Net > 0m
                                 && discovery.ProfitFactor >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.DiscoveryMinProfitFactor;
                if (isSelected)
                    selected.Add(scan.Key);

                splitRows.Add(SplitRow(scheme, "Discovery", studyStart, bounds.DiscoveryEnd, scan.Key, discovery, isSelected,
                    "Selection uses discovery data only (trades>=8, net>0, PF>=1.2; fixed up-front)."));
                splitRows.Add(SplitRow(scheme, "Validation", bounds.DiscoveryEnd, bounds.ValidationEnd, scan.Key, validation, isSelected, string.Empty));
                splitRows.Add(SplitRow(scheme, "Holdout", bounds.ValidationEnd, studyEnd, scan.Key, holdout, isSelected, string.Empty));
            }

            selectedByScheme[scheme] = selected;
        }

        var rollingWeeklySkipped = studySpanDays < NoPaidDataShortWindowMultiSymbolResearchV2Catalog.RollingWeeklyMinSpanDays;
        if (!rollingWeeklySkipped)
            splitRows.AddRange(BuildRollingWeeklyRows(scans, moderateByCombo, studyStart, studyEnd));

        // Base rule summary (full study window + primary-scheme segments, futures-moderate).
        var baseSummary = scans.Select(scan =>
        {
            var trades = moderateByCombo[scan.Key];
            var full = Stats(trades);
            var discovery = Stats(SegmentTrades(trades, studyStart, primaryBounds.DiscoveryEnd));
            var validation = Stats(SegmentTrades(trades, primaryBounds.DiscoveryEnd, primaryBounds.ValidationEnd));
            var holdout = Stats(SegmentTrades(trades, primaryBounds.ValidationEnd, studyEnd));
            var geometry = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.GeometryFor(scan.Key.Interval);
            return new MultiSymbolBaseRuleSummaryRow
            {
                Symbol = scan.Key.Symbol.ToString(),
                Interval = scan.Key.Interval,
                Direction = scan.Key.Direction.ToString(),
                RuleFamily = scan.Key.Family.ToString(),
                TargetPercent = geometry.TargetPercent,
                StopPercent = geometry.StopPercent,
                MaxHoldMinutes = geometry.MaxHoldMinutes,
                CooldownCandles = geometry.CooldownCandles,
                SignalCount = scan.SignalCount,
                TradeCount = full.Count,
                NetPnlQuote = full.Net,
                WinRate = full.WinRate,
                ProfitFactor = full.ProfitFactor,
                MaxDrawdownQuote = full.MaxDrawdown,
                MaxConsecutiveLosses = full.MaxConsecutiveLosses,
                DiscoveryNet = discovery.Net,
                ValidationNet = validation.Net,
                HoldoutNet = holdout.Net,
                DiscoveryTrades = discovery.Count,
                ValidationTrades = validation.Count,
                HoldoutTrades = holdout.Count,
                CostScenario = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.PrimaryCostScenario
            };
        }).OrderByDescending(r => r.ValidationNet + r.HoldoutNet).ToArray();

        // Candidates: selected in the primary scheme's discovery segment only, capped.
        var candidates = selectedByScheme[NoPaidDataShortWindowMultiSymbolResearchV2Catalog.PrimarySplitScheme]
            .OrderByDescending(k => Stats(SegmentTrades(moderateByCombo[k], studyStart, primaryBounds.DiscoveryEnd)).Net)
            .Take(NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MaxActivationCandidates)
            .ToArray();

        // Part D — walk-forward activation; activation rule chosen on discovery segment only.
        var activationConfigs = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.BuildActivationConfigs();
        var leaderboard = new List<MultiSymbolLeaderboardRow>();
        var candidateTrades = new List<MultiSymbolCandidateTradeRow>();
        var activationPeriods = new List<MultiSymbolActivationPeriodRow>();
        var costRows = new List<MultiSymbolCostSensitivityRow>();

        foreach (var key in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trades = moderateByCombo[key];
            var flowIndex = flowIndexes[key.Symbol];

            MultiSymbolActivationSimResult? best = null;
            decimal bestDiscoveryNet = decimal.MinValue;
            MultiSymbolActivationSimResult? alwaysOn = null;
            foreach (var config in activationConfigs)
            {
                var sim = NoPaidDataShortWindowMultiSymbolResearchV2Engine.Simulate(
                    key, config, trades, studyStart, studyEnd, flowIndex,
                    NoPaidDataShortWindowMultiSymbolResearchV2Catalog.PrimaryCostScenario);
                var discoveryNet = sim.TakenTrades
                    .Where(t => t.EntryTimeUtc < primaryBounds.DiscoveryEnd)
                    .Sum(t => t.NetPnlQuote);
                if (config.IsAlwaysOn)
                {
                    alwaysOn = sim;
                    continue;
                }

                if (discoveryNet > bestDiscoveryNet)
                {
                    bestDiscoveryNet = discoveryNet;
                    best = sim;
                }
            }

            var alwaysOnDiscoveryNet = alwaysOn!.TakenTrades
                .Where(t => t.EntryTimeUtc < primaryBounds.DiscoveryEnd)
                .Sum(t => t.NetPnlQuote);
            // Only prefer an activation overlay when it beats always-on on discovery data.
            var chosen = best is not null && bestDiscoveryNet > alwaysOnDiscoveryNet ? best : alwaysOn;

            activationPeriods.AddRange(chosen.Periods);

            // Part E — cost scenarios for the chosen activation rule.
            var scan = scans.First(s => s.Key == key);
            var perScenarioValHoldNet = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var scenario in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CostScenarios)
            {
                var scenarioTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                    scan.BaseTrades, scenario, btcContext, dataEndUtc);
                var scenarioSim = NoPaidDataShortWindowMultiSymbolResearchV2Engine.Simulate(
                    key, chosen.Config, scenarioTrades, studyStart, studyEnd, flowIndex, scenario);
                var fullTaken = scenarioSim.TakenTrades;
                var valHold = fullTaken.Where(t => t.EntryTimeUtc >= primaryBounds.DiscoveryEnd).ToArray();
                var valHoldNet = Math.Round(valHold.Sum(t => t.NetPnlQuote), 8);
                perScenarioValHoldNet[scenario] = valHoldNet;
                costRows.Add(new MultiSymbolCostSensitivityRow
                {
                    Symbol = key.Symbol.ToString(),
                    Interval = key.Interval,
                    Direction = key.Direction.ToString(),
                    RuleFamily = key.Family.ToString(),
                    ActivationRule = chosen.Config.ActivationRuleName,
                    CostScenario = scenario,
                    FullWindowTrades = fullTaken.Count,
                    FullWindowNet = Math.Round(fullTaken.Sum(t => t.NetPnlQuote), 8),
                    ValidationHoldoutTrades = valHold.Length,
                    ValidationHoldoutNet = valHoldNet,
                    ValidationHoldoutNetPositive = valHoldNet > 0m
                });
            }

            // Candidate trades (moderate) tagged by segment.
            foreach (var t in chosen.TakenTrades)
            {
                candidateTrades.Add(new MultiSymbolCandidateTradeRow
                {
                    Symbol = key.Symbol.ToString(),
                    Interval = key.Interval,
                    Direction = key.Direction.ToString(),
                    RuleFamily = key.Family.ToString(),
                    ActivationRule = chosen.Config.ActivationRuleName,
                    EntryTimeUtc = t.EntryTimeUtc,
                    ExitTimeUtc = t.ExitTimeUtc,
                    NetPnlQuote = Math.Round(t.NetPnlQuote, 8),
                    IsWinner = t.NetPnlQuote > 0m,
                    ExitReason = t.ExitReason,
                    Segment = SegmentLabel(t.EntryTimeUtc, primaryBounds),
                    CostScenario = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.PrimaryCostScenario
                });
            }

            leaderboard.Add(BuildLeaderboardRow(
                key, chosen, perScenarioValHoldNet, primaryBounds, studyStart, studyEnd));
        }

        var orderedLeaderboard = leaderboard
            .OrderByDescending(r => r.Recommendation == "FreezeForForwardIncubation")
            .ThenByDescending(r => r.ValidationNet + r.HoldoutNet)
            .ToArray();

        var watchlist = BuildWatchlist(orderedLeaderboard, costRows);

        var answers = BuildAnswers(coverage, baseSummary, orderedLeaderboard, watchlist, candidates.Length,
            studyStart, studyEnd, studySpanDays, rollingWeeklySkipped);

        var result = new NoPaidDataShortWindowMultiSymbolResearchV2RunResult(
            coverage, baseSummary, splitRows, candidateTrades, activationPeriods, costRows,
            orderedLeaderboard, watchlist, answers, studyStart, studyEnd);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new NoPaidDataShortWindowMultiSymbolResearchV2ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(result, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "no-paid-short-window-multisymbol-v2",
                settings.DataDirectory,
                settings.OutputDirectory,
                runAtUtc,
                studyStartUtc = studyStart,
                studyEndUtc = studyEnd,
                studySpanDays = Math.Round(studySpanDays, 2),
                symbols = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols.Select(s => s.ToString()),
                optionalSymbolsConsidered = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.OptionalSymbolNames,
                intervals = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.Intervals,
                comboCount = scans.Count,
                discoverySelectedCandidates = candidates.Length,
                activationConfigCount = activationConfigs.Count,
                primarySplitScheme = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.PrimarySplitScheme,
                rollingWeeklySkipped,
                bootstrapAttempted = settings.BootstrapFuturesData,
                downloadOutcomes = downloadOutcomes.Select(o => new { o.Symbol, o.SourceKey, o.Success, o.AddedCount, o.TotalCount, o.Message }),
                frozenIncubationTrackTouched = false,
                frozenProfileProtected = "Frozen_BNB_Rule01Short_FundingNormal_Daily24h_V1 (not read, not modified, not regenerated)",
                backtestOnly = true,
                liveFuturesRecommended = false,
                paidDataUsed = false,
                realOrdersPlaced = false
            }, JsonOptions),
            cancellationToken);

        return result;
    }

    private static SegmentBounds ResolveSegmentBounds(
        DateTime studyStart, DateTime studyEnd, decimal discoveryFraction, decimal validationFraction)
    {
        var span = studyEnd - studyStart;
        var discoveryEnd = studyStart + TimeSpan.FromTicks((long)(span.Ticks * (double)discoveryFraction));
        var validationEnd = studyStart + TimeSpan.FromTicks((long)(span.Ticks * (double)(discoveryFraction + validationFraction)));
        return new SegmentBounds(discoveryEnd, validationEnd);
    }

    private static IReadOnlyList<RegimeDriftDiagnosticTrade> SegmentTrades(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades, DateTime startUtc, DateTime endUtc)
        => trades.Where(t => t.EntryTimeUtc >= startUtc && t.EntryTimeUtc < endUtc).ToArray();

    private static string SegmentLabel(DateTime entryUtc, SegmentBounds bounds)
        => entryUtc < bounds.DiscoveryEnd ? "Discovery"
            : entryUtc < bounds.ValidationEnd ? "Validation" : "Holdout";

    private static TradeStats Stats(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        if (trades.Count == 0)
            return new TradeStats(0, 0m, 0m, 0m, 0m, 0);

        var ordered = trades.OrderBy(t => t.ExitTimeUtc).ToArray();
        var net = ordered.Sum(t => t.NetPnlQuote);
        var wins = ordered.Count(t => t.NetPnlQuote > 0m);
        var grossWin = ordered.Where(t => t.NetPnlQuote > 0m).Sum(t => t.NetPnlQuote);
        var grossLoss = Math.Abs(ordered.Where(t => t.NetPnlQuote <= 0m).Sum(t => t.NetPnlQuote));
        var pf = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : Math.Round(grossWin / grossLoss, 6);

        decimal equity = 0m, peak = 0m, maxDd = 0m;
        int consec = 0, maxConsec = 0;
        foreach (var t in ordered)
        {
            equity += t.NetPnlQuote;
            if (equity > peak) peak = equity;
            var dd = peak - equity;
            if (dd > maxDd) maxDd = dd;
            if (t.NetPnlQuote <= 0m)
            {
                consec++;
                if (consec > maxConsec) maxConsec = consec;
            }
            else
            {
                consec = 0;
            }
        }

        return new TradeStats(
            ordered.Length,
            Math.Round(net, 8),
            Math.Round((decimal)wins / ordered.Length, 6),
            pf,
            Math.Round(maxDd, 8),
            maxConsec);
    }

    private static MultiSymbolSplitValidationRow SplitRow(
        string scheme, string segment, DateTime startUtc, DateTime endUtc,
        MultiSymbolComboKey key, TradeStats stats, bool selected, string notes)
        => new()
        {
            SplitScheme = scheme,
            Segment = segment,
            SegmentStartUtc = startUtc,
            SegmentEndUtc = endUtc,
            Symbol = key.Symbol.ToString(),
            Interval = key.Interval,
            Direction = key.Direction.ToString(),
            RuleFamily = key.Family.ToString(),
            TradeCount = stats.Count,
            NetPnlQuote = stats.Net,
            WinRate = stats.WinRate,
            ProfitFactor = stats.ProfitFactor,
            SelectedInDiscovery = selected,
            Notes = notes
        };

    private static IReadOnlyList<MultiSymbolSplitValidationRow> BuildRollingWeeklyRows(
        IReadOnlyList<MultiSymbolComboScanResult> scans,
        IReadOnlyDictionary<MultiSymbolComboKey, IReadOnlyList<RegimeDriftDiagnosticTrade>> moderateByCombo,
        DateTime studyStart,
        DateTime studyEnd)
    {
        var rows = new List<MultiSymbolSplitValidationRow>();
        var windowIndex = 0;
        for (var w0 = studyStart; w0.AddDays(28) <= studyEnd; w0 = w0.AddDays(7), windowIndex++)
        {
            var scheme = $"RollingWeekly_W{windowIndex}";
            var discoveryEnd = w0.AddDays(14);
            var validationEnd = w0.AddDays(21);
            var holdoutEnd = w0.AddDays(28);
            foreach (var scan in scans)
            {
                var trades = moderateByCombo[scan.Key];
                var discovery = Stats(SegmentTrades(trades, w0, discoveryEnd));
                var validation = Stats(SegmentTrades(trades, discoveryEnd, validationEnd));
                var holdout = Stats(SegmentTrades(trades, validationEnd, holdoutEnd));
                var selected = discovery.Count >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.DiscoveryMinTrades
                               && discovery.Net > 0m
                               && discovery.ProfitFactor >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.DiscoveryMinProfitFactor;
                rows.Add(SplitRow(scheme, "Discovery", w0, discoveryEnd, scan.Key, discovery, selected, string.Empty));
                rows.Add(SplitRow(scheme, "Validation", discoveryEnd, validationEnd, scan.Key, validation, selected, string.Empty));
                rows.Add(SplitRow(scheme, "Holdout", validationEnd, holdoutEnd, scan.Key, holdout, selected, string.Empty));
            }
        }

        return rows;
    }

    private MultiSymbolLeaderboardRow BuildLeaderboardRow(
        MultiSymbolComboKey key,
        MultiSymbolActivationSimResult chosen,
        IReadOnlyDictionary<string, decimal> perScenarioValHoldNet,
        SegmentBounds bounds,
        DateTime studyStart,
        DateTime studyEnd)
    {
        var taken = chosen.TakenTrades;
        var full = Stats(taken);
        var discovery = Stats(SegmentTrades(taken, studyStart, bounds.DiscoveryEnd));
        var validation = Stats(SegmentTrades(taken, bounds.DiscoveryEnd, bounds.ValidationEnd));
        var holdout = Stats(SegmentTrades(taken, bounds.ValidationEnd, studyEnd));
        var valHoldTrades = SegmentTrades(taken, bounds.DiscoveryEnd, studyEnd);
        var valHold = Stats(valHoldTrades);

        var latencyNet = perScenarioValHoldNet.GetValueOrDefault(
            NoPaidDataShortWindowMultiSymbolResearchV2Catalog.ModerateLatencyScenario);
        var stressPlusNet = perScenarioValHoldNet.GetValueOrDefault(
            NoPaidDataShortWindowMultiSymbolResearchV2Catalog.StressPlusScenario);
        var bestCostNet = perScenarioValHoldNet.Count > 0 ? perScenarioValHoldNet.Values.Max() : 0m;

        var positivePeriodRate = chosen.ActivatedPeriodCount > 0
            ? Math.Round((decimal)chosen.PositivePeriodCount / chosen.ActivatedPeriodCount, 6)
            : 0m;

        // Cluster + day concentration judged on the validation+holdout portion.
        var valHoldClusterCount = chosen.ActiveRanges
            .Count(r => valHoldTrades.Any(t => t.EntryTimeUtc >= r.Start && t.EntryTimeUtc < r.End));
        var dailyNets = valHoldTrades.GroupBy(t => t.ExitTimeUtc.Date).Select(g => g.Sum(t => t.NetPnlQuote)).ToArray();
        var maxDayShareOk = valHold.Net > 0m && dailyNets.Length > 0
                            && dailyNets.Max() / valHold.Net <= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MaxSingleDayProfitShare;

        var criteria = new[]
        {
            validation.Net > 0m,
            holdout.Net > 0m,
            valHold.Count >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinValidationHoldoutTrades,
            latencyNet > 0m,
            stressPlusNet >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.StressPlusCollapseFloorQuote,
            maxDayShareOk,
            positivePeriodRate >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinPositivePeriodRate,
            valHold.MaxConsecutiveLosses <= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MaxConsecutiveLossesLimit,
            valHoldClusterCount >= 2
        };
        var allPass = criteria.All(c => c);

        var overfitWarning = discovery.Net > 0m && (validation.Net <= 0m || holdout.Net <= 0m);
        var sparseWarning = valHold.Count < NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinValidationHoldoutTrades
                            || (!chosen.Config.IsAlwaysOn && chosen.ActivatedPeriodCount < NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinActivatedPeriodsForConfidence);
        var singleClusterWarning = valHoldClusterCount <= 1;

        var recommendation = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.ResolveRecommendation(
            allPass, validation.Net, holdout.Net, valHold.Count);

        return new MultiSymbolLeaderboardRow
        {
            Symbol = key.Symbol.ToString(),
            Interval = key.Interval,
            Direction = key.Direction.ToString(),
            RuleFamily = key.Family.ToString(),
            ActivationRule = chosen.Config.ActivationRuleName,
            DiscoveryNet = discovery.Net,
            ValidationNet = validation.Net,
            HoldoutNet = holdout.Net,
            FullWindowNet = full.Net,
            TradeCount = full.Count,
            ValidationTradeCount = validation.Count,
            HoldoutTradeCount = holdout.Count,
            WinRate = full.WinRate,
            ProfitFactor = full.ProfitFactor,
            MaxDrawdown = valHold.MaxDrawdown,
            MaxConsecutiveLosses = valHold.MaxConsecutiveLosses,
            PositiveActivatedPeriodsPercent = Math.Round(positivePeriodRate * 100m, 2),
            BestCostScenarioNet = bestCostNet,
            ModerateLatencyNet = latencyNet,
            StressPlusNet = stressPlusNet,
            OverfitWarning = overfitWarning,
            SparseWarning = sparseWarning,
            SingleClusterWarning = singleClusterWarning,
            Recommendation = recommendation,
            SuggestedFrozenProfileName = recommendation == "FreezeForForwardIncubation"
                ? NoPaidDataShortWindowMultiSymbolResearchV2Catalog.SuggestFrozenProfileName(key, chosen.Config.ActivationRuleName)
                : string.Empty,
            Notes = "Cost/risk fields (MaxDrawdown, MaxConsecutiveLosses, ModerateLatencyNet, StressPlusNet, BestCostScenarioNet) are computed on the validation+holdout segment only; WinRate/ProfitFactor/FullWindowNet cover the full study window. No live trading is recommended from this run."
        };
    }

    /// <summary>
    /// Watchlist: candidates positive across discovery/validation/holdout (typically failing only
    /// on sparse trade counts) plus explicitly tracked candidates. Derived purely from already
    /// computed leaderboard/cost results — no new search, no retuning, no threshold changes.
    /// </summary>
    private static IReadOnlyList<MultiSymbolWatchlistCandidateRow> BuildWatchlist(
        IReadOnlyList<MultiSymbolLeaderboardRow> leaderboard,
        IReadOnlyList<MultiSymbolCostSensitivityRow> costRows)
    {
        var rows = new List<MultiSymbolWatchlistCandidateRow>();
        foreach (var row in leaderboard)
        {
            var tracked = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.TrackedWatchlistCandidates.Any(t =>
                string.Equals(t.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Interval, row.Interval, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.Direction, row.Direction, StringComparison.OrdinalIgnoreCase)
                && string.Equals(t.RuleFamily, row.RuleFamily, StringComparison.OrdinalIgnoreCase));
            var allSegmentsPositive = row.DiscoveryNet > 0m && row.ValidationNet > 0m && row.HoldoutNet > 0m;
            if (!tracked && !allSegmentsPositive)
                continue;

            var costResults = costRows
                .Where(c => c.Symbol == row.Symbol && c.Interval == row.Interval
                            && c.Direction == row.Direction && c.RuleFamily == row.RuleFamily)
                .ToDictionary(c => c.CostScenario, c => c.ValidationHoldoutNet);
            var valHoldTrades = row.ValidationTradeCount + row.HoldoutTradeCount;
            var recommendation = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.ResolveWatchlistRecommendation(
                row.ValidationNet, row.HoldoutNet, valHoldTrades);

            rows.Add(new MultiSymbolWatchlistCandidateRow
            {
                Symbol = row.Symbol,
                Interval = row.Interval,
                Direction = row.Direction,
                RuleFamily = row.RuleFamily,
                ActivationRule = row.ActivationRule,
                DiscoveryNet = row.DiscoveryNet,
                ValidationNet = row.ValidationNet,
                HoldoutNet = row.HoldoutNet,
                ValidationHoldoutTradeCount = valHoldTrades,
                RequiredTradeCount = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinValidationHoldoutTrades,
                MissingTradeCount = Math.Max(0, NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinValidationHoldoutTrades - valHoldTrades),
                CostScenarioResults = costResults,
                OverfitWarning = row.OverfitWarning,
                SparseWarning = row.SparseWarning,
                SingleClusterWarning = row.SingleClusterWarning,
                Recommendation = recommendation,
                NextRerunCondition = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.NextRerunConditionText,
                ExplicitlyTracked = tracked,
                Notes = "Tracking only: thresholds unchanged, no new grid search, frozen BNB incubation untouched, no live trading recommendation."
            });
        }

        return rows
            .OrderByDescending(r => r.ExplicitlyTracked)
            .ThenByDescending(r => r.ValidationNet + r.HoldoutNet)
            .ToArray();
    }

    internal static IReadOnlyList<MultiSymbolDataCoverageRow> BuildCoverage(
        FuturesMarketDataLoader futuresLoader,
        IReadOnlyDictionary<TradingSymbol, SymbolValidationResult> validated,
        DateTime studyStart,
        DateTime studyEnd)
    {
        var rows = new List<MultiSymbolDataCoverageRow>();

        foreach (var symbol in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols)
        {
            var candles = validated[symbol].Candles;
            var candleStart = candles.Count > 0 ? candles[0].OpenTimeUtc : (DateTime?)null;
            var candleEnd = candles.Count > 0 ? candles[^1].OpenTimeUtc : (DateTime?)null;
            var candleSpan = candleStart.HasValue && candleEnd.HasValue
                ? Math.Round((decimal)(candleEnd.Value - candleStart.Value).TotalDays, 2)
                : 0m;

            var oi = SourceSpanDays(futuresLoader, symbol, "openInterestHist5m");
            var taker = SourceSpanDays(futuresLoader, symbol, "takerLongShortRatio5m");
            var global = SourceSpanDays(futuresLoader, symbol, "globalLongShortAccountRatio5m");
            var top = SourceSpanDays(futuresLoader, symbol, "topLongShortPositionRatio5m");
            var funding = SourceSpanDays(futuresLoader, symbol, "funding");
            var mark = SourceSpanDays(futuresLoader, symbol, "markPriceKlines");
            var index = SourceSpanDays(futuresLoader, symbol, "indexPriceKlines");

            var flowSpans = new[] { oi, taker, global, top };
            var minFlowSpan = flowSpans.Min(s => s.SpanDays);
            var flowStart = flowSpans.Where(s => s.Start.HasValue).Select(s => s.Start!.Value).DefaultIfEmpty().Max();
            var flowEnd = flowSpans.Where(s => s.End.HasValue).Select(s => s.End!.Value).DefaultIfEmpty().Min();
            var usable = Math.Round((decimal)(studyEnd - studyStart).TotalDays, 2);

            var eligible = candleSpan >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinCandleSpanDaysForEligibility
                           && minFlowSpan >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MinFlowSpanDaysForEligibility;

            foreach (var interval in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.Intervals)
            {
                rows.Add(new MultiSymbolDataCoverageRow
                {
                    Symbol = symbol.ToString(),
                    Interval = interval,
                    CandleDataPresent = candles.Count > 0,
                    CandleStartUtc = candleStart,
                    CandleEndUtc = candleEnd,
                    CandleSpanDays = candleSpan,
                    OiCoverageDays = oi.SpanDays,
                    TakerCoverageDays = taker.SpanDays,
                    GlobalLongShortCoverageDays = global.SpanDays,
                    TopLongShortCoverageDays = top.SpanDays,
                    FundingSpanDays = funding.SpanDays,
                    MarkIndexSpanDays = Math.Min(mark.SpanDays, index.SpanDays),
                    FlowStartUtc = flowStart == default ? null : flowStart,
                    FlowEndUtc = flowEnd == default ? null : flowEnd,
                    UsableWindowDays = usable,
                    EligibleForShortWindowResearch = eligible,
                    Notes = eligible
                        ? "Local merged free-data cache; flow features limited to the merged span."
                        : "Insufficient candle or flow coverage for short-window research."
                });
            }
        }

        foreach (var optional in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.OptionalSymbolNames)
        {
            var inEnum = Enum.TryParse<TradingSymbol>(optional, out _);
            rows.Add(new MultiSymbolDataCoverageRow
            {
                Symbol = optional,
                Interval = "all",
                CandleDataPresent = false,
                EligibleForShortWindowResearch = false,
                Notes = inEnum
                    ? "Optional symbol: no local candle/flow data yet; excluded until a clean free-data cache is accumulated."
                    : "Optional symbol: not supported by the TradingSymbol enum; would require domain change before any research."
            });
        }

        return rows;
    }

    private static (decimal SpanDays, DateTime? Start, DateTime? End) SourceSpanDays(
        FuturesMarketDataLoader loader, TradingSymbol symbol, string sourceKey)
    {
        var raw = loader.LoadRaw(symbol, sourceKey);
        var timestamps = raw
            .Select(r => long.TryParse(r.TryGetValue("t", out var t) ? t : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0L)
            .Where(v => v > 0).OrderBy(v => v).ToArray();
        if (timestamps.Length == 0)
            return (0m, null, null);
        var start = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).UtcDateTime;
        var end = DateTimeOffset.FromUnixTimeMilliseconds(timestamps[^1]).UtcDateTime;
        return (Math.Round((decimal)(end - start).TotalDays, 2), start, end);
    }

    private static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        IReadOnlyList<MultiSymbolDataCoverageRow> coverage,
        IReadOnlyList<MultiSymbolBaseRuleSummaryRow> baseSummary,
        IReadOnlyList<MultiSymbolLeaderboardRow> leaderboard,
        IReadOnlyList<MultiSymbolWatchlistCandidateRow> watchlist,
        int candidateCount,
        DateTime studyStart,
        DateTime studyEnd,
        decimal studySpanDays,
        bool rollingWeeklySkipped)
    {
        var eligibleSymbols = coverage
            .Where(c => c.EligibleForShortWindowResearch)
            .Select(c => c.Symbol).Distinct().ToArray();
        var freezeRows = leaderboard.Where(r => r.Recommendation == "FreezeForForwardIncubation").ToArray();
        var watchlistRows = leaderboard.Where(r => r.Recommendation == "Watchlist").ToArray();
        var overfitCount = leaderboard.Count(r => r.OverfitWarning);
        var familiesWithPositiveValHold = baseSummary
            .Where(r => r.ValidationNet > 0m && r.HoldoutNet > 0m)
            .Select(r => r.RuleFamily).Distinct().ToArray();

        return
        [
            new ReachabilityResearchAnswer
            {
                Question = "Which symbols/intervals are eligible for short-window research on free data?",
                Answer = $"Eligible: {string.Join(", ", eligibleSymbols)} on 5m/15m/30m with a usable window of {studySpanDays:F1} days ({studyStart:yyyy-MM-dd} -> {studyEnd:yyyy-MM-dd}). Optional symbols (XRP/ADA/DOGE/AVAX/LINK/LTC) have no clean local data and were excluded from scanning.",
                Verdict = eligibleSymbols.Length > 0 ? "CoreSymbolsEligible" : "NoEligibleSymbols"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Did any rule family show positive validation AND holdout performance?",
                Answer = familiesWithPositiveValHold.Length > 0
                    ? $"Yes, at base-rule level (before activation): {string.Join(", ", familiesWithPositiveValHold)}. See multisymbol-base-rule-summary for per-symbol detail."
                    : "No family had positive validation and holdout segments simultaneously at base-rule level.",
                Verdict = familiesWithPositiveValHold.Length > 0 ? "SomeFamiliesGeneralize" : "NoFamilyGeneralizes"
            },
            new ReachabilityResearchAnswer
            {
                Question = "How many discovery-selected candidates survived validation/holdout judgment?",
                Answer = $"{candidateCount} combo(s) were selected from discovery data alone; {leaderboard.Count(r => r.ValidationNet > 0m && r.HoldoutNet > 0m)} kept positive validation and holdout nets; {overfitCount} carry an overfit warning (discovery positive, validation or holdout not).",
                Verdict = overfitCount == candidateCount && candidateCount > 0 ? "MostlyOverfit" : "MixedResults"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Did any candidate pass every success criterion?",
                Answer = freezeRows.Length > 0
                    ? $"Yes: {string.Join("; ", freezeRows.Select(r => $"{r.Symbol} {r.Interval} {r.Direction} {r.RuleFamily} + {r.ActivationRule} (suggested profile {r.SuggestedFrozenProfileName})"))}. These are proposals only — the existing BNB frozen candidate is not replaced."
                    : "No candidate passed all success criteria in this run.",
                Verdict = freezeRows.Length > 0 ? "FreezeProposalsExist" : "NoFreezeProposals"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Was the rolling weekly split scheme usable?",
                Answer = rollingWeeklySkipped
                    ? $"No — the usable window is {studySpanDays:F1} days, below the {NoPaidDataShortWindowMultiSymbolResearchV2Catalog.RollingWeeklyMinSpanDays:F0}-day minimum. Keep merging free data; rolling weekly splits activate automatically once the local cache is long enough."
                    : "Yes — rolling weekly windows were evaluated; see multisymbol-split-validation-summary rows with scheme RollingWeekly_W*.",
                Verdict = rollingWeeklySkipped ? "RollingSplitsSkipped" : "RollingSplitsRan"
            },
            new ReachabilityResearchAnswer
            {
                Question = "When should this be rerun?",
                Answer = "After the merged flow cache exceeds 35 days and rolling weekly splits activate, then again at 45 and 60 days of merged data if the watchlist candidates are still positive. "
                         + (watchlist.Count > 0
                             ? $"Watchlist now tracks {watchlist.Count} candidate(s): {string.Join("; ", watchlist.Select(w => $"{w.Symbol} {w.Interval} {w.Direction} {w.RuleFamily} + {w.ActivationRule} ({w.Recommendation}, missing {w.MissingTradeCount} of {w.RequiredTradeCount} validation+holdout trades)"))}."
                             : "The watchlist is currently empty."),
                Verdict = "RerunAt35Then45And60Days",
                Details = new Dictionary<string, object?>
                {
                    ["rerunThresholdDays"] = new[] { 35, 45, 60 },
                    ["watchlistCount"] = watchlist.Count
                }
            },
            new ReachabilityResearchAnswer
            {
                Question = "What should happen next?",
                Answer = freezeRows.Length > 0
                    ? "Freeze proposals exist but were validated on a single ~30d window with internal splits only. Recommended path: keep merging free data daily, re-run this branch after 1-2 more weeks, and only then consider creating a new forward-incubation track (separate from the untouched BNB candidate)."
                    : watchlistRows.Length > 0
                        ? $"No freeze proposals; {watchlistRows.Length} watchlist candidate(s) deserve a re-run after more free data accumulates. Keep the BNB incubation track running unchanged."
                        : "Nothing actionable yet: keep merging free flow data, let the window grow past 35 days so rolling splits activate, and re-run. The BNB incubation track continues unchanged.",
                Verdict = freezeRows.Length > 0 ? "ReRunThenConsiderNewIncubationTrack"
                    : watchlistRows.Length > 0 ? "KeepCollectingReRun" : "KeepCollecting",
                Details = new Dictionary<string, object?>
                {
                    ["backtestOnly"] = true,
                    ["liveFuturesRecommended"] = false,
                    ["paidDataUsed"] = false,
                    ["frozenBnbCandidateTouched"] = false
                }
            }
        ];
    }

    private async Task RefreshCandlesAsync(HistoricalKlineDataLoader loader, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var symbol in NoPaidDataShortWindowMultiSymbolResearchV2Catalog.CoreSymbols)
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
}
