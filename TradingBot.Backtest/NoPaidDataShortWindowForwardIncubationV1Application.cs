using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// NoPaidDataShortWindowForwardIncubationV1 — backtest/research only.
/// Freezes the best candidate from NoPaidDataShortWindowFlowResearchV1 and evaluates it on
/// strictly-forward data only. No rule reselection, no activation retuning, no threshold changes.
/// Free data only; no production/live changes; no real orders; no API keys in source control.
/// </summary>
public sealed class NoPaidDataShortWindowForwardIncubationV1Application(BacktestSettings settings)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<NoPaidDataShortWindowForwardIncubationV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var runAtUtc = DateTime.UtcNow;
        var loader = new HistoricalKlineDataLoader(settings);
        var downloadOutcomes = new List<ShortWindowDownloadOutcome>();

        // Task 2 — continue the free-data merge (never overwrites previously collected rows).
        if (settings.BootstrapFuturesData)
        {
            await RefreshCandlesAsync(loader, cancellationToken);
            var flowDownloader = new ShortWindowFlowDataDownloader();
            downloadOutcomes.AddRange(await flowDownloader.DownloadAllAsync(
                settings.DataDirectory, FuturesMarketDataCatalog.Symbols, runAtUtc.AddDays(-365), runAtUtc, cancellationToken));
        }

        // Task 1 — frozen candidate config (created once, then always read back unchanged).
        var state = await LoadOrCreateFrozenStateAsync(runAtUtc, cancellationToken);
        var frozenConfig = NoPaidDataShortWindowForwardIncubationV1Catalog.BuildFrozenActivationConfig();

        var bnb = await loader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken);
        var btc = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        if (bnb.Candles.Count == 0 || btc.Candles.Count == 0)
            throw new InvalidOperationException("BNBUSDT and BTCUSDT local data required for forward incubation.");

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

        var futuresLoader = new FuturesMarketDataLoader(settings.DataDirectory);
        var flowIndex = new ShortWindowFlowFeatureIndex(futuresLoader, TradingSymbol.BNBUSDT, intervalCandles, windowBtc);

        // Task 2 — coverage report for the flow sources backing the frozen rule.
        var coverage = BuildDataCoverage(futuresLoader, state.FrozenStartUtc);

        // Base Rule01 short trades (single fixed profile scan; no rule search).
        var discoveryPath = ResolveDiscoveryJsonPath();
        var rules = await DirectionalRuleFuturesSimulationV1RuleCatalog.LoadHoldoutRulesAsync(discoveryPath, cancellationToken);
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException("Rule01 not found.");
        var profile = NoPaidDataShortWindowFlowResearchV1Catalog.BuildProfile(rule);
        var v2Profile = DirectionalRuleFuturesValidationV31Catalog.ToV2Profile(profile);
        var scan = DirectionalRuleFuturesValidationV2Simulator.ScanProfile(
            v2Profile, "365d", intervalCandles, windowBnb, btcContext, marketWideContext, cancellationToken);

        // Tasks 3+4 — forward-only evaluation: study window starts strictly at the frozen timestamp.
        var frozenStart = state.FrozenStartUtc;
        var forwardEnd = dataEndUtc > frozenStart ? dataEndUtc : frozenStart;
        var forwardSpanDays = Math.Round((decimal)(forwardEnd - frozenStart).TotalDays, 4);

        var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
            scan.Trades, NoPaidDataShortWindowForwardIncubationV1Catalog.PrimaryCostScenario, btcContext, dataEndUtc);
        var forwardSim = NoPaidDataShortWindowFlowResearchV1Engine.Simulate(
            frozenConfig, moderateTrades, frozenStart, forwardEnd, flowIndex,
            NoPaidDataShortWindowForwardIncubationV1Catalog.PrimaryCostScenario);

        // Same 5 scenarios as the research branch catalog (futures-moderate .. stress-plus).
        var costSensitivity = NoPaidDataShortWindowFlowResearchV1Aggregator.BuildCostSensitivity(
            frozenConfig, scan.Trades, btcContext, frozenStart, forwardEnd, dataEndUtc, flowIndex);

        decimal NetFor(string scenario) => costSensitivity
            .FirstOrDefault(c => string.Equals(c.CostScenario, scenario, StringComparison.OrdinalIgnoreCase))
            ?.NetPnlQuote ?? 0m;

        var netModerate = forwardSim.Summary.NetPnlQuote;
        var netLatency002 = NetFor(NoPaidDataShortWindowForwardIncubationV1Catalog.ModerateSlippageScenario);
        var netStressPlus = NetFor(NoPaidDataShortWindowForwardIncubationV1Catalog.StressPlusScenario);

        // Task 5 — conservative health gates (fixed thresholds, forward window only).
        var healthGates = BuildHealthGates(forwardSim, netLatency002, netStressPlus);
        var allGatesPass = healthGates.All(g => g.Pass);

        var verdict = NoPaidDataShortWindowForwardIncubationV1Catalog.ResolveVerdict(
            forwardSpanDays, forwardSim.Summary.CheckpointCount, forwardSim.Summary.TotalTrades,
            netModerate, netLatency002, allGatesPass);

        var previousHistory = await LoadHistoryAsync(cancellationToken);
        var previousHistoryEntry = previousHistory.Count > 0 ? previousHistory[^1] : null;

        // Task 4 — append this run to the persistent forward-incubation history.
        var historyEntry = new ForwardIncubationHistoryEntry
        {
            RunAtUtc = runAtUtc,
            FrozenStartUtc = frozenStart,
            ForwardWindowEndUtc = forwardEnd,
            ForwardSpanDays = forwardSpanDays,
            ForwardTrades = forwardSim.Summary.TotalTrades,
            ForwardNetModerate = netModerate,
            ForwardNetLatency002 = netLatency002,
            ForwardNetStressPlus = netStressPlus,
            MaxConsecutiveLosses = forwardSim.Summary.MaxConsecutiveLosses,
            PositivePeriodRate = forwardSim.Summary.PositivePeriodRate,
            HealthGatesPassed = healthGates.Count(g => g.Pass),
            HealthGatesTotal = healthGates.Count,
            Verdict = verdict
        };
        var history = await AppendHistoryAsync(historyEntry, cancellationToken);

        var frozenSummary = BuildFrozenSummary(
            state, runAtUtc, forwardEnd, forwardSpanDays, forwardSim.Summary.TotalTrades, netModerate, verdict,
            forwardSim.Summary.MaxDrawdownQuote);

        var latestCandleUtc = bnb.Candles[^1].OpenTimeUtc;
        var frozenStatePath = NoPaidDataShortWindowForwardIncubationV1Catalog.FrozenStatePath(settings.DataDirectory);
        var forwardHistoryPath = NoPaidDataShortWindowForwardIncubationV1Catalog.ForwardHistoryPath(settings.DataDirectory);
        var noTradeSummary = ForwardIncubationDiagnosticsBuilder.BuildForBnb(
            state,
            frozenStart,
            forwardEnd,
            forwardSpanDays,
            latestCandleUtc,
            forwardSim.Summary.TotalTrades,
            netModerate,
            netLatency002,
            netStressPlus,
            forwardSim.Periods,
            forwardSim.Trades,
            scan.Skipped,
            healthGates,
            verdict,
            previousHistoryEntry,
            frozenHashStatus: "FrozenStateUnmodified",
            frozenFilesTouched: $"{frozenStatePath} (read-only); {forwardHistoryPath} (append-only)",
            settings.OutputDirectory);

        var answers = BuildAnswers(state, forwardSim, costSensitivity, healthGates, verdict, forwardSpanDays, netModerate, netLatency002, netStressPlus);

        var result = new NoPaidDataShortWindowForwardIncubationV1RunResult(
            frozenSummary, coverage, forwardSim.Trades, forwardSim.Periods, costSensitivity,
            healthGates, history, answers, noTradeSummary, verdict, frozenStart, forwardEnd, forwardSpanDays);

        Directory.CreateDirectory(settings.OutputDirectory);
        var writer = new NoPaidDataShortWindowForwardIncubationV1ReportWriter(settings.OutputDirectory);
        await writer.WriteAsync(result, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "no-paid-short-window-forward-incubation-v1",
                settings.DataDirectory,
                settings.OutputDirectory,
                frozenProfile = state.ProfileName,
                frozenStatePath = NoPaidDataShortWindowForwardIncubationV1Catalog.FrozenStatePath(settings.DataDirectory),
                forwardHistoryPath = NoPaidDataShortWindowForwardIncubationV1Catalog.ForwardHistoryPath(settings.DataDirectory),
                forwardHistoryRunCount = history.Count,
                frozenStartUtc = frozenStart,
                forwardWindowEndUtc = forwardEnd,
                forwardSpanDays,
                forwardTrades = forwardSim.Summary.TotalTrades,
                forwardNetModerate = netModerate,
                forwardNetLatency002 = netLatency002,
                forwardNetStressPlus = netStressPlus,
                healthGatesPassed = healthGates.Count(g => g.Pass),
                healthGatesTotal = healthGates.Count,
                verdict,
                reportStatus = noTradeSummary.ReportStatus,
                latestRunStatus = noTradeSummary.LatestRunStatus,
                dataAdvancedSincePreviousRun = noTradeSummary.DataAdvancedSincePreviousRun,
                newTradesSincePreviousRun = noTradeSummary.NewTradesSincePreviousRun,
                newNetModerateSincePreviousRun = noTradeSummary.NewNetModerateSincePreviousRun,
                newNetStressPlusSincePreviousRun = noTradeSummary.NewNetStressPlusSincePreviousRun,
                previousRunForwardWindowEndUtc = noTradeSummary.PreviousRunForwardWindowEndUtc,
                currentRunForwardWindowEndUtc = noTradeSummary.CurrentRunForwardWindowEndUtc,
                compactSummaryLine = noTradeSummary.CompactSummaryLine,
                nextAction = noTradeSummary.NextAction,
                topActivationSkipReasons = noTradeSummary.TopActivationSkipReasons,
                topEntrySkipReasons = noTradeSummary.TopEntrySkipReasons,
                bootstrapAttempted = settings.BootstrapFuturesData,
                downloadOutcomes = downloadOutcomes.Select(o => new { o.Symbol, o.SourceKey, o.Success, o.AddedCount, o.TotalCount, o.Message }),
                noNewOptimization = true,
                frozenRuleOnly = true,
                forwardOnlyJudgment = true,
                backtestOnly = true,
                liveFuturesRecommended = false,
                paidDataUsed = false,
                realOrdersPlaced = false
            }, JsonOptions),
            cancellationToken);

        return result;
    }

    private async Task<FrozenCandidateState> LoadOrCreateFrozenStateAsync(DateTime runAtUtc, CancellationToken cancellationToken)
    {
        var path = NoPaidDataShortWindowForwardIncubationV1Catalog.FrozenStatePath(settings.DataDirectory);
        if (File.Exists(path))
        {
            var existing = JsonSerializer.Deserialize<FrozenCandidateState>(
                await File.ReadAllTextAsync(path, cancellationToken));
            if (existing is not null)
                return existing; // Never modified after creation.
        }

        var state = NoPaidDataShortWindowForwardIncubationV1Catalog.BuildDefaultState(runAtUtc);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        return state;
    }

    private async Task<IReadOnlyList<ForwardIncubationHistoryEntry>> LoadHistoryAsync(CancellationToken cancellationToken)
    {
        var path = NoPaidDataShortWindowForwardIncubationV1Catalog.ForwardHistoryPath(settings.DataDirectory);
        if (!File.Exists(path))
            return [];

        var existing = JsonSerializer.Deserialize<List<ForwardIncubationHistoryEntry>>(
            await File.ReadAllTextAsync(path, cancellationToken));
        return existing ?? [];
    }

    private async Task<IReadOnlyList<ForwardIncubationHistoryEntry>> AppendHistoryAsync(
        ForwardIncubationHistoryEntry entry, CancellationToken cancellationToken)
    {
        var path = NoPaidDataShortWindowForwardIncubationV1Catalog.ForwardHistoryPath(settings.DataDirectory);
        var history = new List<ForwardIncubationHistoryEntry>(await LoadHistoryAsync(cancellationToken));

        history.Add(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(history, JsonOptions), cancellationToken);
        return history;
    }

    private IReadOnlyList<ForwardDataCoverageRow> BuildDataCoverage(FuturesMarketDataLoader futuresLoader, DateTime frozenStartUtc)
    {
        var rows = new List<ForwardDataCoverageRow>();
        foreach (var symbol in FuturesMarketDataCatalog.Symbols)
        foreach (var sourceKey in NoPaidDataShortWindowForwardIncubationV1Catalog.CoverageSourceKeys)
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
        ShortWindowSimResult forwardSim, decimal netLatency002, decimal netStressPlus)
    {
        var summary = forwardSim.Summary;
        var net = summary.NetPnlQuote;
        var rows = new List<ForwardHealthGateRow>();

        rows.Add(Gate("MinForwardTrades",
            $">= {NoPaidDataShortWindowForwardIncubationV1Catalog.MinForwardTrades} new forward trades",
            summary.TotalTrades.ToString(CultureInfo.InvariantCulture),
            true,
            summary.TotalTrades >= NoPaidDataShortWindowForwardIncubationV1Catalog.MinForwardTrades));

        rows.Add(Gate("ForwardNetPositiveModerate",
            "Forward net > 0 under futures-moderate",
            net.ToString("F2", CultureInfo.InvariantCulture),
            true,
            net > 0m));

        rows.Add(Gate("ForwardNetPositiveLatency002",
            "Forward net > 0 under futures-moderate-latency-002",
            netLatency002.ToString("F2", CultureInfo.InvariantCulture),
            true,
            netLatency002 > 0m));

        rows.Add(Gate("StressPlusNotCollapsed",
            $"futures-stress-plus net >= {NoPaidDataShortWindowForwardIncubationV1Catalog.StressPlusCollapseFloorQuote:F0}",
            netStressPlus.ToString("F2", CultureInfo.InvariantCulture),
            true,
            netStressPlus >= NoPaidDataShortWindowForwardIncubationV1Catalog.StressPlusCollapseFloorQuote));

        rows.Add(Gate("MaxConsecutiveLosses",
            $"<= {NoPaidDataShortWindowForwardIncubationV1Catalog.MaxConsecutiveLossesLimit} consecutive losses",
            summary.MaxConsecutiveLosses.ToString(CultureInfo.InvariantCulture),
            true,
            summary.MaxConsecutiveLosses <= NoPaidDataShortWindowForwardIncubationV1Catalog.MaxConsecutiveLossesLimit));

        var dailyNets = forwardSim.Trades
            .GroupBy(t => t.ExitTimeUtc.Date)
            .Select(g => g.Sum(t => t.NetPnlQuote))
            .ToArray();
        var maxDay = dailyNets.Length > 0 ? dailyNets.Max() : 0m;
        var dayShare = net > 0m ? Math.Round(maxDay / net, 4) : (decimal?)null;
        rows.Add(Gate("SingleDayProfitConcentration",
            $"No single day > {NoPaidDataShortWindowForwardIncubationV1Catalog.MaxSingleDayProfitShare:P0} of total profit",
            dayShare.HasValue ? dayShare.Value.ToString("P1", CultureInfo.InvariantCulture) : "n/a (net <= 0)",
            net > 0m,
            net > 0m && dayShare.HasValue && dayShare.Value <= NoPaidDataShortWindowForwardIncubationV1Catalog.MaxSingleDayProfitShare,
            net > 0m ? string.Empty : "Not applicable while forward net is non-positive."));

        rows.Add(Gate("PositiveActivatedPeriods",
            $">= {NoPaidDataShortWindowForwardIncubationV1Catalog.MinPositivePeriodRate:P0} of activated periods positive",
            $"{summary.PositivePeriodRate:P1} ({summary.PositivePeriodCount}/{summary.ActivatedPeriodCount})",
            summary.ActivatedPeriodCount > 0,
            summary.ActivatedPeriodCount > 0
            && summary.PositivePeriodRate >= NoPaidDataShortWindowForwardIncubationV1Catalog.MinPositivePeriodRate));

        var ddRatio = net > 0m ? Math.Round(summary.MaxDrawdownQuote / net, 4) : (decimal?)null;
        rows.Add(Gate("DrawdownRelativeToNet",
            $"Max drawdown <= {NoPaidDataShortWindowForwardIncubationV1Catalog.MaxDrawdownToNetRatio:F1}x forward net (net must be > 0)",
            ddRatio.HasValue
                ? $"dd={summary.MaxDrawdownQuote:F2}, ratio={ddRatio.Value:F2}"
                : $"dd={summary.MaxDrawdownQuote:F2}, net <= 0",
            net > 0m,
            net > 0m && ddRatio.HasValue && ddRatio.Value <= NoPaidDataShortWindowForwardIncubationV1Catalog.MaxDrawdownToNetRatio,
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

    private static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        FrozenCandidateState state,
        ShortWindowSimResult forwardSim,
        IReadOnlyList<ShortWindowCostSensitivityRow> costSensitivity,
        IReadOnlyList<ForwardHealthGateRow> healthGates,
        string verdict,
        decimal forwardSpanDays,
        decimal netModerate,
        decimal netLatency002,
        decimal netStressPlus)
    {
        var summary = forwardSim.Summary;
        var hasForwardData = verdict != "NotEnoughForwardDataYet";
        var clusterAnswer = summary.ActivationClusterCount switch
        {
            0 => "No activation clusters with trades yet.",
            1 => "All forward profits/losses come from a single activation cluster.",
            _ => $"Spread across {summary.ActivationClusterCount} activation clusters."
        };

        return
        [
            new ReachabilityResearchAnswer
            {
                Question = "Did the frozen candidate have any truly new forward data after discovery?",
                Answer = hasForwardData
                    ? $"Yes: {forwardSpanDays:F2} day(s) after frozen start {state.FrozenStartUtc:yyyy-MM-dd HH:mm}Z, with {summary.CheckpointCount} checkpoint(s) and {summary.TotalTrades} forward trade(s)."
                    : $"No: only {forwardSpanDays:F2} day(s) of data exist after the frozen start {state.FrozenStartUtc:yyyy-MM-dd HH:mm}Z (minimum {NoPaidDataShortWindowForwardIncubationV1Catalog.MinForwardSpanDaysForJudgment:F0}d). Frozen profile and report framework generated; re-run after more data accumulates.",
                Verdict = hasForwardData ? "ForwardDataAvailable" : "NotEnoughForwardDataYet"
            },
            new ReachabilityResearchAnswer
            {
                Question = "If yes, did it remain profitable?",
                Answer = !hasForwardData
                    ? "Not assessable yet."
                    : $"Forward net under futures-moderate: {netModerate:F2} over {summary.TotalTrades} trades (win rate {summary.WinRate:P1}, PF {summary.ProfitFactor:F2}).",
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
                    : $"Forward net under futures-stress-plus: {netStressPlus:F2} (collapse floor {NoPaidDataShortWindowForwardIncubationV1Catalog.StressPlusCollapseFloorQuote:F0}).",
                Verdict = !hasForwardData ? "NotAssessable"
                    : netStressPlus > 0m ? "SurvivesStressPlus"
                    : netStressPlus >= NoPaidDataShortWindowForwardIncubationV1Catalog.StressPlusCollapseFloorQuote ? "AvoidsCollapse" : "CollapsesUnderStressPlus"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Are profits spread or still one cluster?",
                Answer = !hasForwardData ? "Not assessable yet." : clusterAnswer,
                Verdict = !hasForwardData ? "NotAssessable"
                    : summary.ActivationClusterCount >= 2 ? "SpreadAcrossClusters"
                    : summary.ActivationClusterCount == 1 ? "SingleClusterOnly" : "NoClustersYet"
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
                    ["gates"] = healthGates.Select(g => new { g.GateName, g.ObservedValue, g.Pass }).ToArray()
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
                    ["noNewOptimization"] = true
                }
            }
        ];
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
