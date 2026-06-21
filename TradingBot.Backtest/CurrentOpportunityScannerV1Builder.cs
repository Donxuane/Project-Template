using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class CurrentOpportunityScannerV1Builder
{
    private static readonly Dictionary<string, CrossSymbolActivationConfig> ActivationLookup =
        NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.BuildActivationConfigs()
            .ToDictionary(c => c.ActivationRuleName, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> BlockedReadinessValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "LookbackStarvedCurrent",
        "ActivationCurrentlyBlocked",
        "StressNegativeForward",
        "SafetyBlocked",
        "NotExecutable",
        "ForwardEvidencePending"
    };

    public static CurrentOpportunityScannerV1RunResult Build(
        CurrentOpportunityScannerV1InputBundle input,
        CurrentOpportunityScannerV1MarketContext market,
        DateTime runAtUtc)
    {
        var rows = new List<CurrentOpportunityScannerV1CandidateRow>();

        foreach (var leader in input.Leaderboard)
        {
            var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                leader.Symbol, leader.Interval, leader.Direction,
                leader.TargetPercent, leader.StopPercent, leader.ActivationRule);
            input.V2ByKey.TryGetValue(key, out var v2);

            if (!ActivationLookup.TryGetValue(leader.ActivationRule, out var activationConfig))
                continue;

            if (!Enum.TryParse<TradingSymbol>(leader.Symbol, true, out var symbol))
                continue;
            if (!Enum.TryParse<LongShortDirection>(leader.Direction, true, out var direction))
                continue;

            var comboKey = new CrossSymbolComboKey(
                symbol, leader.Interval, direction,
                leader.TargetPercent, leader.StopPercent,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes);

            if (!market.TryGetScan(comboKey, out var scan))
                continue;

            var moderateTrades = NoPaidDataShortWindowFlowResearchV1Aggregator.MapCostScenario(
                scan.BaseTrades,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.PrimaryCostScenario,
                market.BtcContext,
                market.EvalUtc);

            var activation = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
                activationConfig,
                comboKey,
                moderateTrades,
                market.EvalUtc,
                market.StudyStartUtc,
                market.GetFlowIndex(symbol, leader.Interval));

            var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(leader.Interval);
            var intervalCandles = market.GetIntervalCandles(symbol, leader.Interval);
            var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                comboKey,
                intervalCandles,
                scan.BaseTrades,
                market.EvalUtc,
                market.StudyStartUtc,
                cooldown);

            var netModerate = v2?.NetModerate ?? leader.NetPnl;
            var netStress = v2?.NetStressPlus ?? leader.StressPlusNet;
            var refNotional = CrossSymbolCandidateEngineV2Catalog.ReferenceUnitNotionalUsd(leader.Symbol);
            var normalizedPer100 = refNotional > 0m
                ? Math.Round(netModerate * 100m / refNotional, 6)
                : (v2?.NormalizedNetPer100Usdt ?? 0m);
            var maxDrawdown = v2?.MaxDrawdown ?? leader.MaxDrawdown;
            var normalizedRisk = NormalizedRiskPnlModule.Compute(leader.Symbol, netModerate, maxDrawdown);

            input.ShadowByScope.TryGetValue(
                CurrentOpportunityScannerV1Loader.ScopeKey(leader.Symbol, leader.Interval, leader.Direction),
                out var shadowRow);

            var bottleneck = FindBottleneck(input.BottleneckAudit, leader, v2?.SuggestedFrozenProfileName);
            var researchPromotion = v2?.ResearchPromotionStatus ?? v2?.PromotionStatus ?? leader.Recommendation;
            var readiness = v2?.CurrentExecutionReadiness ?? InferReadiness(activation, entry, bottleneck);

            var (blockedCategory, reasonIfBlocked, riskStatus, precisionStatus) = ClassifyCandidate(
                leader,
                v2,
                activation,
                entry,
                netModerate,
                netStress,
                bottleneck,
                researchPromotion,
                readiness,
                shadowRow,
                comboKey,
                market);

            var researchQualityOk = netModerate > 0m
                                    && netStress > 0m
                                    && !leader.SparseWarning
                                    && !leader.OverfitWarning
                                    && !leader.SingleClusterWarning;

            var wouldBeActionable = blockedCategory == string.Empty
                                    && activation.Passed
                                    && entry.Present
                                    && researchQualityOk
                                    && !string.Equals(riskStatus, "Blocked", StringComparison.OrdinalIgnoreCase);

            var almostActionable = !wouldBeActionable
                                   && (activation.Passed || entry.Present)
                                   && researchQualityOk;

            rows.Add(new CurrentOpportunityScannerV1CandidateRow
            {
                CandidateKey = key,
                Symbol = leader.Symbol,
                Interval = leader.Interval,
                Direction = leader.Direction,
                TargetPercent = leader.TargetPercent,
                StopPercent = leader.StopPercent,
                ActivationRule = leader.ActivationRule,
                ResearchPromotionStatus = researchPromotion,
                CurrentExecutionReadiness = readiness,
                ResearchScore = v2?.CandidateScore ?? Math.Round(netModerate, 4),
                TradeCount = v2?.TradeCount ?? leader.TradeCount,
                NetModerate = netModerate,
                NetStressPlus = netStress,
                WinRate = v2?.WinRate ?? leader.WinRate,
                ProfitFactor = v2?.ProfitFactor ?? leader.ProfitFactor,
                SparseWarning = leader.SparseWarning,
                OverfitWarning = leader.OverfitWarning,
                SingleClusterWarning = leader.SingleClusterWarning,
                LatestCandleUtc = market.EvalUtc,
                ActivationCurrentlyPassed = activation.Passed,
                ActivationFailureReason = activation.Passed ? string.Empty : activation.Reason,
                BaseEntrySignalPresentNow = entry.Present,
                EntrySignalFailureReason = entry.Present ? string.Empty : entry.Reason,
                WouldBeShadowActionable = wouldBeActionable,
                WouldPlaceOrder = false,
                ReasonIfBlocked = reasonIfBlocked,
                BlockedReasonCategory = blockedCategory,
                NormalizedNetPer100Usdt = normalizedPer100,
                NormalizedRisk = normalizedRisk,
                AssignedShadowNotionalUsdt = v2?.AssignedShadowNotionalUsdt > 0m
                    ? v2.AssignedShadowNotionalUsdt
                    : CurrentOpportunityScannerV1Catalog.DefaultShadowNotionalUsdt,
                RiskStatus = riskStatus,
                PrecisionStatus = precisionStatus,
                AlmostActionable = almostActionable
            });
        }

        var actionable = rows.Where(r => r.WouldBeShadowActionable).ToArray();
        var blocked = rows.Where(r => !r.WouldBeShadowActionable).ToArray();

        var blockerCounts = blocked
            .Where(r => !string.IsNullOrEmpty(r.BlockedReasonCategory))
            .GroupBy(r => r.BlockedReasonCategory, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}={g.Count()}")
            .Take(CurrentOpportunityScannerV1Catalog.TopBlockersLimit)
            .ToArray();

        var summaryRisk = actionable.Length > 0
            ? actionable.OrderByDescending(r => r.ResearchScore).First().NormalizedRisk
            : rows.OrderByDescending(r => r.NormalizedRisk.NetPnlPer100UsdtAt3x).FirstOrDefault()?.NormalizedRisk
              ?? new NormalizedRiskPnlMetrics();

        var summary = new CurrentOpportunityScannerV1SummaryRow
        {
            RunAtUtc = runAtUtc,
            EvaluatedAtUtc = market.EvalUtc,
            NormalizedRisk = summaryRisk,
            EvaluatedCandidateCount = rows.Count,
            ActivationPassedCount = rows.Count(r => r.ActivationCurrentlyPassed),
            BaseEntrySignalPresentCount = rows.Count(r => r.BaseEntrySignalPresentNow),
            ActionableShadowCount = actionable.Length,
            AlmostActionableCount = rows.Count(r => r.AlmostActionable),
            BlockedByActivationCount = blocked.Count(r => r.BlockedReasonCategory == "ActivationBlocked"),
            BlockedByEntryMissingCount = blocked.Count(r => r.BlockedReasonCategory == "EntrySignalCurrentlyMissing"),
            BlockedByResearchStressCount = blocked.Count(r => r.BlockedReasonCategory == "ResearchStressNegative"),
            BlockedByBottleneckCount = blocked.Count(r => r.BlockedReasonCategory == "BottleneckParked"),
            BlockedByExecutionReadinessCount = blocked.Count(r => r.BlockedReasonCategory == "CurrentExecutionReadinessBlocked"),
            BlockedByResearchQualityCount = blocked.Count(r => r.BlockedReasonCategory == "ResearchQualityBlocked"),
            TopBlockers = blockerCounts,
            TopActionableCandidates = actionable
                .OrderByDescending(r => r.ResearchScore)
                .ThenByDescending(r => r.NormalizedNetPer100Usdt)
                .Take(CurrentOpportunityScannerV1Catalog.TopActionableLimit)
                .ToArray(),
            TopAlmostActionableCandidates = rows
                .Where(r => r.AlmostActionable)
                .OrderByDescending(r => r.ActivationCurrentlyPassed)
                .ThenByDescending(r => r.BaseEntrySignalPresentNow)
                .ThenByDescending(r => r.ResearchScore)
                .Take(CurrentOpportunityScannerV1Catalog.TopAlmostActionableLimit)
                .ToArray(),
            V1InputDirectory = input.V1InputDirectory,
            V2InputDirectory = input.V2InputDirectory,
            CompactSummaryLine = actionable.Length == 0
                ? $"No current opportunity | evaluated={rows.Count} activationPassed={rows.Count(r => r.ActivationCurrentlyPassed)} entryPresent={rows.Count(r => r.BaseEntrySignalPresentNow)} | Normalized Est. (per $100 at 3x): {summaryRisk.NetPnlPer100UsdtAt3x:F2}"
                : $"Shadow opportunity exists | actionable={actionable.Length} evaluated={rows.Count} activationPassed={rows.Count(r => r.ActivationCurrentlyPassed)} | Normalized Est. (per $100 at 3x): {summaryRisk.NetPnlPer100UsdtAt3x:F2}"
        };

        return new CurrentOpportunityScannerV1RunResult(summary, rows, actionable, blocked);
    }

    private static (string BlockedCategory, string Reason, string RiskStatus, string PrecisionStatus) ClassifyCandidate(
        CrossSymbolLeaderboardRow leader,
        CrossSymbolCandidateEngineV2CandidateRow? v2,
        FuturesTestnetShadowEvaluator.ActivationState activation,
        FuturesTestnetShadowEvaluator.EntryState entry,
        decimal netModerate,
        decimal netStress,
        FrozenProfileBottleneckAuditRow? bottleneck,
        string researchPromotion,
        string readiness,
        CrossSymbolCandidateEngineV2ShadowDecisionImportRow? shadowRow,
        CrossSymbolComboKey comboKey,
        CurrentOpportunityScannerV1MarketContext market)
    {
        var precisionStatus = market.EvaluatePrecisionStatus(comboKey.Symbol, entry.EntryPrice);
        var riskStatus = "ShadowEligible";

        if (bottleneck is not null
            && string.Equals(bottleneck.Recommendation, "Park", StringComparison.OrdinalIgnoreCase)
            && MatchesBottleneckScope(leader, bottleneck))
        {
            return ("BottleneckParked",
                $"Bottleneck audit recommends Park ({bottleneck.BottleneckClassification}).",
                "Blocked",
                precisionStatus);
        }

        if (string.Equals(researchPromotion, "PromoteToShadow", StringComparison.OrdinalIgnoreCase)
            && BlockedReadinessValues.Contains(readiness))
        {
            return ("CurrentExecutionReadinessBlocked",
                $"Research promotes but current readiness is {readiness}.",
                "Blocked",
                precisionStatus);
        }

        if (entry.Present && netStress <= 0m)
        {
            return ("ResearchStressNegative",
                "Entry signal present but research stress-plus net is not positive.",
                "Blocked",
                precisionStatus);
        }

        if (activation.Passed && !entry.Present)
        {
            return ("EntrySignalCurrentlyMissing",
                string.IsNullOrEmpty(entry.Reason) ? "Activation passed but no base entry signal now." : entry.Reason,
                "Blocked",
                precisionStatus);
        }

        if (!activation.Passed)
        {
            return ("ActivationBlocked",
                string.IsNullOrEmpty(activation.Reason) ? "Activation not passed at latest evaluation time." : activation.Reason,
                "Blocked",
                precisionStatus);
        }

        if (netModerate <= 0m || netStress <= 0m
            || leader.SparseWarning || leader.OverfitWarning || leader.SingleClusterWarning)
        {
            return ("ResearchQualityBlocked",
                BuildResearchQualityReason(leader, netModerate, netStress),
                "Blocked",
                precisionStatus);
        }

        if (shadowRow is not null
            && shadowRow.RiskStatus.Contains("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return ("ShadowRiskBlocked",
                shadowRow.ReasonIfBlocked,
                "Blocked",
                precisionStatus);
        }

        if (!string.Equals(precisionStatus, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            return ("PrecisionBlocked",
                "Exchange precision/notional check failed for shadow sizing.",
                "Blocked",
                precisionStatus);
        }

        return (string.Empty, string.Empty, riskStatus, precisionStatus);
    }

    private static string BuildResearchQualityReason(
        CrossSymbolLeaderboardRow leader,
        decimal netModerate,
        decimal netStress)
    {
        var parts = new List<string>();
        if (netModerate <= 0m) parts.Add("NetModerate<=0");
        if (netStress <= 0m) parts.Add("NetStressPlus<=0");
        if (leader.SparseWarning) parts.Add("SparseWarning");
        if (leader.OverfitWarning) parts.Add("OverfitWarning");
        if (leader.SingleClusterWarning) parts.Add("SingleClusterWarning");
        return string.Join("; ", parts);
    }

    private static FrozenProfileBottleneckAuditRow? FindBottleneck(
        IReadOnlyList<FrozenProfileBottleneckAuditRow> audit,
        CrossSymbolLeaderboardRow leader,
        string? suggestedProfileName)
    {
        if (!string.IsNullOrWhiteSpace(suggestedProfileName))
        {
            var byName = audit.FirstOrDefault(a =>
                string.Equals(a.ProfileName, suggestedProfileName, StringComparison.OrdinalIgnoreCase));
            if (byName is not null)
                return byName;
        }

        return audit.FirstOrDefault(a => MatchesBottleneckScope(leader, a));
    }

    private static bool MatchesBottleneckScope(
        CrossSymbolLeaderboardRow leader,
        FrozenProfileBottleneckAuditRow audit)
        => string.Equals(audit.Symbol, leader.Symbol, StringComparison.OrdinalIgnoreCase)
           && string.Equals(audit.Interval, leader.Interval, StringComparison.OrdinalIgnoreCase)
           && string.Equals(audit.Direction, leader.Direction, StringComparison.OrdinalIgnoreCase);

    private static string InferReadiness(
        FuturesTestnetShadowEvaluator.ActivationState activation,
        FuturesTestnetShadowEvaluator.EntryState entry,
        FrozenProfileBottleneckAuditRow? bottleneck)
    {
        if (bottleneck is not null
            && string.Equals(bottleneck.BottleneckClassification, "LookbackStarved", StringComparison.OrdinalIgnoreCase))
            return "LookbackStarvedCurrent";

        if (!activation.Passed)
            return "ActivationCurrentlyBlocked";

        if (activation.Passed && !entry.Present)
            return "EntrySignalCurrentlyMissing";

        return "ForwardEvidencePending";
    }
}
