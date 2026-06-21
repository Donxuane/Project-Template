using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

internal static class CrossSymbolForwardIncubationRunner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed record TradeStats(
        int Count, decimal Net, decimal WinRate, decimal ProfitFactor, decimal MaxDrawdown, int MaxConsecutiveLosses);

    internal sealed record CatalogRefs(
        string ModeName,
        string TrackLabel,
        string FrozenProfileName,
        TradingSymbol Symbol,
        string Interval,
        CrossSymbolComboKey FrozenComboKey,
        Func<CrossSymbolActivationConfig> BuildFrozenActivationConfig,
        Func<DateTime, FrozenCandidateState> BuildDefaultState,
        Func<string, string> FrozenStatePath,
        Func<string, string> ForwardHistoryPath,
        string[] CostScenarios,
        string PrimaryCostScenario,
        string ModerateSlippageScenario,
        string StressPlusScenario,
        TradingSymbol[] CoverageSymbols,
        string[] CoverageSourceKeys,
        Func<decimal, int, int, decimal, decimal, bool, string> ResolveVerdict,
        int MinForwardTrades,
        decimal StressPlusCollapseFloorQuote,
        int MaxConsecutiveLossesLimit,
        decimal MaxSingleDayProfitShare,
        decimal MinPositivePeriodRate,
        decimal MaxDrawdownToNetRatio,
        decimal MinForwardSpanDaysForJudgment,
        string AnswersSymbolLabel,
        string AnswersBesideTracksNote);

    internal static async Task<CrossSymbolForwardIncubationRunResult> RunAsync(
        BacktestSettings settings,
        CatalogRefs catalog,
        bool includeBnb5mProtection,
        bool includeSol5mProtection,
        bool includeBnb15mProtection,
        bool includeSol15mProtection,
        CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var loader = new HistoricalKlineDataLoader(settings);
        var downloadOutcomes = new List<ShortWindowDownloadOutcome>();

        var protectedFiles = ForwardIncubationTrackProtection.ResolveProtectedFiles(
            settings.DataDirectory, settings.OutputDirectory,
            includeBnb5mProtection, includeSol5mProtection, includeBnb15mProtection, includeSol15mProtection);
        var protectedHashesBefore = ForwardIncubationTrackProtection.HashFiles(protectedFiles);

        if (settings.BootstrapFuturesData)
        {
            await RefreshCandlesAsync(loader, settings.DataDirectory, catalog.Symbol, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            downloadOutcomes.AddRange(await flowDownloader.DownloadAllAsync(
                settings.DataDirectory, FuturesMarketDataCatalog.Symbols, runAtUtc.AddDays(-365), runAtUtc, cancellationToken));
        }

        var state = await LoadOrCreateFrozenStateAsync(settings, catalog, runAtUtc, cancellationToken);
        var frozenConfig = catalog.BuildFrozenActivationConfig();
        var frozenKey = catalog.FrozenComboKey;

        var symbolData = await loader.LoadAndValidateAsync(catalog.Symbol, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (symbolData.Candles.Count == 0 || btc.Candles.Count == 0)
            throw new InvalidOperationException($"{catalog.Symbol} and BTCUSDT local data required for forward incubation.");

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>
        {
            [catalog.Symbol] = symbolData,
            [TradingSymbol.BTCUSDT] = btc
        };
        var (dataStartUtc, dataEndUtc) = RobustnessWindowResolver.ResolveDataBounds(validated);
        var spanDays = (int)Math.Max(1, (dataEndUtc - dataStartUtc).TotalDays);
        var windowStart = dataEndUtc.AddDays(-Math.Min(365, spanDays));
        if (windowStart < dataStartUtc)
            windowStart = dataStartUtc;

        var windowSymbol = CandleWindowSlicer.Slice(symbolData.Candles, windowStart, dataEndUtc);
        var windowBtc = CandleWindowSlicer.Slice(btc.Candles, windowStart, dataEndUtc);
        var intervalCandles = CandleAggregator.Aggregate(catalog.Symbol, windowSymbol, "1m", catalog.Interval).Candles;
        if (intervalCandles.Count == 0)
            throw new InvalidOperationException($"No interval candles for {catalog.Symbol} {catalog.Interval} profile.");

        var btcContext = new BtcContextIndex(windowBtc);
        var marketWideContext = new MarketWideContextIndex(
            new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>
            {
                [catalog.Symbol] = windowSymbol,
                [TradingSymbol.BTCUSDT] = windowBtc
            },
            includeBtcInProxy: true);

        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, catalog.Symbol, intervalCandles, windowBtc);
        var coverage = BuildDataCoverage(futuresLoader, catalog, state.FrozenStartUtc);

        var scans = NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator.ScanSymbolInterval(
            catalog.Symbol, catalog.Interval, intervalCandles, windowSymbol, flowIndex, btcContext,
            marketWideContext, windowStart, dataEndUtc, cancellationToken);
        var scan = scans.FirstOrDefault(s => s.Key == frozenKey)
                   ?? throw new InvalidOperationException($"Frozen combo {frozenKey} not produced by scan.");

        var frozenStart = state.FrozenStartUtc;
        var forwardEnd = dataEndUtc > frozenStart ? dataEndUtc : frozenStart;
        var forwardSpanDays = Math.Round((decimal)(forwardEnd - frozenStart).TotalDays, 4);

        var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.BaseTrades, catalog.PrimaryCostScenario, btcContext, dataEndUtc);
        var forwardSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
            frozenKey, frozenConfig, moderateTrades, frozenStart, forwardEnd, flowIndex,
            catalog.PrimaryCostScenario, collectPeriods: true);
        var forwardStats = Stats(forwardSim.TakenTrades);
        var checkpointCount = forwardSim.Periods.Count;

        var costRows = new List<CrossSymbolCostSensitivityRow>();
        var netByScenario = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var scenario in catalog.CostScenarios)
        {
            var scenarioTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                scan.BaseTrades, scenario, btcContext, dataEndUtc);
            var scenarioSim = NoPaidDataShortWindowFlowResearchV1CrossSymbolEngine.Simulate(
                frozenKey, frozenConfig, scenarioTrades, frozenStart, forwardEnd, flowIndex, scenario, collectPeriods: false);
            var scenarioStats = Stats(scenarioSim.TakenTrades);
            netByScenario[scenario] = scenarioStats.Net;
            costRows.Add(new CrossSymbolCostSensitivityRow
            {
                Symbol = frozenKey.Symbol.ToString(),
                Interval = frozenKey.Interval,
                Direction = frozenKey.Direction.ToString(),
                TargetPercent = frozenKey.TargetPercent,
                StopPercent = frozenKey.StopPercent,
                ActivationRule = frozenConfig.ActivationRuleName,
                CostScenario = scenario,
                TradeCount = scenarioStats.Count,
                NetPnlQuote = scenarioStats.Net,
                WinRate = scenarioStats.WinRate,
                ProfitFactor = scenarioStats.ProfitFactor,
                NetPositive = scenarioStats.Net > 0m
            });
        }

        var netModerate = forwardStats.Net;
        var netLatency002 = netByScenario.GetValueOrDefault(catalog.ModerateSlippageScenario);
        var netStressPlus = netByScenario.GetValueOrDefault(catalog.StressPlusScenario);
        var positivePeriodRate = forwardSim.ActivatedPeriodCount > 0
            ? Math.Round((decimal)forwardSim.PositivePeriodCount / forwardSim.ActivatedPeriodCount, 6)
            : 0m;

        var acceleratedValidation = ForwardIncubationAcceleratedValidationBuilder.Build(
            frozenKey,
            frozenConfig,
            scan.BaseTrades,
            intervalCandles,
            btcContext,
            flowIndex,
            frozenStart,
            forwardEnd,
            netModerate,
            catalog.PrimaryCostScenario,
            catalog.ModerateSlippageScenario,
            catalog.StressPlusScenario);

        var healthGates = BuildHealthGates(catalog, forwardSim, forwardStats, netLatency002, netStressPlus, positivePeriodRate);
        var allGatesPass = healthGates.All(g => g.Pass);
        var verdict = catalog.ResolveVerdict(
            forwardSpanDays, checkpointCount, forwardStats.Count, netModerate, netLatency002, allGatesPass);

        var previousHistory = await LoadHistoryAsync(settings, catalog, cancellationToken);
        var previousHistoryEntry = previousHistory.Count > 0 ? previousHistory[^1] : null;
        var historyEntry = new ForwardIncubationHistoryEntry
        {
            RunAtUtc = runAtUtc,
            FrozenStartUtc = frozenStart,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            ForwardTrades = forwardStats.Count,
            ForwardNetModerate = netModerate,
            ForwardNetLatency002 = netLatency002,
            ForwardNetStressPlus = netStressPlus,
            MaxConsecutiveLosses = forwardStats.MaxConsecutiveLosses,
            PositivePeriodRate = positivePeriodRate,
            HealthGatesPassed = healthGates.Count(g => g.Pass),
            HealthGatesTotal = healthGates.Count,
            Verdict = verdict
        };
        var history = await AppendHistoryAsync(settings, catalog, historyEntry, cancellationToken);
        var frozenSummary = BuildFrozenSummary(
            state, runAtUtc, forwardEnd, forwardSpanDays, forwardStats.Count, netModerate, verdict, forwardStats.MaxDrawdown);

        var tradeRows = forwardSim.TakenTrades
            .Select(t => new CrossSymbolTradeRow
            {
                Symbol = frozenKey.Symbol.ToString(),
                Interval = frozenKey.Interval,
                Direction = frozenKey.Direction.ToString(),
                TargetPercent = frozenKey.TargetPercent,
                StopPercent = frozenKey.StopPercent,
                ActivationRule = frozenConfig.ActivationRuleName,
                EntryTimeUtc = t.EntryTimeUtc,
                ExitTimeUtc = t.ExitTimeUtc,
                NetPnlQuote = Math.Round(t.NetPnlQuote, 8),
                IsWinner = t.NetPnlQuote > 0m,
                ExitReason = t.ExitReason,
                CostScenario = catalog.PrimaryCostScenario
            })
            .ToArray();

        var answers = BuildAnswers(catalog, state, forwardSim, forwardStats, healthGates, verdict,
            forwardSpanDays, checkpointCount, netModerate, netLatency002, netStressPlus, positivePeriodRate);

        var baseSignalsInsideForwardWindow = scan.BaseTrades
            .Count(t => t.TimeUtc >= frozenStart && t.TimeUtc < forwardEnd);
        var activatedRanges = forwardSim.Periods
            .Where(p => p.Activated)
            .Select(p => (p.ActivationStartUtc, p.ActivationEndUtc))
            .ToArray();
        var baseSignalsInsideActivatedWindows = scan.BaseTrades
            .Count(t => t.TimeUtc >= frozenStart && t.TimeUtc < forwardEnd
                        && activatedRanges.Any(r => t.TimeUtc >= r.ActivationStartUtc && t.TimeUtc < r.ActivationEndUtc));

        var protectedHashesAfter = ForwardIncubationTrackProtection.HashFiles(protectedFiles);
        var protectedIdentical = ForwardIncubationTrackProtection.FilesByteIdentical(protectedHashesBefore, protectedHashesAfter);
        var protectedHashStatus = protectedIdentical ? "Unchanged" : "Changed";

        var latestCandleUtc = symbolData.Candles[^1].OpenTimeUtc;
        var noTradeSummary = ForwardIncubationDiagnosticsBuilder.BuildForCrossSymbolTrack(
            state,
            frozenStart,
            forwardEnd,
            forwardSpanDays,
            latestCandleUtc,
            forwardStats.Count,
            netModerate,
            netLatency002,
            netStressPlus,
            forwardSim.Periods,
            tradeRows,
            scan.BaseTrades,
            healthGates,
            verdict,
            previousHistoryEntry,
            catalog.TrackLabel,
            frozenHashStatus: $"Protected frozen tracks hash {protectedHashStatus.ToLowerInvariant()}",
            bnbFrozenHashStatus: includeBnb5mProtection || includeBnb15mProtection ? protectedHashStatus : "NotChecked",
            solFrozenHashStatus: includeSol5mProtection || includeSol15mProtection ? protectedHashStatus : "NotChecked",
            protectedTracksHashStatus: protectedHashStatus,
            frozenFilesTouched: string.Join("; ", protectedFiles),
            settings.OutputDirectory);

        return new CrossSymbolForwardIncubationRunResult(
            catalog.ModeName,
            catalog.FrozenProfileName,
            catalog.FrozenStatePath(settings.DataDirectory),
            catalog.ForwardHistoryPath(settings.DataDirectory),
            frozenSummary,
            coverage,
            tradeRows,
            forwardSim.Periods,
            costRows,
            healthGates,
            history,
            answers,
            noTradeSummary,
            verdict,
            frozenStart,
            forwardEnd,
            forwardSpanDays,
            protectedIdentical,
            protectedFiles,
            downloadOutcomes,
            settings.BootstrapFuturesData,
            acceleratedValidation,
            baseSignalsInsideForwardWindow,
            baseSignalsInsideActivatedWindows,
            forwardSim.ClusterCount);
    }

    private static async Task<FrozenCandidateState> LoadOrCreateFrozenStateAsync(
        BacktestSettings settings, CatalogRefs catalog, DateTime runAtUtc, CancellationToken cancellationToken)
    {
        var path = catalog.FrozenStatePath(settings.DataDirectory);
        if (File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<FrozenCandidateState>(
                await File.ReadAllTextAsync(path, cancellationToken));
            if (existing is not null)
                return existing;
        }

        var state = catalog.BuildDefaultState(runAtUtc);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        return state;
    }

    private static async Task<IReadOnlyList<ForwardIncubationHistoryEntry>> LoadHistoryAsync(
        BacktestSettings settings, CatalogRefs catalog, CancellationToken cancellationToken)
    {
        var path = catalog.ForwardHistoryPath(settings.DataDirectory);
        if (!File.Exists(path))
            return [];

        var existing = JsonSerializer.Deserialize<List<ForwardIncubationHistoryEntry>>(
            await File.ReadAllTextAsync(path, cancellationToken));
        return existing ?? [];
    }

    private static async Task<IReadOnlyList<ForwardIncubationHistoryEntry>> AppendHistoryAsync(
        BacktestSettings settings,
        CatalogRefs catalog,
        ForwardIncubationHistoryEntry entry,
        CancellationToken cancellationToken)
    {
        var path = catalog.ForwardHistoryPath(settings.DataDirectory);
        var history = new List<ForwardIncubationHistoryEntry>(await LoadHistoryAsync(settings, catalog, cancellationToken));
        history.Add(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(history, JsonOptions), cancellationToken);
        return history;
    }

    private static IReadOnlyList<ForwardDataCoverageRow> BuildDataCoverage(
        FuturesMarketDataLoader futuresLoader, CatalogRefs catalog, DateTime frozenStartUtc)
    {
        var rows = new List<ForwardDataCoverageRow>();
        foreach (var symbol in catalog.CoverageSymbols)
        foreach (var sourceKey in catalog.CoverageSourceKeys)
        {
            var raw = futuresLoader.LoadRaw(symbol, sourceKey);
            var timestamps = raw
                .Select(r => long.TryParse(r.TryGetValue("t", out var t) ? t : "0", NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0L)
                .Where(v => v > 0).OrderBy(v => v).ToArray();
            var present = timestamps.Length > 0;
            DateTime? localStart = present ? DateTimeOffset.FromUnixTimeMilliseconds(timestamps[0]).UtcDateTime : null;
            DateTime? localEnd = present ? DateTimeOffset.FromUnixTimeMilliseconds(timestamps[^1]).UtcDateTime : null;
            var span = present ? Math.Round((decimal)(localEnd!.Value - localStart!.Value).TotalDays, 2) : 0m;
            var beyondFrozen = present && localEnd!.Value > frozenStartUtc
                ? Math.Round((decimal)(localEnd.Value - frozenStartUtc).TotalDays, 4)
                : 0m;

            rows.Add(new ForwardDataCoverageRow
            {
                Symbol = symbol.ToString(),
                SourceKey = sourceKey,
                LocalFilePresent = present,
                LocalRecordCount = timestamps.Length,
                LocalStartUtc = localStart,
                LocalEndUtc = localEnd,
                LocalSpanDays = span,
                DaysBeyondFrozenStart = beyondFrozen,
                Notes = present
                    ? "Merged local cache; accumulates beyond the ~30d Binance retention across runs."
                    : "No local file yet; run with --bootstrap-futures-data true."
            });
        }

        return rows;
    }

    private static IReadOnlyList<ForwardHealthGateRow> BuildHealthGates(
        CatalogRefs catalog,
        CrossSymbolSimOutcome forwardSim,
        TradeStats stats,
        decimal netLatency002,
        decimal netStressPlus,
        decimal positivePeriodRate)
    {
        var net = stats.Net;
        var rows = new List<ForwardHealthGateRow>
        {
            Gate("MinForwardTrades",
                $">= {catalog.MinForwardTrades} new forward trades",
                stats.Count.ToString(CultureInfo.InvariantCulture),
                true,
                stats.Count >= catalog.MinForwardTrades),
            Gate("ForwardNetPositiveModerate",
                "Forward net > 0 under futures-moderate",
                net.ToString("F2", CultureInfo.InvariantCulture),
                true,
                net > 0m),
            Gate("ForwardNetPositiveLatency002",
                "Forward net > 0 under futures-moderate-latency-002",
                netLatency002.ToString("F2", CultureInfo.InvariantCulture),
                true,
                netLatency002 > 0m),
            Gate("StressPlusNotCollapsed",
                $"futures-stress-plus net >= {catalog.StressPlusCollapseFloorQuote:F0}",
                netStressPlus.ToString("F2", CultureInfo.InvariantCulture),
                true,
                netStressPlus >= catalog.StressPlusCollapseFloorQuote),
            Gate("MaxConsecutiveLosses",
                $"<= {catalog.MaxConsecutiveLossesLimit} consecutive losses",
                stats.MaxConsecutiveLosses.ToString(CultureInfo.InvariantCulture),
                true,
                stats.MaxConsecutiveLosses <= catalog.MaxConsecutiveLossesLimit)
        };

        var dailyNets = forwardSim.TakenTrades
            .GroupBy(t => t.ExitTimeUtc.Date)
            .Select(g => g.Sum(t => t.NetPnlQuote))
            .ToArray();
        var maxDay = dailyNets.Length > 0 ? dailyNets.Max() : 0m;
        var dayShare = net > 0m ? Math.Round(maxDay / net, 4) : (decimal?)null;
        rows.Add(Gate("SingleDayProfitConcentration",
            $"No single day > {catalog.MaxSingleDayProfitShare:P0} of total profit",
            dayShare.HasValue ? dayShare.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a (net <= 0)",
            net > 0m,
            net > 0m && dayShare.HasValue && dayShare.Value <= catalog.MaxSingleDayProfitShare,
            net > 0m ? string.Empty : "Not applicable while forward net is non-positive."));

        rows.Add(Gate("PositiveActivatedPeriods",
            $">= {catalog.MinPositivePeriodRate:P0} of activated periods positive",
            $"{positivePeriodRate:P1} ({forwardSim.PositivePeriodCount}/{forwardSim.ActivatedPeriodCount})",
            forwardSim.ActivatedPeriodCount > 0,
            forwardSim.ActivatedPeriodCount > 0 && positivePeriodRate >= catalog.MinPositivePeriodRate));

        var ddRatio = net > 0m ? Math.Round(stats.MaxDrawdown / net, 4) : (decimal?)null;
        rows.Add(Gate("DrawdownRelativeToNet",
            $"Max drawdown <= {catalog.MaxDrawdownToNetRatio:F1}x forward net (net must be > 0)",
            ddRatio.HasValue
                ? $"dd={stats.MaxDrawdown:F2}, ratio={ddRatio.Value:F2}"
                : $"dd={stats.MaxDrawdown:F2}, net <= 0",
            net > 0m,
            net > 0m && ddRatio.HasValue && ddRatio.Value <= catalog.MaxDrawdownToNetRatio,
            net > 0m ? string.Empty : "Not applicable while forward net is non-positive."));

        return rows;
    }

    private static ForwardHealthGateRow Gate(
        string name, string requirement, string observed, bool applicable, bool pass, string notes = "")
        => new()
        {
            GateName = name,
            Requirement = requirement,
            ObservedValue = observed,
            Applicable = applicable,
            Pass = pass,
            Notes = notes
        };

    private static FrozenCandidateSummaryRow BuildFrozenSummary(
        FrozenCandidateState state, DateTime runAtUtc, DateTime forwardEnd, decimal forwardSpanDays,
        int forwardTrades, decimal forwardNetModerate, string verdict, decimal maxDrawdown)
    {
        var normalizedRisk = NormalizedRiskPnlModule.Compute(state.Symbol, forwardNetModerate, maxDrawdown);
        return new()
        {
            ProfileName = state.ProfileName,
            CreatedAtUtc = state.CreatedAtUtc,
            FrozenStartUtc = state.FrozenStartUtc,
            BaseRule = state.BaseRule,
            Symbol = state.Symbol,
            Interval = state.Interval,
            EntryMode = state.EntryMode,
            TargetPercent = state.TargetPercent,
            StopPercent = state.StopPercent,
            MaxHoldMinutes = state.MaxHoldMinutes,
            CooldownCandles = state.CooldownCandles,
            OverlapPolicy = state.OverlapPolicy,
            ActivationFlowCondition = state.ActivationFlowCondition,
            CheckpointFrequencyHours = state.CheckpointFrequencyHours,
            ActivationPeriodHours = state.ActivationPeriodHours,
            LookbackDaysInformational = state.LookbackDaysInformational,
            DiscoveryWindow = state.DiscoveryWindow,
            DiscoveryBaselineTrades = state.DiscoveryBaselineTrades,
            DiscoveryBaselineNet = state.DiscoveryBaselineNet,
            DiscoveryCandidateTrades = state.DiscoveryCandidateTrades,
            DiscoveryCandidateNet = state.DiscoveryCandidateNet,
            DiscoveryCandidateProfitFactor = state.DiscoveryCandidateProfitFactor,
            DiscoveryCandidateStressPlusNet = state.DiscoveryCandidateStressPlusNet,
            Caveats = state.Caveats,
            RunAtUtc = runAtUtc,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            ForwardTrades = forwardTrades,
            ForwardNetModerate = forwardNetModerate,
            Verdict = verdict,
            NormalizedRisk = normalizedRisk
        };
    }

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

    private static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        CatalogRefs catalog,
        FrozenCandidateState state,
        CrossSymbolSimOutcome forwardSim,
        TradeStats stats,
        IReadOnlyList<ForwardHealthGateRow> healthGates,
        string verdict,
        decimal forwardSpanDays,
        int checkpointCount,
        decimal netModerate,
        decimal netLatency002,
        decimal netStressPlus,
        decimal positivePeriodRate)
    {
        var hasForwardData = verdict != "NotEnoughForwardDataYet";
        var clusterAnswer = forwardSim.ClusterCount switch
        {
            0 => "No activation clusters with trades yet.",
            1 => "All forward profits/losses come from a single activation cluster.",
            _ => $"Spread across {forwardSim.ClusterCount} activation clusters."
        };

        return
        [
            new ReachabilityResearchAnswer
            {
                Question = $"Did the frozen {catalog.AnswersSymbolLabel} candidate have any truly new forward data after discovery?",
                Answer = hasForwardData
                    ? $"Yes: {forwardSpanDays:F2} day(s) after frozen start {state.FrozenStartUtc:yyyy-MM-dd HH:mm}Z, with {checkpointCount} checkpoint(s) and {stats.Count} forward trade(s)."
                    : $"No: only {forwardSpanDays:F2} day(s) of data exist after the frozen start {state.FrozenStartUtc:yyyy-MM-dd HH:mm}Z (minimum {catalog.MinForwardSpanDaysForJudgment:F0}d). Frozen profile and report framework generated; re-run after more data accumulates.",
                Verdict = hasForwardData ? "ForwardDataAvailable" : "NotEnoughForwardDataYet"
            },
            new ReachabilityResearchAnswer
            {
                Question = "If yes, did it remain profitable?",
                Answer = !hasForwardData
                    ? "Not assessable yet."
                    : $"Forward net under futures-moderate: {netModerate:F2} over {stats.Count} trades (win rate {stats.WinRate:P1}, PF {stats.ProfitFactor:F2}).",
                Verdict = !hasForwardData ? "NotAssessable" : netModerate > 0m ? "ForwardProfitable" : "ForwardNotProfitable"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Did it survive moderate+0.02 slippage?",
                Answer = !hasForwardData
                    ? "Not assessable yet."
                    : $"Forward net under futures-moderate-latency-002: {netLatency002:F2}.",
                Verdict = !hasForwardData ? "NotAssessable" : netLatency002 > 0m ? "SurvivesModerateSlippage" : "FailsModerateSlippage"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Did it survive stress-plus or at least avoid collapse?",
                Answer = !hasForwardData
                    ? "Not assessable yet."
                    : $"Forward net under futures-stress-plus: {netStressPlus:F2} (collapse floor {catalog.StressPlusCollapseFloorQuote:F0}).",
                Verdict = !hasForwardData ? "NotAssessable"
                    : netStressPlus > 0m ? "SurvivesStressPlus"
                    : netStressPlus >= catalog.StressPlusCollapseFloorQuote ? "AvoidsCollapse" : "CollapsesUnderStressPlus"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Are profits spread or still one cluster?",
                Answer = !hasForwardData ? "Not assessable yet." : clusterAnswer,
                Verdict = !hasForwardData ? "NotAssessable"
                    : forwardSim.ClusterCount >= 2 ? "SpreadAcrossClusters"
                    : forwardSim.ClusterCount == 1 ? "SingleClusterOnly" : "NoClustersYet"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Is it enough for paper/sandbox later?",
                Answer = $"Health gates passed: {healthGates.Count(g => g.Pass)}/{healthGates.Count}. " +
                         (verdict == "CandidateEligibleForPaperLater"
                             ? "All gates pass — eligible for paper/sandbox consideration later. Still research-only; no live recommendation from this run."
                             : "Not yet. Eligibility requires every health gate to pass on truly forward data."),
                Verdict = verdict == "CandidateEligibleForPaperLater" ? "EligibleForPaperLater" : "NotEligibleYet",
                Details = new Dictionary<string, object?>
                {
                    ["gates"] = healthGates.Select(g => new { g.GateName, g.ObservedValue, g.Pass }).ToArray(),
                    ["positivePeriodRate"] = positivePeriodRate
                }
            },
            new ReachabilityResearchAnswer
            {
                Question = "Should we keep incubating, retest after more collected data, or park it?",
                Answer = verdict switch
                {
                    "NotEnoughForwardDataYet" => "Keep collecting free flow data (merge downloader) and re-run; no judgment possible yet.",
                    "KeepIncubating" => "Keep incubating: forward sample too small for judgment; re-run after more data accumulates.",
                    "CandidateImproving" => "Keep incubating: forward results positive but gates not all passed yet.",
                    "CandidateDeteriorating" => "Keep incubating with caution: forward results negative but not conclusive; if deterioration persists, park it.",
                    "CandidateFailed" => "Park the candidate: forward window contradicts the discovery result.",
                    "CandidateEligibleForPaperLater" => "All gates pass — review for paper/sandbox planning after at least one more independent forward window.",
                    _ => "Keep incubating."
                },
                Verdict = verdict,
                Details = new Dictionary<string, object?>
                {
                    ["backtestOnly"] = true,
                    ["liveFuturesRecommended"] = false,
                    ["paidDataUsed"] = false,
                    ["noNewOptimization"] = true,
                    ["besideExistingTracksNote"] = catalog.AnswersBesideTracksNote
                }
            }
        ];
    }

    private static async Task RefreshCandlesAsync(
        HistoricalKlineDataLoader loader, string dataDirectory, TradingSymbol symbol, CancellationToken cancellationToken)
    {
        var downloader = new BinanceKlineBootstrapDownloader();
        foreach (var refreshSymbol in new[] { symbol, TradingSymbol.BTCUSDT })
        {
            var existing = await loader.LoadAndValidateAsync(refreshSymbol, cancellationToken);
            var lastUtc = existing.Candles.Count > 0 ? existing.Candles[^1].OpenTimeUtc : DateTime.UtcNow.AddDays(-35);
            var nowUtc = DateTime.UtcNow;
            if ((nowUtc - lastUtc) < TimeSpan.FromMinutes(30))
                continue;
            var path = Path.Combine(dataDirectory, $"{refreshSymbol}-1m.json");
            await downloader.DownloadAndMergeToJsonAsync(
                refreshSymbol.ToString(), path, 1000, lastUtc.AddHours(-2), nowUtc, cancellationToken);
        }
    }
}

