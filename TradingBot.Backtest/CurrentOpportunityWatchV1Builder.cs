namespace TradingBot.Backtest;

public static class CurrentOpportunityWatchV1Builder
{
    private static readonly HashSet<string> BlockedReadinessValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "LookbackStarvedCurrent",
        "ActivationCurrentlyBlocked",
        "StressNegativeForward",
        "SafetyBlocked",
        "NotExecutable"
    };

    public static CurrentOpportunityWatchV1RunResult Build(
        CurrentOpportunityScannerV1RunResult scanner,
        EntryNearMissAuditV1RunResult audit,
        IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> frequencyCandidates,
        IReadOnlyList<FrozenProfileBottleneckAuditRow> bottleneckAudit,
        bool countingBugFixed,
        IReadOnlyList<CurrentOpportunityWatchV1HistoryRow> priorHistory,
        DateTime runAtUtc,
        DateTime dataLastCandleUtc,
        bool evalAdvancedSincePreviousCycle,
        int cycleNumber)
    {
        var auditByKey = audit.Candidates.ToDictionary(c => c.CandidateKey, StringComparer.OrdinalIgnoreCase);
        var scannerByKey = scanner.Candidates.ToDictionary(c => c.CandidateKey, StringComparer.OrdinalIgnoreCase);
        var bottleneckByScope = BuildBottleneckByScope(bottleneckAudit);

        // Fixed-frequency study promoted candidates become first-class exact-entry watch targets.
        var promoted = frequencyCandidates
            .Where(f => string.Equals(
                f.Recommendation,
                CurrentOpportunityWatchV1Catalog.PromoteToExactEntryWatcherRecommendation,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var fixedFrequencyRows = BuildFixedFrequencyRows(promoted, scannerByKey, bottleneckByScope);
        var fixedByKey = fixedFrequencyRows.ToDictionary(f => f.CandidateKey, StringComparer.OrdinalIgnoreCase);

        var watchlistItems = BuildWatchlistItems(scanner, auditByKey, fixedFrequencyRows, scannerByKey);
        var ranked = RankWatchlist(watchlistItems);
        var exactEntrySignals = ranked.Where(r => r.BaseEntrySignalPresentNow && r.ActivationCurrentlyPassed).ToArray();
        var nearMisses = ranked.Where(r => r.WatchKind == "NearMiss").ToArray();
        var topWatchlist = ranked
            .Take(CurrentOpportunityWatchV1Catalog.TopWatchlistLimit)
            .Select((row, index) => row with { WatchRank = index + 1 })
            .ToArray();

        var top = topWatchlist.FirstOrDefault();
        var actionable = scanner.ActionableShadow
            .OrderByDescending(c => c.ResearchScore)
            .ThenByDescending(c => c.NetStressPlus)
            .FirstOrDefault();
        var exactEntryCandidate = actionable?.CandidateKey
                                  ?? exactEntrySignals.FirstOrDefault()?.CandidateKey
                                  ?? string.Empty;

        // Rule 6: a promoted fixed-frequency candidate with a current exact entry (activation + base entry).
        var fixedFrequencyExactEntry = fixedFrequencyRows
            .FirstOrDefault(f => f.ActivationCurrentlyPassed && f.BaseEntrySignalPresentNow);
        var fixedFrequencyExactEntryPresent = fixedFrequencyExactEntry is not null;

        var closestFixed = fixedFrequencyRows.FirstOrDefault();
        var blockedByReadiness = fixedFrequencyRows.Count(f =>
            f.WatchReason == CurrentOpportunityWatchV1Catalog.WatchReasonReadinessBlocked);
        var needsIncubation = fixedFrequencyRows.Count(f =>
            f.WatchReason == CurrentOpportunityWatchV1Catalog.WatchReasonNeedsIncubation);

        var watchStatus = ResolveWatchStatus(
            scanner.Summary.ActionableShadowCount,
            scanner.Summary.BaseEntrySignalPresentCount,
            audit.Summary.TopNearMissCount,
            fixedFrequencyExactEntryPresent);
        var exactNote = watchStatus == "ExactEntrySignalAppeared"
            ? scanner.Summary.ActionableShadowCount > 0 || fixedFrequencyExactEntryPresent
                ? CurrentOpportunityWatchV1Catalog.ExactEntryAppearedMessage
                : "Exact base entry signal on confirmed closed candles. Shadow not yet actionable — review before any testnet action."
            : string.Empty;

        var plainEnglish = BuildPlainEnglish(
            watchStatus,
            top,
            exactEntryCandidate,
            nearMisses.Length > 0,
            fixedFrequencyRows,
            fixedFrequencyExactEntry,
            closestFixed,
            blockedByReadiness,
            countingBugFixed);

        var eth15WatchRow = fixedFrequencyRows.FirstOrDefault(r =>
            r.Symbol.Contains("ETH", StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.Interval, "15m", StringComparison.OrdinalIgnoreCase));
        var statusRisk = eth15WatchRow?.NormalizedRisk
                         ?? closestFixed?.NormalizedRisk
                         ?? new NormalizedRiskPnlMetrics();

        var status = new CurrentOpportunityWatchV1StatusRow
        {
            RunAtUtc = runAtUtc,
            EvaluatedAtUtc = scanner.Summary.EvaluatedAtUtc,
            WatchStatus = watchStatus,
            EvaluatedCandidateCount = scanner.Summary.EvaluatedCandidateCount,
            ActivationPassedCount = scanner.Summary.ActivationPassedCount,
            EntrySignalPresentCount = scanner.Summary.BaseEntrySignalPresentCount,
            ActionableShadowCount = scanner.Summary.ActionableShadowCount,
            TopNearMissCount = audit.Summary.TopNearMissCount,
            TopWatchCandidate = top?.CandidateKey ?? string.Empty,
            TopWatchSymbol = top?.Symbol ?? string.Empty,
            TopWatchInterval = top?.Interval ?? string.Empty,
            TopWatchDirection = top?.Direction ?? string.Empty,
            TopWatchActivationRule = top?.ActivationRule ?? string.Empty,
            TopWatchDistanceToEntryPercent = top?.DistanceToEntryPercent ?? 0m,
            TopWatchFailedCondition = top?.FailedCondition ?? string.Empty,
            ExactEntrySignalCandidate = exactEntryCandidate,
            ExactEntryAppearedNote = exactNote,
            DataLastCandleUtc = dataLastCandleUtc,
            EvalAdvancedSincePreviousCycle = evalAdvancedSincePreviousCycle,
            CycleNumber = cycleNumber,
            UsesConfirmedClosedCandlesOnly = true,
            WouldPlaceOrder = false,
            RealOrdersPlaced = false,
            LiveFuturesRecommended = false,
            FixedFrequencyPromotedCount = promoted.Length,
            FixedFrequencyWatchedCount = fixedFrequencyRows.Count,
            FixedFrequencyBlockedByReadinessCount = blockedByReadiness,
            FixedFrequencyNeedsIncubationCount = needsIncubation,
            FixedFrequencyExactEntryPresent = fixedFrequencyExactEntryPresent,
            FixedFrequencyExactEntryCandidate = fixedFrequencyExactEntry?.CandidateKey ?? string.Empty,
            ClosestFixedFrequencyCandidate = closestFixed?.CandidateKey ?? string.Empty,
            ClosestFixedFrequencyWatchReason = closestFixed?.WatchReason ?? string.Empty,
            CountingBugFixed = countingBugFixed,
            CanEnterTestnetOrderMode = false,
            NormalizedRisk = statusRisk,
            CompactSummaryLine =
                $"WatchStatus={watchStatus} evaluated={scanner.Summary.EvaluatedCandidateCount} activationPassed={scanner.Summary.ActivationPassedCount} entryPresent={scanner.Summary.BaseEntrySignalPresentCount} actionable={scanner.Summary.ActionableShadowCount} topNearMiss={audit.Summary.TopNearMissCount} fixedFreqWatched={fixedFrequencyRows.Count} fixedFreqExactEntry={fixedFrequencyExactEntryPresent} canEnterTestnet=false | Normalized Est. (per $100 at 3x): {statusRisk.NetPnlPer100UsdtAt3x:F2}",
            PlainEnglish = plainEnglish
        };

        var historyRow = new CurrentOpportunityWatchV1HistoryRow
        {
            RunAtUtc = runAtUtc,
            EvaluatedAtUtc = scanner.Summary.EvaluatedAtUtc,
            WatchStatus = watchStatus,
            EvaluatedCandidateCount = status.EvaluatedCandidateCount,
            ActivationPassedCount = status.ActivationPassedCount,
            EntrySignalPresentCount = status.EntrySignalPresentCount,
            ActionableShadowCount = status.ActionableShadowCount,
            TopNearMissCount = status.TopNearMissCount,
            TopWatchCandidate = status.TopWatchCandidate,
            ExactEntrySignalCandidate = status.ExactEntrySignalCandidate
        };

        var history = CurrentOpportunityWatchV1HistoryStore.Append(priorHistory, historyRow);
        return new CurrentOpportunityWatchV1RunResult(
            status, history, topWatchlist, exactEntrySignals, nearMisses, fixedFrequencyRows);
    }

    private static string ResolveWatchStatus(
        int actionableShadowCount,
        int entrySignalPresentCount,
        int topNearMissCount,
        bool fixedFrequencyExactEntryPresent)
    {
        if (actionableShadowCount > 0 || entrySignalPresentCount > 0 || fixedFrequencyExactEntryPresent)
            return "ExactEntrySignalAppeared";

        if (topNearMissCount > 0)
            return "NearMissOnly";

        return "NoCurrentOpportunity";
    }

    private static Dictionary<string, FrozenProfileBottleneckAuditRow> BuildBottleneckByScope(
        IReadOnlyList<FrozenProfileBottleneckAuditRow> bottleneckAudit)
    {
        var dict = new Dictionary<string, FrozenProfileBottleneckAuditRow>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in bottleneckAudit)
        {
            var scope = CurrentOpportunityScannerV1Loader.ScopeKey(row.Symbol, row.Interval, row.Direction);
            // Prefer the most informative classification when multiple profiles share a scope.
            if (!dict.ContainsKey(scope) || !string.IsNullOrEmpty(row.BottleneckClassification))
                dict[scope] = row;
        }

        return dict;
    }

    private static List<CurrentOpportunityWatchV1FixedFrequencyRow> BuildFixedFrequencyRows(
        IReadOnlyList<CrossCandidateExactEntryFrequencyStudyV1CandidateRow> promoted,
        IReadOnlyDictionary<string, CurrentOpportunityScannerV1CandidateRow> scannerByKey,
        IReadOnlyDictionary<string, FrozenProfileBottleneckAuditRow> bottleneckByScope)
    {
        var rows = new List<CurrentOpportunityWatchV1FixedFrequencyRow>();

        foreach (var f in promoted)
        {
            scannerByKey.TryGetValue(f.CandidateKey, out var scannerRow);
            var scope = CurrentOpportunityScannerV1Loader.ScopeKey(f.Symbol, f.Interval, f.Direction);
            bottleneckByScope.TryGetValue(scope, out var bottleneck);

            var activationPassed = scannerRow?.ActivationCurrentlyPassed ?? f.ActivationCurrentlyPassed ?? false;
            var entryPresent = scannerRow?.BaseEntrySignalPresentNow ?? f.BaseEntrySignalPresentNow ?? false;
            var actionableShadow = scannerRow?.WouldBeShadowActionable ?? false;
            var readiness = scannerRow?.CurrentExecutionReadiness ?? string.Empty;
            var researchPromotion = scannerRow?.ResearchPromotionStatus ?? string.Empty;
            var bottleneckClass = bottleneck?.BottleneckClassification ?? string.Empty;
            var bottleneckRec = bottleneck?.Recommendation ?? string.Empty;

            var hasCurrentExactEntry = activationPassed && entryPresent;
            var readinessBlocked = IsReadinessBlocked(readiness, bottleneckClass, bottleneckRec);
            var incubating = !hasCurrentExactEntry && !readinessBlocked && NeedsForwardIncubation(readiness, researchPromotion);

            var (watchReason, tier) = ResolveFixedFrequencyWatchReason(hasCurrentExactEntry, readinessBlocked, incubating);

            // Diagnostic shadow only: testnet/live order mode stays disabled regardless of historical frequency.
            const bool canEnterTestnetOrderMode = false;

            rows.Add(new CurrentOpportunityWatchV1FixedFrequencyRow
            {
                CandidateKey = f.CandidateKey,
                Symbol = f.Symbol,
                Interval = f.Interval,
                Direction = f.Direction,
                TargetPercent = f.TargetPercent,
                StopPercent = f.StopPercent,
                ActivationRule = f.ActivationRule,
                FixedFrequencyRecommendation = f.Recommendation,
                ExactEntryCountInsideActivatedWindows = f.ExactEntryCountInsideActivatedWindows,
                ExactEntriesPerDay = f.ExactEntriesPerDay,
                LastExactEntryUtc = f.LastExactEntryUtc,
                DaysSinceLastExactEntry = f.DaysSinceLastExactEntry,
                NetModerate = f.NetModerate,
                NetStressPlus = f.NetStressPlus,
                WinRate = f.WinRate,
                ProfitFactor = f.ProfitFactor,
                ResearchPromotionStatus = researchPromotion,
                CurrentExecutionReadiness = readiness,
                CurrentBottleneckClassification = bottleneckClass,
                CurrentBottleneckRecommendation = bottleneckRec,
                ActivationCurrentlyPassed = activationPassed,
                BaseEntrySignalPresentNow = entryPresent,
                ActionableShadow = actionableShadow,
                WatchPriorityTier = tier,
                WatchReason = watchReason,
                WatchStatus = hasCurrentExactEntry ? "ExactEntrySignalAppeared" : "WatchingForExactEntry",
                WouldPlaceOrder = false,
                CanEnterTestnetOrderMode = canEnterTestnetOrderMode,
                NormalizedRisk = NormalizedRiskPnlModule.Compute(f.Symbol, f.NetModerate)
            });
        }

        // Highest watch priority: current exact entry, then ready-and-waiting, then incubating, then blocked.
        // Within a tier, stronger historical frequency and stress quality rank first.
        var ordered = rows
            .OrderByDescending(r => r.ActivationCurrentlyPassed && r.BaseEntrySignalPresentNow)
            .ThenByDescending(r => r.WatchReason != CurrentOpportunityWatchV1Catalog.WatchReasonReadinessBlocked)
            .ThenByDescending(r => r.WatchReason != CurrentOpportunityWatchV1Catalog.WatchReasonNeedsIncubation)
            .ThenByDescending(r => r.ExactEntriesPerDay)
            .ThenByDescending(r => r.NetStressPlus)
            .ToList();

        return ordered
            .Select((row, index) => row with { WatchPriority = index + 1 })
            .ToList();
    }

    private static bool IsReadinessBlocked(string readiness, string bottleneckClassification, string bottleneckRecommendation)
        => BlockedReadinessValues.Contains(readiness)
           || string.Equals(readiness, "LookbackStarvedCurrent", StringComparison.OrdinalIgnoreCase)
           || string.Equals(bottleneckRecommendation, "Park", StringComparison.OrdinalIgnoreCase)
           || string.Equals(bottleneckClassification, "LookbackStarved", StringComparison.OrdinalIgnoreCase);

    private static bool NeedsForwardIncubation(string readiness, string researchPromotion)
        => string.Equals(readiness, "ForwardEvidencePending", StringComparison.OrdinalIgnoreCase)
           || (!string.IsNullOrEmpty(researchPromotion)
               && !researchPromotion.Contains("Frozen", StringComparison.OrdinalIgnoreCase)
               && !researchPromotion.Contains("Promoted", StringComparison.OrdinalIgnoreCase));

    private static (string WatchReason, string Tier) ResolveFixedFrequencyWatchReason(
        bool hasCurrentExactEntry,
        bool readinessBlocked,
        bool incubating)
    {
        if (hasCurrentExactEntry)
            return (CurrentOpportunityWatchV1Catalog.WatchReasonCurrentExactEntry, "CurrentExactEntry");
        if (readinessBlocked)
            return (CurrentOpportunityWatchV1Catalog.WatchReasonReadinessBlocked, "ReadinessBlocked");
        if (incubating)
            return (CurrentOpportunityWatchV1Catalog.WatchReasonNeedsIncubation, "NeedsForwardIncubation");
        return (CurrentOpportunityWatchV1Catalog.WatchReasonAwaitingEntry, "ReadyAwaitingEntry");
    }

    private static List<CurrentOpportunityWatchV1WatchlistRow> BuildWatchlistItems(
        CurrentOpportunityScannerV1RunResult scanner,
        IReadOnlyDictionary<string, EntryNearMissAuditV1CandidateRow> auditByKey,
        IReadOnlyList<CurrentOpportunityWatchV1FixedFrequencyRow> fixedFrequencyRows,
        IReadOnlyDictionary<string, CurrentOpportunityScannerV1CandidateRow> scannerByKey)
    {
        var items = new List<CurrentOpportunityWatchV1WatchlistRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Fixed-frequency promoted candidates always go on the watchlist, above near-misses.
        foreach (var ff in fixedFrequencyRows)
        {
            scannerByKey.TryGetValue(ff.CandidateKey, out var scannerRow);
            auditByKey.TryGetValue(ff.CandidateKey, out var auditRow);
            items.Add(FixedFrequencyToWatchlistRow(ff, scannerRow, auditRow));
            seen.Add(ff.CandidateKey);
        }

        // 2) Current exact-entry scanner candidates not already promoted.
        foreach (var candidate in scanner.Candidates.Where(c => c.BaseEntrySignalPresentNow && c.ActivationCurrentlyPassed))
        {
            if (seen.Contains(candidate.CandidateKey))
                continue;
            auditByKey.TryGetValue(candidate.CandidateKey, out var auditRow);
            items.Add(ToWatchlistRow(candidate, auditRow, "ExactEntrySignal"));
            seen.Add(candidate.CandidateKey);
        }

        // 3) Top near-misses not already covered.
        foreach (var auditRow in auditByKey.Values.Where(a => a.IsTopNearMiss))
        {
            if (seen.Contains(auditRow.CandidateKey))
                continue;

            var scannerRow = scanner.Candidates.FirstOrDefault(c =>
                string.Equals(c.CandidateKey, auditRow.CandidateKey, StringComparison.OrdinalIgnoreCase));
            if (scannerRow is null)
                continue;

            items.Add(ToWatchlistRow(scannerRow, auditRow, "NearMiss"));
            seen.Add(auditRow.CandidateKey);
        }

        return items;
    }

    private static CurrentOpportunityWatchV1WatchlistRow FixedFrequencyToWatchlistRow(
        CurrentOpportunityWatchV1FixedFrequencyRow ff,
        CurrentOpportunityScannerV1CandidateRow? scanner,
        EntryNearMissAuditV1CandidateRow? audit)
        => new()
        {
            WatchKind = CurrentOpportunityWatchV1Catalog.FixedFrequencyWatchKind,
            CandidateKey = ff.CandidateKey,
            Symbol = ff.Symbol,
            Interval = ff.Interval,
            Direction = ff.Direction,
            TargetPercent = ff.TargetPercent,
            StopPercent = ff.StopPercent,
            ActivationRule = ff.ActivationRule,
            ResearchScore = scanner?.ResearchScore ?? 0m,
            NetModerate = ff.NetModerate,
            NetStressPlus = ff.NetStressPlus,
            ActivationCurrentlyPassed = ff.ActivationCurrentlyPassed,
            BaseEntrySignalPresentNow = ff.BaseEntrySignalPresentNow,
            WouldBeShadowActionable = ff.ActionableShadow,
            NearMissClassification = audit?.NearMissClassification ?? string.Empty,
            DistanceToEntryPercent = audit?.DistanceToEntryPercent ?? 0m,
            FailedCondition = audit?.FailedEntryConditions.FirstOrDefault() ?? string.Empty,
            SparseWarning = scanner?.SparseWarning ?? false,
            OverfitWarning = scanner?.OverfitWarning ?? false,
            SingleClusterWarning = scanner?.SingleClusterWarning ?? false,
            IsSolUsdt30mShort = scanner is not null && IsSolUsdt30mShort(scanner),
            IsFixedFrequencyPromoted = true,
            FixedFrequencyRecommendation = ff.FixedFrequencyRecommendation,
            ExactEntryCountInsideActivatedWindows = ff.ExactEntryCountInsideActivatedWindows,
            ExactEntriesPerDay = ff.ExactEntriesPerDay,
            LastExactEntryUtc = ff.LastExactEntryUtc,
            DaysSinceLastExactEntry = ff.DaysSinceLastExactEntry,
            WatchReason = ff.WatchReason
        };

    private static CurrentOpportunityWatchV1WatchlistRow ToWatchlistRow(
        CurrentOpportunityScannerV1CandidateRow scanner,
        EntryNearMissAuditV1CandidateRow? audit,
        string watchKind)
        => new()
        {
            WatchKind = watchKind,
            CandidateKey = scanner.CandidateKey,
            Symbol = scanner.Symbol,
            Interval = scanner.Interval,
            Direction = scanner.Direction,
            TargetPercent = scanner.TargetPercent,
            StopPercent = scanner.StopPercent,
            ActivationRule = scanner.ActivationRule,
            ResearchScore = scanner.ResearchScore,
            NetModerate = scanner.NetModerate,
            NetStressPlus = scanner.NetStressPlus,
            ActivationCurrentlyPassed = scanner.ActivationCurrentlyPassed,
            BaseEntrySignalPresentNow = scanner.BaseEntrySignalPresentNow,
            WouldBeShadowActionable = scanner.WouldBeShadowActionable,
            NearMissClassification = audit?.NearMissClassification ?? string.Empty,
            DistanceToEntryPercent = audit?.DistanceToEntryPercent ?? 0m,
            FailedCondition = audit?.FailedEntryConditions.FirstOrDefault() ?? string.Empty,
            SparseWarning = scanner.SparseWarning,
            OverfitWarning = scanner.OverfitWarning,
            SingleClusterWarning = scanner.SingleClusterWarning,
            IsSolUsdt30mShort = IsSolUsdt30mShort(scanner),
            WatchReason = string.Empty
        };

    private static bool IsSolUsdt30mShort(CurrentOpportunityScannerV1CandidateRow candidate)
        => string.Equals(candidate.Symbol, CurrentOpportunityWatchV1Catalog.SolUsdt30mShortSymbol, StringComparison.OrdinalIgnoreCase)
           && string.Equals(candidate.Interval, CurrentOpportunityWatchV1Catalog.SolUsdt30mShortInterval, StringComparison.OrdinalIgnoreCase)
           && string.Equals(candidate.Direction, CurrentOpportunityWatchV1Catalog.SolUsdt30mShortDirection, StringComparison.OrdinalIgnoreCase);

    private static List<CurrentOpportunityWatchV1WatchlistRow> RankWatchlist(
        IReadOnlyList<CurrentOpportunityWatchV1WatchlistRow> items)
        => items
            // Rule 3: near-miss-only must never outrank fixed-frequency exact-entry watch candidates.
            .OrderByDescending(r => r.IsFixedFrequencyPromoted)
            // Within fixed-frequency (and overall), a current exact entry ranks first.
            .ThenByDescending(r => r.BaseEntrySignalPresentNow && r.ActivationCurrentlyPassed)
            .ThenByDescending(RankExactEntryFirst)
            // Rule 4: readiness-blocked promoted candidates rank below ready/incubating ones.
            .ThenByDescending(r => r.WatchReason != CurrentOpportunityWatchV1Catalog.WatchReasonReadinessBlocked)
            .ThenByDescending(r => r.ExactEntriesPerDay)
            .ThenByDescending(RankOneConditionAway)
            .ThenByDescending(RankSolBoost)
            .ThenByDescending(r => r.NetStressPlus > 0m)
            .ThenByDescending(r => !r.SparseWarning && !r.OverfitWarning && !r.SingleClusterWarning)
            .ThenByDescending(r => r.ResearchScore)
            .ThenBy(r => r.DistanceToEntryPercent)
            .ToList();

    private static int RankExactEntryFirst(CurrentOpportunityWatchV1WatchlistRow row)
        => row.WatchKind == "ExactEntrySignal" ? 1 : 0;

    private static int RankOneConditionAway(CurrentOpportunityWatchV1WatchlistRow row)
        => string.Equals(row.NearMissClassification, "OneConditionAway", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

    private static int RankSolBoost(CurrentOpportunityWatchV1WatchlistRow row)
        => row.IsSolUsdt30mShort
           && string.Equals(row.NearMissClassification, "OneConditionAway", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

    private static CurrentOpportunityWatchV1PlainEnglish BuildPlainEnglish(
        string watchStatus,
        CurrentOpportunityWatchV1WatchlistRow? top,
        string exactEntryCandidate,
        bool hasNearMisses,
        IReadOnlyList<CurrentOpportunityWatchV1FixedFrequencyRow> fixedFrequencyRows,
        CurrentOpportunityWatchV1FixedFrequencyRow? fixedFrequencyExactEntry,
        CurrentOpportunityWatchV1FixedFrequencyRow? closestFixed,
        int blockedByReadiness,
        bool countingBugFixed)
    {
        var isExact = watchStatus == "ExactEntrySignalAppeared";
        var isNearMiss = watchStatus == "NearMissOnly";

        var watchedList = fixedFrequencyRows.Count == 0
            ? "No fixed-frequency promoted candidates were loaded. (Run the fixed cross-candidate exact-entry frequency study first.)"
            : $"{fixedFrequencyRows.Count} promoted fixed-frequency candidate(s) on watch (CountingBugFixed={countingBugFixed}): "
              + string.Join("; ", fixedFrequencyRows.Take(5).Select(f =>
                  $"{f.CandidateKey} exact={f.ExactEntryCountInsideActivatedWindows} perDay={f.ExactEntriesPerDay:F4} reason={f.WatchReason}"))
              + ".";

        var closestOrActivated = fixedFrequencyExactEntry is not null
            ? $"{fixedFrequencyExactEntry.CandidateKey} currently has activation AND a base entry signal on confirmed closed candles (WatchStatus=ExactEntrySignalAppeared, WouldPlaceOrder=false)."
            : closestFixed is null
                ? "No fixed-frequency promoted candidate is currently activated."
                : closestFixed.ActivationCurrentlyPassed
                    ? $"{closestFixed.CandidateKey} is currently activated but has no base entry signal yet (WatchReason={closestFixed.WatchReason})."
                    : $"Closest by watch priority is {closestFixed.CandidateKey} (WatchReason={closestFixed.WatchReason}); none are currently activated.";

        var anyExact = fixedFrequencyExactEntry is not null
            ? $"Yes. {fixedFrequencyExactEntry.CandidateKey} shows a current exact entry. This is a shadow/diagnostic observation only — no order is placed (WouldPlaceOrder=false)."
            : "No. No promoted fixed-frequency candidate currently has both activation and a base entry signal on confirmed closed candles.";

        var blocked = blockedByReadiness > 0
            ? $"Yes. {blockedByReadiness} promoted candidate(s) are blocked by current readiness (bottleneck Park/LookbackStarved or execution-readiness blocked) → WatchReason={CurrentOpportunityWatchV1Catalog.WatchReasonReadinessBlocked}. Strong historical frequency does not override current readiness."
            : "No promoted fixed-frequency candidate is currently blocked by readiness bottlenecks.";

        return new CurrentOpportunityWatchV1PlainEnglish
        {
            IsExactEntrySignalNow = isExact
                ? $"Yes. Exact shadow entry signal present for {exactEntryCandidate}. {CurrentOpportunityWatchV1Catalog.ExactEntryAppearedMessage}"
                : "No exact base entry signal is present on confirmed closed candles.",
            IsNearMissWorthWatching = isNearMiss
                ? "Yes, diagnostically. At least one activation-passed near-miss meets watch quality filters. This is not forward proof and must not be traded."
                : hasNearMisses
                    ? "Some near-miss rows exist in audit output, but none passed top near-miss quality filters for active watch."
                    : "No near-miss worth active watch under current quality filters.",
            ClosestCandidate = top is null
                ? "(none)"
                : $"{top.CandidateKey} ({top.WatchKind}, distance={top.DistanceToEntryPercent:F4}%)",
            MissingCondition = top is null
                ? "(none)"
                : top.WatchKind == "ExactEntrySignal" || (top.BaseEntrySignalPresentNow && top.ActivationCurrentlyPassed)
                    ? "(none — exact entry signal present)"
                    : string.IsNullOrEmpty(top.FailedCondition)
                        ? top.WatchReason
                        : top.FailedCondition,
            ShouldWeTrade = "No. Diagnostic shadow mode never places orders. Near-miss is watch-only and is not forward proof.",
            WhichFixedFrequencyCandidatesWatched = watchedList,
            WhichFixedFrequencyClosestOrActivated = closestOrActivated,
            AnyCurrentExactEntries = anyExact,
            AnyBlockedByCurrentReadiness = blocked
        };
    }
}
