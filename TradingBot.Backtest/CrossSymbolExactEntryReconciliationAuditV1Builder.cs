using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class CrossSymbolExactEntryReconciliationAuditV1Builder
{
    private static readonly Dictionary<string, CrossSymbolActivationConfig> ActivationLookup =
        NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.BuildActivationConfigs()
            .ToDictionary(c => c.ActivationRuleName, StringComparer.OrdinalIgnoreCase);

    public static CrossSymbolExactEntryReconciliationAuditV1RunResult Build(
        CrossSymbolExactEntryReconciliationAuditV1InputBundle input,
        CurrentOpportunityScannerV1MarketContext market,
        DateTime runAtUtc,
        DateTime studyStartUtc,
        DateTime studyEndUtc)
    {
        var periodsByKey = GroupPeriods(input.Periods);
        var tradesByKey = GroupTrades(input.Trades);
        var candidateRows = new List<CrossSymbolExactEntryReconciliationAuditV1CandidateRow>();
        var sampleRows = new List<CrossSymbolExactEntryReconciliationAuditV1SampleRow>();

        foreach (var leader in input.Leaderboard)
        {
            var candidateKey = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                leader.Symbol, leader.Interval, leader.Direction,
                leader.TargetPercent, leader.StopPercent, leader.ActivationRule);

            var keyMismatch = !string.Equals(candidateKey, BuildKeyFromLeader(leader), StringComparison.OrdinalIgnoreCase);
            input.FrequencyByKey.TryGetValue(candidateKey, out var frequencyRow);
            input.V2ByKey.TryGetValue(candidateKey, out var v2Row);

            if (!ActivationLookup.TryGetValue(leader.ActivationRule, out var activationConfig))
            {
                candidateRows.Add(MissingRow(leader, candidateKey, frequencyRow, "MissingInputData", "MissingActivationConfig"));
                continue;
            }

            if (!Enum.TryParse<TradingSymbol>(leader.Symbol, true, out var symbol))
            {
                candidateRows.Add(MissingRow(leader, candidateKey, frequencyRow, "MissingInputData", "InvalidSymbol"));
                continue;
            }

            if (!Enum.TryParse<LongShortDirection>(leader.Direction, true, out var direction))
            {
                candidateRows.Add(MissingRow(leader, candidateKey, frequencyRow, "MissingInputData", "InvalidDirection"));
                continue;
            }

            var comboKey = new CrossSymbolComboKey(
                symbol, leader.Interval, direction,
                leader.TargetPercent, leader.StopPercent,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes);

            if (!market.TryGetScan(comboKey, out var scan))
            {
                candidateRows.Add(MissingRow(leader, candidateKey, frequencyRow, "MissingInputData", "MissingMarketScan"));
                continue;
            }

            if (keyMismatch)
            {
                candidateRows.Add(MissingRow(leader, candidateKey, frequencyRow, "CandidateKeyMismatch", "LeaderboardKeyMismatch"));
                continue;
            }

            periodsByKey.TryGetValue(candidateKey, out var candidatePeriods);
            candidatePeriods ??= [];

            tradesByKey.TryGetValue(candidateKey, out var candidateTrades);
            candidateTrades ??= [];

            var v1TradesInWindow = candidateTrades
                .Where(t => t.EntryTimeUtc >= studyStartUtc && t.EntryTimeUtc <= studyEndUtc)
                .Where(t => string.Equals(
                    t.CostScenario,
                    NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario,
                    StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.EntryTimeUtc)
                .ToArray();

            var tradeKeyMismatch = v1TradesInWindow.Any(t =>
                !string.Equals(t.Symbol, leader.Symbol, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(t.Interval, leader.Interval, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(t.Direction, leader.Direction, StringComparison.OrdinalIgnoreCase)
                || t.TargetPercent != leader.TargetPercent
                || t.StopPercent != leader.StopPercent
                || !string.Equals(t.ActivationRule, leader.ActivationRule, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                        t.Symbol, t.Interval, t.Direction, t.TargetPercent, t.StopPercent, t.ActivationRule),
                    candidateKey,
                    StringComparison.OrdinalIgnoreCase));

            if (tradeKeyMismatch)
            {
                candidateRows.Add(MissingRow(leader, candidateKey, frequencyRow, "CandidateKeyMismatch", "TradeKeyMismatch"));
                continue;
            }

            var activatedPeriods = candidatePeriods
                .Where(p => p.Activated)
                .Where(p => p.ActivationEndUtc > studyStartUtc && p.ActivationStartUtc <= studyEndUtc)
                .ToArray();

            var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(leader.Interval);
            var intervalCandles = market.GetIntervalCandles(symbol, leader.Interval);
            var flowIndex = market.GetFlowIndex(symbol, leader.Interval);
            var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                scan.BaseTrades,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario,
                market.BtcContext,
                studyEndUtc);

            var replayedExactEntryTimes = CountExactEntriesInsideActivatedWindows(
                activationConfig,
                comboKey,
                intervalCandles,
                scan.BaseTrades,
                moderateTrades,
                flowIndex,
                studyStartUtc,
                studyEndUtc,
                cooldown);

            var frequencyExactCount = frequencyRow?.ExactEntryCountInsideActivatedWindows ?? -1;
            var v1EntryTimes = v1TradesInWindow.Select(t => t.EntryTimeUtc).ToArray();
            var tradesInsideActivation = v1TradesInWindow.Count(t => FindActivatedPeriod(t.EntryTimeUtc, activatedPeriods) is not null);
            var intervalMinutes = IntervalMinutes(leader.Interval);

            var matchedByEntryTime = v1EntryTimes.Count(t => replayedExactEntryTimes.Contains(t));
            var matchedWithinOneCandle = v1EntryTimes.Count(t =>
                replayedExactEntryTimes.Any(e => Math.Abs((e - t).TotalMinutes) <= intervalMinutes));
            var matchedWithinActivation = tradesInsideActivation;

            DateTime? firstMismatch = null;
            string mismatchType = string.Empty;
            var tradeSamples = new List<CrossSymbolExactEntryReconciliationAuditV1SampleRow>();

            foreach (var trade in v1TradesInWindow)
            {
                var period = FindActivatedPeriod(trade.EntryTimeUtc, activatedPeriods);
                var evalProbe = ProbeEvaluatorAtTrade(
                    activationConfig,
                    comboKey,
                    intervalCandles,
                    scan.BaseTrades,
                    moderateTrades,
                    flowIndex,
                    studyStartUtc,
                    cooldown,
                    trade.EntryTimeUtc,
                    period);

                if (!evalProbe.MapsToConfirmedClosedCandle)
                {
                    firstMismatch ??= trade.EntryTimeUtc;
                    mismatchType = string.IsNullOrEmpty(mismatchType) ? "TimeAlignmentMismatch" : mismatchType;
                    tradeSamples.Add(BuildSample(candidateKey, trade.EntryTimeUtc, period, evalProbe, "EntryTimeDoesNotMapToConfirmedClosedCandle"));
                    continue;
                }

                if (period is null)
                {
                    firstMismatch ??= trade.EntryTimeUtc;
                    mismatchType = string.IsNullOrEmpty(mismatchType) ? "ActivationWindowMismatch" : mismatchType;
                    tradeSamples.Add(BuildSample(candidateKey, trade.EntryTimeUtc, null, evalProbe, "V1TradeOutsideActivatedPeriod"));
                    continue;
                }

                if (!evalProbe.EntryPresentAtEntryTime)
                {
                    firstMismatch ??= trade.EntryTimeUtc;
                    if (evalProbe.EntryPresentAtAlternateEvalTime && evalProbe.ActivationPassedAtAlternateEvalTime)
                        mismatchType = string.IsNullOrEmpty(mismatchType) ? "TimeAlignmentMismatch" : mismatchType;
                    else if (evalProbe.ActivationPassedAtEntryTime)
                        mismatchType = string.IsNullOrEmpty(mismatchType) ? "EntryEvaluatorMismatch" : mismatchType;
                    else
                        mismatchType = string.IsNullOrEmpty(mismatchType) ? "ActivationWindowMismatch" : mismatchType;

                    tradeSamples.Add(BuildSample(
                        candidateKey,
                        trade.EntryTimeUtc,
                        period,
                        evalProbe,
                        evalProbe.EntryReasonAtEntryTime));
                }
            }

            var status = ResolveStatus(
                v1TradesInWindow.Length,
                frequencyExactCount,
                replayedExactEntryTimes.Count,
                matchedByEntryTime,
                tradesInsideActivation,
                mismatchType,
                frequencyRow is null,
                v2Row is null);

            candidateRows.Add(new CrossSymbolExactEntryReconciliationAuditV1CandidateRow
            {
                CandidateKey = candidateKey,
                Symbol = leader.Symbol,
                Interval = leader.Interval,
                Direction = leader.Direction,
                TargetPercent = leader.TargetPercent,
                StopPercent = leader.StopPercent,
                ActivationRule = leader.ActivationRule,
                V1TradeCount = v1TradesInWindow.Length,
                V1TradeEntryTimes = v1EntryTimes,
                V1ActivatedPeriodCount = activatedPeriods.Length,
                V1TradesInsideActivatedPeriods = tradesInsideActivation,
                FrequencyStudyExactEntryCount = Math.Max(0, frequencyExactCount),
                ReplayedExactEntryCount = replayedExactEntryTimes.Count,
                MatchedByEntryTimeCount = matchedByEntryTime,
                MatchedWithinOneCandleCount = matchedWithinOneCandle,
                MatchedWithinActivationWindowCount = matchedWithinActivation,
                FirstMismatchTimeUtc = firstMismatch,
                MismatchType = mismatchType,
                ReconciliationStatus = status,
                LeaderboardKeyPresent = true,
                V2KeyPresent = v2Row is not null,
                FrequencyStudyKeyPresent = frequencyRow is not null
            });

            sampleRows.AddRange(tradeSamples);
        }

        var mismatches = candidateRows
            .Where(c => !string.Equals(c.ReconciliationStatus, "ExactMatch", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.V1TradeCount)
            .ThenBy(c => c.CandidateKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var samples = sampleRows
            .OrderBy(s => s.V1EntryTimeUtc)
            .Take(Math.Max(CrossSymbolExactEntryReconciliationAuditV1Catalog.MinSampleMismatches, sampleRows.Count))
            .ToArray();

        var summary = BuildSummary(candidateRows, mismatches, runAtUtc, studyStartUtc, studyEndUtc);
        return new CrossSymbolExactEntryReconciliationAuditV1RunResult(summary, candidateRows, mismatches, samples);
    }

    private static CrossSymbolExactEntryReconciliationAuditV1CandidateRow MissingRow(
        CrossSymbolLeaderboardRow leader,
        string candidateKey,
        CrossCandidateExactEntryFrequencyStudyV1CandidateRow? frequencyRow,
        string status,
        string mismatchType)
        => new()
        {
            CandidateKey = candidateKey,
            Symbol = leader.Symbol,
            Interval = leader.Interval,
            Direction = leader.Direction,
            TargetPercent = leader.TargetPercent,
            StopPercent = leader.StopPercent,
            ActivationRule = leader.ActivationRule,
            V1TradeCount = leader.TradeCount,
            FrequencyStudyExactEntryCount = frequencyRow?.ExactEntryCountInsideActivatedWindows ?? 0,
            MismatchType = mismatchType,
            ReconciliationStatus = status,
            LeaderboardKeyPresent = true,
            FrequencyStudyKeyPresent = frequencyRow is not null
        };

    private static string ResolveStatus(
        int v1TradeCount,
        int frequencyExactCount,
        int replayedExactCount,
        int matchedByEntryTime,
        int tradesInsideActivation,
        string mismatchType,
        bool frequencyMissing,
        bool v2Missing)
    {
        if (frequencyMissing)
            return "MissingInputData";

        if (!string.IsNullOrEmpty(mismatchType) && mismatchType.Contains("Key", StringComparison.OrdinalIgnoreCase))
            return "CandidateKeyMismatch";

        if (v1TradeCount == 0 && frequencyExactCount == 0 && replayedExactCount == 0)
            return "ExactMatch";

        if (frequencyExactCount == replayedExactCount
            && frequencyExactCount == matchedByEntryTime
            && v1TradeCount == matchedByEntryTime)
            return "ExactMatch";

        if (v1TradeCount > 0 && frequencyExactCount == 0 && replayedExactCount == 0)
        {
            return mismatchType switch
            {
                "ActivationWindowMismatch" => "ActivationWindowMismatch",
                "EntryEvaluatorMismatch" => "EntryEvaluatorMismatch",
                "TimeAlignmentMismatch" => "TimeAlignmentMismatch",
                _ when tradesInsideActivation > 0 => "V1TradesExistButFrequencyZero",
                _ => "V1TradesExistButFrequencyZero"
            };
        }

        if (!string.IsNullOrEmpty(mismatchType))
            return mismatchType switch
            {
                "ActivationWindowMismatch" => "ActivationWindowMismatch",
                "EntryEvaluatorMismatch" => "EntryEvaluatorMismatch",
                "TimeAlignmentMismatch" => "TimeAlignmentMismatch",
                _ => "V1TradesExistButFrequencyZero"
            };

        if (frequencyExactCount != replayedExactCount)
            return "TimeAlignmentMismatch";

        return v2Missing ? "ExactMatch" : "ExactMatch";
    }

    private static CrossSymbolExactEntryReconciliationAuditV1SummaryRow BuildSummary(
        IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1CandidateRow> candidates,
        IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1CandidateRow> mismatches,
        DateTime runAtUtc,
        DateTime studyStartUtc,
        DateTime studyEndUtc)
    {
        var withV1Trades = candidates.Count(c => c.V1TradeCount > 0);
        var frequencyZero = candidates.Count(c => c.FrequencyStudyExactEntryCount == 0);
        var replayedNonZero = candidates.Count(c => c.ReplayedExactEntryCount > 0);
        var exactMatch = candidates.Count(c => c.ReconciliationStatus == "ExactMatch");
        var v1ButFreqZero = candidates.Count(c => c.ReconciliationStatus == "V1TradesExistButFrequencyZero");
        var entryEvalMismatch = candidates.Count(c => c.ReconciliationStatus == "EntryEvaluatorMismatch");
        var activationMismatch = candidates.Count(c => c.ReconciliationStatus == "ActivationWindowMismatch");
        var timeMismatch = candidates.Count(c => c.ReconciliationStatus == "TimeAlignmentMismatch");

        var statusCounts = candidates
            .GroupBy(c => c.ReconciliationStatus)
            .OrderByDescending(g => g.Count())
            .ToArray();
        var primaryRootCause = statusCounts.Length == 0
            ? "NoCandidatesEvaluated"
            : $"{statusCounts[0].Key} ({statusCounts[0].Count()} candidates)";

        var plainEnglish = BuildPlainEnglish(
            candidates,
            withV1Trades,
            frequencyZero,
            replayedNonZero,
            v1ButFreqZero,
            entryEvalMismatch,
            timeMismatch,
            primaryRootCause);

        return new CrossSymbolExactEntryReconciliationAuditV1SummaryRow
        {
            RunAtUtc = runAtUtc,
            StudyStartUtc = studyStartUtc,
            StudyEndUtc = studyEndUtc,
            EvaluatedCandidateCount = candidates.Count,
            CandidatesWithV1Trades = withV1Trades,
            CandidatesFrequencyZero = frequencyZero,
            CandidatesReplayedNonZero = replayedNonZero,
            CandidatesExactMatch = exactMatch,
            CandidatesV1TradesButFrequencyZero = v1ButFreqZero,
            CandidatesEntryEvaluatorMismatch = entryEvalMismatch,
            CandidatesActivationWindowMismatch = activationMismatch,
            CandidatesTimeAlignmentMismatch = timeMismatch,
            TotalV1TradesInWindow = candidates.Sum(c => c.V1TradeCount),
            TotalV1TradesInsideActivatedPeriods = candidates.Sum(c => c.V1TradesInsideActivatedPeriods),
            TotalReplayedExactEntries = candidates.Sum(c => c.ReplayedExactEntryCount),
            TotalFrequencyStudyExactEntries = candidates.Sum(c => c.FrequencyStudyExactEntryCount),
            PrimaryRootCause = primaryRootCause,
            CompactSummaryLine =
                $"Cross-symbol exact entry reconciliation | evaluated={candidates.Count} v1Trades={candidates.Sum(c => c.V1TradeCount)} frequencyExact={candidates.Sum(c => c.FrequencyStudyExactEntryCount)} replayedExact={candidates.Sum(c => c.ReplayedExactEntryCount)} mismatches={mismatches.Count} primary={primaryRootCause}",
            PlainEnglish = plainEnglish
        };
    }

    private static CrossSymbolExactEntryReconciliationAuditV1PlainEnglish BuildPlainEnglish(
        IReadOnlyList<CrossSymbolExactEntryReconciliationAuditV1CandidateRow> candidates,
        int withV1Trades,
        int frequencyZero,
        int replayedNonZero,
        int v1ButFreqZero,
        int entryEvalMismatch,
        int timeMismatch,
        string primaryRootCause)
    {
        var totalV1 = candidates.Sum(c => c.V1TradeCount);
        var insideActivation = candidates.Sum(c => c.V1TradesInsideActivatedPeriods);

        var areReal = totalV1 > 0
            ? $"Yes — V1 discovery recorded {totalV1} primary-scenario trades in the study window, and {insideActivation} of those fall inside V1 activated periods from cross-symbol-v1-periods.json. These are the same base directional-rule entries the cross-symbol engine attributed to activation windows, not synthetic placeholders."
            : "No V1 trades were found in-window for evaluated candidates; there is nothing to reconcile against base entries.";

        var whyZero = frequencyZero == candidates.Count && withV1Trades > 0
            ? "The frequency study walk requires EvaluateCrossSymbolActivation and EvaluateCrossSymbolEntryNow to both pass at the same candle-walk evalUtc (next candle open after a closed bar). At a V1 trade EntryTimeUtc the shadow entry evaluator typically returns OpenTradeOverlap because the base trade is already open, so Present=false even while activation is true. The study therefore undercounts entries that V1 correctly records inside activation windows."
            : frequencyZero == candidates.Count
                ? "All candidates reported zero exact entries in both the frequency study and the replayed walk."
                : $"Only {frequencyZero} of {candidates.Count} candidates reported zero frequency-study exact entries.";

        var isValid = withV1Trades > 0 && frequencyZero == candidates.Count && replayedNonZero == 0
            ? "Invalid as a 'no exact entries ever' market conclusion. Zero frequency-study counts with positive V1 trade counts indicate a measurement/alignment bug, not proof that base entries never occur under activation."
            : replayedNonZero > 0
                ? "Partially valid — replayed walk found exact entries where the frequency study may have undercounted depending on alignment."
                : "Valid only if V1 trade counts are also zero; otherwise treat zero exact-entry counts as inconclusive.";

        var shouldFix = withV1Trades > 0 && frequencyZero == candidates.Count
            ? "Yes — Cross-Candidate Exact Entry Frequency Study V1 should be fixed before using ExactEntryCountInsideActivatedWindows for promotion or watcher decisions. Reconcile entry detection with V1 period semantics (entry inside [ActivationStartUtc, ActivationEndUtc)) using confirmed closed-candle eval times, not simultaneous open-trade timestamps."
            : "Review recommended — reconciliation statuses are mixed; fix only if V1TradesExistButFrequencyZero or EntryEvaluatorMismatch dominates.";

        var alignment = entryEvalMismatch + timeMismatch + v1ButFreqZero > 0
            ? "Going forward: (1) count an exact entry when a base trade EntryTimeUtc falls inside an activated period from periods.json; (2) evaluate entry presence at evalUtc = confirmed closed candle open strictly after the signal bar (intervalCandles[i+1].OpenTimeUtc), not at EntryTimeUtc itself; (3) do not require activation and entry Present on the identical walk step when the trade is already open; (4) dedupe by EntryTimeUtc; (5) keep CandidateKey alignment across V1 trades, periods, V2, and frequency outputs."
            : "Current alignment appears consistent — continue using CandidateKey joins and confirmed closed-candle evalUtc from the frequency study walk.";

        return new CrossSymbolExactEntryReconciliationAuditV1PlainEnglish
        {
            AreV1TradesRealExactBaseEntries = areReal,
            WhyFrequencyStudyReportedZero = whyZero,
            IsZeroExactEntryResultValid = isValid,
            ShouldFrequencyStudyBeFixed = shouldFix,
            ExactFieldTimeAlignmentGoingForward = alignment
        };
    }

    private sealed record EvalProbe(
        DateTime EvaluatorCheckedTimeUtc,
        bool MapsToConfirmedClosedCandle,
        bool ActivationPassedAtEntryTime,
        bool EntryPresentAtEntryTime,
        string EntryReasonAtEntryTime,
        bool ActivationPassedAtAlternateEvalTime,
        bool EntryPresentAtAlternateEvalTime,
        string AlternateEvalReason);

    private static EvalProbe ProbeEvaluatorAtTrade(
        CrossSymbolActivationConfig activationConfig,
        CrossSymbolComboKey comboKey,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        ShortWindowFlowFeatureIndex flowIndex,
        DateTime studyStartUtc,
        int cooldownCandles,
        DateTime entryTimeUtc,
        CrossSymbolPeriodRow? period)
    {
        var evalTimes = BuildEvalTimes(intervalCandles, entryTimeUtc);
        var mapsToClosed = evalTimes.Count > 1 || FindCandleIndex(intervalCandles, entryTimeUtc) >= 0;

        var entryEvalUtc = evalTimes[0];
        var activationAtEntry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
            activationConfig, comboKey, moderateTrades, entryEvalUtc, studyStartUtc, flowIndex);
        var entryAtEntry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
            comboKey, intervalCandles, baseTrades, entryEvalUtc, studyStartUtc, cooldownCandles);

        var altPassed = false;
        var altPresent = false;
        var altReason = string.Empty;
        DateTime altEvalUtc = entryEvalUtc;

        foreach (var evalUtc in evalTimes.Skip(1))
        {
            if (period is not null && (evalUtc < period.ActivationStartUtc || evalUtc >= period.ActivationEndUtc))
                continue;

            var activation = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
                activationConfig, comboKey, moderateTrades, evalUtc, studyStartUtc, flowIndex);
            var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                comboKey, intervalCandles, baseTrades, evalUtc, studyStartUtc, cooldownCandles);

            if (activation.Passed && entry.Present)
            {
                altPassed = true;
                altPresent = true;
                altReason = entry.Reason;
                altEvalUtc = evalUtc;
                break;
            }

            if (activation.Passed && !entry.Present && string.IsNullOrEmpty(altReason))
            {
                altPassed = true;
                altReason = entry.Reason;
                altEvalUtc = evalUtc;
            }
        }

        return new EvalProbe(
            entryEvalUtc,
            mapsToClosed,
            activationAtEntry.Passed,
            entryAtEntry.Present,
            entryAtEntry.Reason,
            altPassed,
            altPresent,
            altPresent ? altReason : altReason);
    }

    private static CrossSymbolExactEntryReconciliationAuditV1SampleRow BuildSample(
        string candidateKey,
        DateTime entryTimeUtc,
        CrossSymbolPeriodRow? period,
        EvalProbe probe,
        string reason)
        => new()
        {
            CandidateKey = candidateKey,
            V1EntryTimeUtc = entryTimeUtc,
            ActivationStartUtc = period?.ActivationStartUtc,
            ActivationEndUtc = period?.ActivationEndUtc,
            EvaluatorCheckedTimeUtc = probe.EvaluatorCheckedTimeUtc,
            EvaluatorActivationPassed = probe.ActivationPassedAtEntryTime,
            EvaluatorEntryPresent = probe.EntryPresentAtEntryTime,
            Reason = reason,
            MismatchType = ClassifySampleMismatch(probe, period, reason)
        };

    private static string ClassifySampleMismatch(EvalProbe probe, CrossSymbolPeriodRow? period, string reason)
    {
        if (period is null)
            return "ActivationWindowMismatch";
        if (reason.Contains("ConfirmedClosed", StringComparison.OrdinalIgnoreCase))
            return "TimeAlignmentMismatch";
        if (reason.Contains("OpenTradeOverlap", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("CooldownActive", StringComparison.OrdinalIgnoreCase))
            return "EntryEvaluatorMismatch";
        if (probe.EntryPresentAtAlternateEvalTime)
            return "TimeAlignmentMismatch";
        if (!probe.ActivationPassedAtEntryTime)
            return "ActivationWindowMismatch";
        return "EntryEvaluatorMismatch";
    }

    private static List<DateTime> BuildEvalTimes(IReadOnlyList<KlineCandle> candles, DateTime entryTimeUtc)
    {
        var times = new List<DateTime> { entryTimeUtc };
        var idx = FindCandleIndex(candles, entryTimeUtc);
        if (idx >= 0)
        {
            if (idx + 1 < candles.Count)
                times.Add(candles[idx + 1].OpenTimeUtc);
            if (idx > 0)
                times.Add(candles[idx].OpenTimeUtc);
        }

        return times.Distinct().OrderBy(t => t).ToList();
    }

    private static CrossSymbolPeriodRow? FindActivatedPeriod(
        DateTime entryUtc,
        IReadOnlyList<CrossSymbolPeriodRow> periods)
        => periods.FirstOrDefault(p =>
            p.Activated
            && entryUtc >= p.ActivationStartUtc
            && entryUtc < p.ActivationEndUtc);

    private static List<DateTime> CountExactEntriesInsideActivatedWindows(
        CrossSymbolActivationConfig activationConfig,
        CrossSymbolComboKey comboKey,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        ShortWindowFlowFeatureIndex flowIndex,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        int cooldownCandles)
    {
        var seenEntryTimes = new HashSet<DateTime>();
        var exactEntryTimes = new List<DateTime>();

        if (intervalCandles.Count < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return exactEntryTimes;

        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i++)
        {
            var evalUtc = intervalCandles[i + 1].OpenTimeUtc;
            if (evalUtc < studyStartUtc || evalUtc > studyEndUtc)
                continue;

            var activation = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
                activationConfig,
                comboKey,
                moderateTrades,
                evalUtc,
                studyStartUtc,
                flowIndex);

            if (!activation.Passed)
                continue;

            var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                comboKey,
                intervalCandles,
                baseTrades,
                evalUtc,
                studyStartUtc,
                cooldownCandles);

            if (!entry.Present || entry.EntryTimeUtc is null)
                continue;

            if (seenEntryTimes.Add(entry.EntryTimeUtc.Value))
                exactEntryTimes.Add(entry.EntryTimeUtc.Value);
        }

        exactEntryTimes.Sort();
        return exactEntryTimes;
    }

    private static string BuildKeyFromLeader(CrossSymbolLeaderboardRow leader)
        => CrossSymbolCandidateEngineV2Catalog.CandidateKey(
            leader.Symbol, leader.Interval, leader.Direction,
            leader.TargetPercent, leader.StopPercent, leader.ActivationRule);

    private static Dictionary<string, List<CrossSymbolPeriodRow>> GroupPeriods(IReadOnlyList<CrossSymbolPeriodRow> periods)
    {
        var dict = new Dictionary<string, List<CrossSymbolPeriodRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in periods)
        {
            var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                p.Symbol, p.Interval, p.Direction, p.TargetPercent, p.StopPercent, p.ActivationRule);
            if (!dict.TryGetValue(key, out var list))
            {
                list = [];
                dict[key] = list;
            }

            list.Add(p);
        }

        return dict;
    }

    private static Dictionary<string, List<CrossSymbolTradeRow>> GroupTrades(IReadOnlyList<CrossSymbolTradeRow> trades)
    {
        var dict = new Dictionary<string, List<CrossSymbolTradeRow>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in trades)
        {
            if (!string.Equals(
                    t.CostScenario,
                    NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                t.Symbol, t.Interval, t.Direction, t.TargetPercent, t.StopPercent, t.ActivationRule);
            if (!dict.TryGetValue(key, out var list))
            {
                list = [];
                dict[key] = list;
            }

            list.Add(t);
        }

        return dict;
    }

    private static int FindCandleIndex(IReadOnlyList<KlineCandle> candles, DateTime timeUtc)
    {
        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].OpenTimeUtc <= timeUtc)
                return i;
        }

        return -1;
    }

    private static int IntervalMinutes(string interval) => interval switch
    {
        "1m" => 1,
        "3m" => 3,
        "5m" => 5,
        "15m" => 15,
        "30m" => 30,
        "1h" => 60,
        _ => 5
    };
}