public sealed record CrossSymbolForwardIncubationRunResult(
    string ModeName,
    string FrozenProfileName,
    string FrozenStatePath,
    string ForwardHistoryPath,
    FrozenCandidateSummaryRow FrozenSummary,
    IReadOnlyList<ForwardDataCoverageRow> DataCoverage,
    IReadOnlyList<CrossSymbolTradeRow> ForwardTrades,
    IReadOnlyList<CrossSymbolPeriodRow> ForwardPeriods,
    IReadOnlyList<CrossSymbolCostSensitivityRow> CostSensitivity,
    IReadOnlyList<ForwardHealthGateRow> HealthGates,
    IReadOnlyList<ForwardIncubationHistoryEntry> History,
    IReadOnlyList<ReachabilityResearchAnswer> Answers,
    ForwardIncubationNoTradeReasonSummary NoTradeReasonSummary,
    string Verdict,
    DateTime FrozenStartUtc,
    DateTime ForwardWindowEndUtc,
    decimal ForwardSpanDays,
    bool ProtectedFrozenFilesByteIdentical,
    IReadOnlyList<string> ProtectedFilesChecked,
    IReadOnlyList<ShortWindowDownloadOutcome> DownloadOutcomes,
    bool BootstrapAttempted,
    ForwardIncubationAcceleratedValidationSummary AcceleratedValidation,
    int BaseSignalsInsideForwardWindow,
    int BaseSignalsInsideActivatedWindows,
    int ForwardClusterCount);
