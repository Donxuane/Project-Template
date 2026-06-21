using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class EntryNearMissAuditV1Builder
{
    private const string HypotheticalRiskNoteText =
        "Diagnostic shadow only. Near-miss proximity is not forward proof and must not be traded or incubated without an actual base entry signal.";

    public static EntryNearMissAuditV1RunResult Build(
        EntryNearMissAuditV1InputBundle input,
        CurrentOpportunityScannerV1MarketContext market,
        DateTime runAtUtc)
    {
        var rows = new List<EntryNearMissAuditV1CandidateRow>();

        foreach (var candidate in input.ScannerCandidates.Where(c => c.ActivationCurrentlyPassed))
        {
            if (!Enum.TryParse<TradingSymbol>(candidate.Symbol, true, out var symbol))
                continue;
            if (!Enum.TryParse<LongShortDirection>(candidate.Direction, true, out var direction))
                continue;

            input.LeaderboardByKey.TryGetValue(candidate.CandidateKey, out var leader);
            var bottleneck = FindBottleneck(input.BottleneckAudit, candidate, leader?.SuggestedFrozenProfileName);

            var comboKey = new CrossSymbolComboKey(
                symbol, candidate.Interval, direction,
                candidate.TargetPercent, candidate.StopPercent,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes);

            if (!market.TryGetScan(comboKey, out var scan))
                continue;

            var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(candidate.Interval);
            var intervalCandles = market.GetIntervalCandles(symbol, candidate.Interval);
            var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                comboKey,
                intervalCandles,
                scan.BaseTrades,
                market.EvalUtc,
                market.StudyStartUtc,
                cooldown);

            var eval = EvaluateEntryProximity(
                candidate,
                direction,
                intervalCandles,
                scan.BaseTrades,
                market,
                symbol,
                candidate.Interval,
                entry,
                cooldown,
                bottleneck);

            rows.Add(eval);
        }

        var topNearMisses = rows
            .Where(r => r.IsTopNearMiss)
            .OrderByDescending(r => r.NearMissScore)
            .ThenByDescending(r => r.NetModerate)
            .Take(EntryNearMissAuditV1Catalog.TopNearMissLimit)
            .ToArray();

        var farMisses = rows
            .Where(r => r.NearMissClassification == "ActivationOnlyFarFromEntry")
            .OrderBy(r => r.NearMissScore)
            .ThenByDescending(r => r.FailedConditionCount)
            .Take(EntryNearMissAuditV1Catalog.FarMissLimit)
            .ToArray();

        var topNearMiss = topNearMisses.FirstOrDefault();
        var entryRarityVerdict = ResolveEntryRarityVerdict(rows, topNearMisses);
        var plainEnglish = BuildPlainEnglish(rows, topNearMisses, entryRarityVerdict);

        var summary = new EntryNearMissAuditV1SummaryRow
        {
            RunAtUtc = runAtUtc,
            EvaluatedAtUtc = market.EvalUtc,
            EvaluatedActivationPassedCount = rows.Count,
            ExactEntrySignalCount = rows.Count(r => r.NearMissClassification == "ExactEntrySignal"),
            OneConditionAwayCount = rows.Count(r => r.NearMissClassification == "OneConditionAway"),
            TwoConditionsAwayCount = rows.Count(r => r.NearMissClassification == "TwoConditionsAway"),
            PriceDistanceNearCount = rows.Count(r => r.NearMissClassification == "PriceDistanceNear"),
            FarFromEntryCount = rows.Count(r => r.NearMissClassification == "ActivationOnlyFarFromEntry"),
            ResearchWeakIgnoreCount = rows.Count(r => r.NearMissClassification == "ResearchWeakIgnore"),
            BottleneckBlockedIgnoreCount = rows.Count(r => r.NearMissClassification == "BottleneckBlockedIgnore"),
            TopNearMissCount = topNearMisses.Length,
            TopNearMissCandidate = topNearMiss?.CandidateKey ?? string.Empty,
            TopNearMissReason = topNearMiss is null
                ? string.Empty
                : $"{topNearMiss.NearMissClassification}; failed={string.Join(",", topNearMiss.FailedEntryConditions)}; distance={topNearMiss.DistanceToEntryPercent:F4}%",
            EntryRarityVerdict = entryRarityVerdict,
            TopNearMisses = topNearMisses,
            FarMisses = farMisses,
            PlainEnglish = plainEnglish,
            ScannerInputDirectory = input.ScannerInputDirectory,
            V1InputDirectory = input.V1InputDirectory,
            V2InputDirectory = input.V2InputDirectory,
            CompactSummaryLine =
                $"Entry near-miss audit | activationPassed={rows.Count} exactEntry={rows.Count(r => r.NearMissClassification == "ExactEntrySignal")} topNearMiss={topNearMisses.Length} farFromEntry={rows.Count(r => r.NearMissClassification == "ActivationOnlyFarFromEntry")} verdict={entryRarityVerdict}"
        };

        return new EntryNearMissAuditV1RunResult(summary, rows, topNearMisses, farMisses);
    }

    private static EntryNearMissAuditV1CandidateRow EvaluateEntryProximity(
        CurrentOpportunityScannerV1CandidateRow candidate,
        LongShortDirection direction,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        CurrentOpportunityScannerV1MarketContext market,
        TradingSymbol symbol,
        string interval,
        FuturesTestnetShadowEvaluator.EntryState entry,
        int cooldown,
        FrozenProfileBottleneckAuditRow? bottleneck)
    {
        var signalIndex = ResolveLatestSignalCandleIndex(intervalCandles, market.EvalUtc);
        var signalCandle = intervalCandles[signalIndex];
        var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
            intervalCandles,
            signalIndex,
            market.BtcContext,
            market.GetMarketWideContext(symbol, interval),
            signalCandle.OpenTimeUtc);

        var swingHigh = signalCandle.Close > 0m
            ? signalCandle.Close * (1m + features.DistanceFromRecentHighPercent / 100m)
            : 0m;
        var swingLow = signalCandle.Close > 0m
            ? signalCandle.Close * (1m - features.DistanceFromRecentLowPercent / 100m)
            : 0m;

        var nearExtremeThreshold = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.NearExtremeAtrMultiple * features.AtrPercent;
        var distanceToExtreme = direction == LongShortDirection.Short
            ? features.DistanceFromRecentHighPercent
            : features.DistanceFromRecentLowPercent;
        var distanceToEntry = Math.Max(0m, Math.Round(distanceToExtreme - nearExtremeThreshold, 6));
        var distanceToNearExtremeThreshold = distanceToEntry;

        var atrPassed = features.AtrPercent > 0m;
        var nearExtremePassed = direction == LongShortDirection.Short
            ? features.DistanceFromRecentHighPercent <= nearExtremeThreshold
            : features.DistanceFromRecentLowPercent <= nearExtremeThreshold;
        var elevatedVolPassed = IsElevatedVol(features);
        var candlePatternPassed = atrPassed && nearExtremePassed;
        var trendContextPassed = direction == LongShortDirection.Short
            ? features.TrendSlopePercent <= 0m || features.DistanceFromRecentHighPercent <= nearExtremeThreshold
            : features.TrendSlopePercent >= 0m || features.DistanceFromRecentLowPercent <= nearExtremeThreshold;
        var flowContextPassed = true;

        var openTradeBlocked = HasOpenTradeOverlap(baseTrades, market.EvalUtc, market.StudyStartUtc);
        var cooldownBlocked = IsCooldownActive(baseTrades, intervalCandles, market.EvalUtc, market.StudyStartUtc, cooldown);

        var required = new List<string>
        {
            "AtrPercentPositive",
            "NearExtremeDistance",
            "ElevatedVol",
            "NoOpenTradeOverlap",
            "CooldownClear"
        };
        var passed = new List<string>();
        var failed = new List<string>();

        AddCondition(required[0], atrPassed, passed, failed);
        AddCondition(required[1], nearExtremePassed, passed, failed);
        AddCondition(required[2], elevatedVolPassed, passed, failed);
        AddCondition(required[3], !openTradeBlocked, passed, failed);
        AddCondition(required[4], !cooldownBlocked, passed, failed);

        var (wouldRelax, relaxationName) = EvaluateOneConditionRelaxation(
            direction, features, failed);

        var failedCount = failed.Count;
        var nearMissScore = ComputeNearMissScore(failedCount, distanceToEntry, openTradeBlocked, cooldownBlocked, entry.Present);
        var classification = ClassifyNearMiss(
            candidate,
            entry,
            failedCount,
            distanceToEntry,
            bottleneck);

        var isTopNearMiss = candidate.ActivationCurrentlyPassed
                            && candidate.NetModerate > 0m
                            && candidate.NetStressPlus > 0m
                            && !candidate.SparseWarning
                            && !candidate.OverfitWarning
                            && !candidate.SingleClusterWarning
                            && (failedCount <= 2 || distanceToEntry <= EntryNearMissAuditV1Catalog.PriceDistanceNearThresholdPercent)
                            && classification is not ("ResearchWeakIgnore" or "BottleneckBlockedIgnore");

        return new EntryNearMissAuditV1CandidateRow
        {
            CandidateKey = candidate.CandidateKey,
            Symbol = candidate.Symbol,
            Interval = candidate.Interval,
            Direction = candidate.Direction,
            TargetPercent = candidate.TargetPercent,
            StopPercent = candidate.StopPercent,
            ActivationRule = candidate.ActivationRule,
            ResearchPromotionStatus = candidate.ResearchPromotionStatus,
            CurrentExecutionReadiness = candidate.CurrentExecutionReadiness,
            ResearchScore = candidate.ResearchScore,
            NetModerate = candidate.NetModerate,
            NetStressPlus = candidate.NetStressPlus,
            WinRate = candidate.WinRate,
            ProfitFactor = candidate.ProfitFactor,
            SparseWarning = candidate.SparseWarning,
            OverfitWarning = candidate.OverfitWarning,
            SingleClusterWarning = candidate.SingleClusterWarning,
            LatestCandleUtc = market.EvalUtc,
            ActivationCurrentlyPassed = candidate.ActivationCurrentlyPassed,
            BaseEntrySignalPresentNow = entry.Present,
            EntrySignalFailureReason = entry.Present ? string.Empty : entry.Reason,
            NearMissScore = nearMissScore,
            NearMissClassification = classification,
            DistanceToEntryPercent = distanceToEntry,
            DistanceToNearExtremeThresholdPercent = distanceToNearExtremeThreshold,
            LatestClose = signalCandle.Close,
            RecentHigh = Math.Round(swingHigh, 8),
            RecentLow = Math.Round(swingLow, 8),
            DistanceToRecentHighPercent = features.DistanceFromRecentHighPercent,
            DistanceToRecentLowPercent = features.DistanceFromRecentLowPercent,
            VolatilityState = features.VolatilityRegime,
            ElevatedVolPassed = elevatedVolPassed,
            TrendContextPassed = trendContextPassed,
            FlowContextPassed = flowContextPassed,
            CandlePatternPassed = candlePatternPassed,
            RequiredEntryConditions = required,
            PassedEntryConditions = passed,
            FailedEntryConditions = failed,
            FailedConditionCount = failedCount,
            WouldBecomeEntryIfOneConditionRelaxed = wouldRelax,
            OneConditionRelaxationName = relaxationName,
            HypotheticalSignalDirection = candidate.Direction,
            HypotheticalRiskNote = HypotheticalRiskNoteText,
            IsTopNearMiss = isTopNearMiss
        };
    }

    private static void AddCondition(string name, bool pass, List<string> passed, List<string> failed)
    {
        if (pass)
            passed.Add(name);
        else
            failed.Add(name);
    }

    private static bool IsElevatedVol(MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features)
        => features.VolatilityRegime == "Elevated"
           || (features.VolatilityRegime == "Normal"
               && features.VolumeExpansionRatio >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.VolumeExpansionMin);

    private static (bool WouldRelax, string RelaxationName) EvaluateOneConditionRelaxation(
        LongShortDirection direction,
        MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features,
        IReadOnlyList<string> failed)
    {
        foreach (var condition in failed)
        {
            if (condition is "NoOpenTradeOverlap" or "CooldownClear")
                continue;

            var relaxed = condition switch
            {
                "AtrPercentPositive" => EvaluatePatternWithoutAtr(direction, features),
                "NearExtremeDistance" => EvaluatePatternWithoutNearExtreme(direction, features),
                "ElevatedVol" => EvaluatePatternWithoutElevatedVol(direction, features),
                _ => false
            };

            if (relaxed)
                return (true, condition);
        }

        return (false, string.Empty);
    }

    private static bool EvaluatePatternWithoutAtr(LongShortDirection direction, MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features)
    {
        var threshold = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.NearExtremeAtrMultiple * Math.Max(features.AtrPercent, 0.01m);
        var near = direction == LongShortDirection.Short
            ? features.DistanceFromRecentHighPercent <= threshold
            : features.DistanceFromRecentLowPercent <= threshold;
        return near && IsElevatedVol(features);
    }

    private static bool EvaluatePatternWithoutNearExtreme(LongShortDirection direction, MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features)
        => features.AtrPercent > 0m && IsElevatedVol(features);

    private static bool EvaluatePatternWithoutElevatedVol(LongShortDirection direction, MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features)
    {
        if (features.AtrPercent <= 0m)
            return false;
        var threshold = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.NearExtremeAtrMultiple * features.AtrPercent;
        return direction == LongShortDirection.Short
            ? features.DistanceFromRecentHighPercent <= threshold
            : features.DistanceFromRecentLowPercent <= threshold;
    }

    private static decimal ComputeNearMissScore(
        int failedCount,
        decimal distanceToEntry,
        bool openTradeBlocked,
        bool cooldownBlocked,
        bool entryPresent)
    {
        if (entryPresent)
            return 100m;

        var score = 100m - failedCount * 20m;
        score -= Math.Min(30m, distanceToEntry * 40m);
        if (openTradeBlocked)
            score -= 10m;
        if (cooldownBlocked)
            score -= 10m;
        return Math.Round(Math.Max(0m, Math.Min(100m, score)), 4);
    }

    private static string ClassifyNearMiss(
        CurrentOpportunityScannerV1CandidateRow candidate,
        FuturesTestnetShadowEvaluator.EntryState entry,
        int failedCount,
        decimal distanceToEntry,
        FrozenProfileBottleneckAuditRow? bottleneck)
    {
        if (bottleneck is not null
            && string.Equals(bottleneck.Recommendation, "Park", StringComparison.OrdinalIgnoreCase)
            && MatchesBottleneckScope(candidate, bottleneck))
        {
            return "BottleneckBlockedIgnore";
        }

        if (candidate.NetStressPlus <= 0m)
            return "ResearchWeakIgnore";

        if (entry.Present)
            return "ExactEntrySignal";

        if (failedCount == 1)
            return "OneConditionAway";

        if (failedCount == 2)
            return "TwoConditionsAway";

        if (distanceToEntry <= EntryNearMissAuditV1Catalog.PriceDistanceNearThresholdPercent)
            return "PriceDistanceNear";

        return "ActivationOnlyFarFromEntry";
    }

    private static string ResolveEntryRarityVerdict(
        IReadOnlyList<EntryNearMissAuditV1CandidateRow> rows,
        IReadOnlyList<EntryNearMissAuditV1CandidateRow> topNearMisses)
    {
        if (rows.Count == 0)
            return "NoUsableNearMisses";

        var farCount = rows.Count(r => r.NearMissClassification == "ActivationOnlyFarFromEntry");
        var nearCount = rows.Count(r => r.NearMissClassification is "ExactEntrySignal" or "OneConditionAway" or "TwoConditionsAway" or "PriceDistanceNear");

        if (topNearMisses.Count == 0 && farCount > rows.Count * 0.6)
            return "EntrySignalStructurallyRare";

        if (topNearMisses.Count == 0)
            return "NoUsableNearMisses";

        if (farCount >= nearCount && farCount > rows.Count / 2)
            return "Mixed";

        if (nearCount > 0 && farCount <= nearCount)
            return "EntrySignalCurrentlyAbsentButNear";

        return "Mixed";
    }

    private static EntryNearMissAuditV1PlainEnglish BuildPlainEnglish(
        IReadOnlyList<EntryNearMissAuditV1CandidateRow> rows,
        IReadOnlyList<EntryNearMissAuditV1CandidateRow> topNearMisses,
        string entryRarityVerdict)
    {
        var failureGroups = rows
            .SelectMany(r => r.FailedEntryConditions)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key}={g.Count()}")
            .ToArray();

        var whyNotEntering = rows.Count == 0
            ? "No activation-passed candidates were available to audit."
            : $"All {rows.Count} activation-passed candidates lack a current base entry signal. Top blockers: {string.Join(", ", failureGroups)}. Operational misses (overlap/cooldown) and pattern misses (near-extreme distance, elevated vol) both contribute.";

        var watchAggressively = topNearMisses.Count == 0
            ? "No candidates meet top-near-miss quality filters. Do not watch aggressively based on near-miss logic alone."
            : $"{topNearMisses.Count} candidate(s) are relatively close on entry conditions, led by {topNearMisses[0].CandidateKey} (score={topNearMisses[0].NearMissScore:F1}, {topNearMisses[0].NearMissClassification}). This is diagnostic proximity only — not a trade recommendation.";

        var solRow = rows.FirstOrDefault(r =>
            string.Equals(r.Symbol, "SOLUSDT", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Interval, "30m", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Direction, "Short", StringComparison.OrdinalIgnoreCase));

        var solProximity = solRow is null
            ? "SOLUSDT 30m Short was not among activation-passed candidates in this audit."
            : solRow.NearMissClassification is "OneConditionAway" or "TwoConditionsAway" or "PriceDistanceNear" or "ExactEntrySignal"
                ? $"SOLUSDT 30m Short is close: {solRow.NearMissClassification}, distanceToEntry={solRow.DistanceToEntryPercent:F4}%, failed={string.Join(",", solRow.FailedEntryConditions)}."
                : $"SOLUSDT 30m Short is far from entry: {solRow.NearMissClassification}, distanceToEntry={solRow.DistanceToEntryPercent:F4}%, failed={string.Join(",", solRow.FailedEntryConditions)}.";

        var incubation = entryRarityVerdict == "EntrySignalStructurallyRare"
            ? "Do not create a new incubation candidate from near-miss logic. Entry conditions appear structurally rare relative to activation."
            : topNearMisses.Count > 0
                ? "Do not create incubation from near-miss logic alone. Wait for an actual base entry signal even if a candidate is close."
                : "Do not create incubation from near-miss logic. No usable near-misses passed research-quality filters.";

        var waitForSignal = rows.Any(r => r.BaseEntrySignalPresentNow)
            ? "Some activation-passed candidates already have entry signals in diagnostic mode, but shadow trading remains disabled."
            : "Wait for an actual base entry signal. Activation alone is insufficient and near-miss proximity is not forward proof.";

        return new EntryNearMissAuditV1PlainEnglish
        {
            WhyActivatedCandidatesNotEntering = whyNotEntering,
            AreAnyCloseEnoughToWatchAggressively = watchAggressively,
            SolUsdt30mShortProximity = solProximity,
            ShouldCreateIncubationFromNearMiss = incubation,
            ShouldWaitForActualEntrySignal = waitForSignal
        };
    }

    private static int ResolveLatestSignalCandleIndex(IReadOnlyList<KlineCandle> intervalCandles, DateTime evalUtc)
    {
        var maxIndex = intervalCandles.Count - 2;
        if (maxIndex < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles)
            return Math.Max(0, maxIndex);

        for (var i = maxIndex; i >= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i--)
        {
            if (intervalCandles[i + 1].OpenTimeUtc <= evalUtc)
                return i;
        }

        return Math.Max(MarketRegimeForwardEdgeScanner.MinimumWarmupCandles, maxIndex);
    }

    private static bool HasOpenTradeOverlap(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        DateTime evalUtc,
        DateTime frozenStartUtc)
    {
        return baseTrades.Any(t =>
            t.TimeUtc <= evalUtc
            && t.TimeUtc.AddMinutes((double)t.DurationMinutes) > evalUtc
            && t.TimeUtc >= frozenStartUtc);
    }

    private static bool IsCooldownActive(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<KlineCandle> intervalCandles,
        DateTime evalUtc,
        DateTime frozenStartUtc,
        int cooldownCandles)
    {
        var lastExit = baseTrades
            .Where(t => t.TimeUtc.AddMinutes((double)t.DurationMinutes) <= evalUtc && t.TimeUtc >= frozenStartUtc)
            .OrderByDescending(t => t.TimeUtc.AddMinutes((double)t.DurationMinutes))
            .FirstOrDefault();
        if (lastExit is null)
            return false;

        var exitUtc = lastExit.TimeUtc.AddMinutes((double)lastExit.DurationMinutes);
        var exitIdx = FindCandleIndex(intervalCandles, exitUtc);
        var nextAllowed = exitIdx + 1 + cooldownCandles;
        var latestIdx = ResolveLatestSignalCandleIndex(intervalCandles, evalUtc);
        return latestIdx < nextAllowed;
    }

    private static int FindCandleIndex(IReadOnlyList<KlineCandle> candles, DateTime timeUtc)
    {
        var lo = 0;
        var hi = candles.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (candles[mid].OpenTimeUtc > timeUtc)
                hi = mid;
            else
                lo = mid + 1;
        }

        return Math.Max(0, lo - 1);
    }

    private static FrozenProfileBottleneckAuditRow? FindBottleneck(
        IReadOnlyList<FrozenProfileBottleneckAuditRow> audit,
        CurrentOpportunityScannerV1CandidateRow candidate,
        string? suggestedProfileName)
    {
        if (!string.IsNullOrWhiteSpace(suggestedProfileName))
        {
            var byName = audit.FirstOrDefault(a =>
                string.Equals(a.ProfileName, suggestedProfileName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        return audit.FirstOrDefault(a => MatchesBottleneckScope(candidate, a));
    }

    private static bool MatchesBottleneckScope(
        CurrentOpportunityScannerV1CandidateRow candidate,
        FrozenProfileBottleneckAuditRow audit)
        => string.Equals(audit.Symbol, candidate.Symbol, StringComparison.OrdinalIgnoreCase)
           && string.Equals(audit.Interval, candidate.Interval, StringComparison.OrdinalIgnoreCase)
           && string.Equals(audit.Direction, candidate.Direction, StringComparison.OrdinalIgnoreCase);
}
