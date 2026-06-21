using System.Globalization;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Evaluates cross-symbol V1 leaderboard rows as shadow/testnet research candidates.
/// Diagnostic only — does not place orders or modify frozen profiles.
/// </summary>
public static class CrossSymbolCandidateEngineV2Builder
{
    public static CrossSymbolCandidateEngineV2RunResult Build(
        CrossSymbolCandidateEngineV2InputBundle input,
        CrossSymbolCandidateEngineV2Settings settings,
        CrossSymbolCandidateEngineV2ForwardRealityBundle forwardReality,
        DateTime runAtUtc)
    {
        var costLookup = BuildCostLookup(input.CostSensitivity);
        var coverageLookup = BuildCoverageLookup(input.DataCoverage);
        var tradesByCandidate = GroupTrades(input.Trades);
        var bottleneckContext = BuildBottleneckContext(input.BottleneckAudit);

        var candidates = input.Leaderboard
            .Select(row => EvaluateCandidate(row, costLookup, coverageLookup, bottleneckContext, input.Periods))
            .OrderByDescending(c => c.CandidateScore)
            .ThenByDescending(c => c.NormalizedNetPer100Usdt)
            .ToArray();

        ApplyOverlapWarnings(candidates, tradesByCandidate);
        ApplyForwardRealityOverlay(candidates, forwardReality);
        var shadowPortfolio = BuildShadowPortfolio(candidates, tradesByCandidate, settings, runAtUtc, onlyExecutionReady: false);
        var executionReadyPortfolio = BuildShadowPortfolio(candidates, tradesByCandidate, settings, runAtUtc, onlyExecutionReady: true);

        var rejections = candidates
            .Where(c => c.ResearchPromotionStatus is not "PromoteToShadow")
            .Select(c => new CrossSymbolCandidateEngineV2RejectionRow
            {
                CandidateKey = c.CandidateKey,
                Symbol = c.Symbol,
                Interval = c.Interval,
                Direction = c.Direction,
                ActivationRule = c.ActivationRule,
                PromotionStatus = c.PromotionStatus,
                RejectionReason = c.RejectionReason,
                CandidateScore = c.CandidateScore
            })
            .ToArray();

        var summary = BuildSummary(runAtUtc, settings, candidates, shadowPortfolio, executionReadyPortfolio);

        return new CrossSymbolCandidateEngineV2RunResult(summary, candidates, rejections, shadowPortfolio, executionReadyPortfolio);
    }

    private static void ApplyForwardRealityOverlay(
        CrossSymbolCandidateEngineV2CandidateRow[] candidates,
        CrossSymbolCandidateEngineV2ForwardRealityBundle forwardReality)
    {
        for (var i = 0; i < candidates.Length; i++)
        {
            var overlay = CrossSymbolCandidateEngineV2ForwardRealityOverlayBuilder.Apply(candidates[i], forwardReality);
            candidates[i] = candidates[i] with
            {
                ResearchPromotionStatus = overlay.ResearchPromotionStatus,
                CurrentExecutionReadiness = overlay.CurrentExecutionReadiness,
                CurrentForwardTrades = overlay.CurrentForwardTrades,
                CurrentForwardNetModerate = overlay.CurrentForwardNetModerate,
                CurrentForwardNetStressPlus = overlay.CurrentForwardNetStressPlus,
                CurrentForwardHealthScore = overlay.CurrentForwardHealthScore,
                CurrentBottleneckClassification = overlay.CurrentBottleneckClassification,
                CurrentBottleneckRecommendation = overlay.CurrentBottleneckRecommendation,
                LatestShadowActivationPassed = overlay.LatestShadowActivationPassed,
                LatestShadowEntrySignalPresent = overlay.LatestShadowEntrySignalPresent,
                LatestShadowWouldPlaceOrder = overlay.LatestShadowWouldPlaceOrder,
                LatestShadowRiskStatus = overlay.LatestShadowRiskStatus,
                LatestShadowReasonIfBlocked = overlay.LatestShadowReasonIfBlocked,
                ExecutionReadinessExplanation = overlay.ExecutionReadinessExplanation,
                CanEnterTestnetOrderMode = overlay.CanEnterTestnetOrderMode,
                MatchedFrozenProfileName = overlay.MatchedFrozenProfileName,
                BottleneckRisk = ResolveDisplayBottleneckRisk(
                    overlay.CurrentExecutionReadiness,
                    candidates[i].LegacyBottleneckRisk)
            };
        }
    }

    private static string ResolveDisplayBottleneckRisk(string currentExecutionReadiness, string legacyBottleneckRisk)
    {
        if (!string.IsNullOrWhiteSpace(currentExecutionReadiness))
            return currentExecutionReadiness;

        return string.IsNullOrWhiteSpace(legacyBottleneckRisk) ? "None" : legacyBottleneckRisk;
    }

    private static CrossSymbolCandidateEngineV2CandidateRow EvaluateCandidate(
        CrossSymbolLeaderboardRow row,
        IReadOnlyDictionary<string, CostTriplet> costLookup,
        IReadOnlyDictionary<string, MultiSymbolDataCoverageRow> coverageLookup,
        BottleneckContext bottleneckContext,
        IReadOnlyList<CrossSymbolPeriodRow> periods)
    {
        var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
            row.Symbol, row.Interval, row.Direction, row.TargetPercent, row.StopPercent, row.ActivationRule);

        costLookup.TryGetValue(key, out var costs);
        var netModerate = costs?.Moderate ?? row.NetPnl;
        var netLatency = costs?.Latency ?? row.ModerateLatencyNet;
        var netStress = costs?.StressPlus ?? row.StressPlusNet;

        var refNotional = CrossSymbolCandidateEngineV2Catalog.ReferenceUnitNotionalUsd(row.Symbol);
        var normalizedPer100 = refNotional > 0m ? Math.Round(netModerate * 100m / refNotional, 6) : 0m;
        var normalizedPer1000 = Math.Round(normalizedPer100 * 10m, 6);

        coverageLookup.TryGetValue($"{row.Symbol}|{row.Interval}", out var coverage);
        var dataCoveragePassed = coverage?.EligibleForShortWindowResearch == true
                                 && coverage.UsableWindowDays >= CrossSymbolCandidateEngineV2Catalog.MinUsableCoverageDays;

        var minTradesPassed = row.TradeCount >= CrossSymbolCandidateEngineV2Catalog.MinTradeCountForShadow;
        var stressPassed = netStress > 0m;
        var costStabilityPassed = netModerate > 0m && netLatency > 0m && netStress > 0m;

        var drawdownRatioBad = netModerate > 0m
            && row.MaxDrawdown > netModerate * CrossSymbolCandidateEngineV2Catalog.MaxDrawdownToNetRatio;

        var bottleneckRisk = ResolveBottleneckRisk(row, bottleneckContext, minTradesPassed, stressPassed, periods, key);

        var (promotionStatus, rejectionReason) = ResolvePromotionStatus(
            row, netModerate, netLatency, netStress, minTradesPassed, stressPassed, costStabilityPassed,
            dataCoveragePassed, drawdownRatioBad, normalizedPer100, bottleneckRisk);

        var score = ComputeCandidateScore(
            row, netModerate, netLatency, netStress, normalizedPer100, dataCoveragePassed, bottleneckRisk);

        var assignedNotional = promotionStatus == "PromoteToShadow"
            ? CrossSymbolCandidateEngineV2Catalog.DefaultMaxPerCandidateNotionalUsdt
            : 0m;

        return new CrossSymbolCandidateEngineV2CandidateRow
        {
            CandidateKey = key,
            Symbol = row.Symbol,
            Interval = row.Interval,
            Direction = row.Direction,
            TargetPercent = row.TargetPercent,
            StopPercent = row.StopPercent,
            ActivationRule = row.ActivationRule,
            TradeCount = row.TradeCount,
            NetModerate = netModerate,
            NetModerateLatency002 = netLatency,
            NetStressPlus = netStress,
            WinRate = row.WinRate,
            ProfitFactor = row.ProfitFactor,
            MaxDrawdown = row.MaxDrawdown,
            MaxConsecutiveLosses = row.MaxConsecutiveLosses,
            PositiveActivatedPeriodsPercent = row.PositiveActivatedPeriodsPercent,
            SparseWarning = row.SparseWarning,
            OverfitWarning = row.OverfitWarning,
            SingleClusterWarning = row.SingleClusterWarning,
            CostStabilityPassed = costStabilityPassed,
            StressPassed = stressPassed,
            MinimumTradeCountPassed = minTradesPassed,
            DataCoveragePassed = dataCoveragePassed,
            NormalizedNetPer100Usdt = normalizedPer100,
            NormalizedNetPer1000Usdt = normalizedPer1000,
            EstimatedRequiredMarginAt1x = assignedNotional,
            EstimatedRequiredMarginAt3x = assignedNotional > 0m
                ? Math.Round(assignedNotional / CrossSymbolCandidateEngineV2Catalog.DefaultLeverageForMarginEstimate, 4)
                : 0m,
            CandidateScore = score,
            PromotionStatus = promotionStatus,
            ResearchPromotionStatus = promotionStatus,
            RejectionReason = rejectionReason,
            LegacyBottleneckRisk = bottleneckRisk,
            BottleneckRisk = bottleneckRisk,
            V1Recommendation = row.Recommendation,
            SuggestedFrozenProfileName = row.SuggestedFrozenProfileName
        };
    }

    private static (string PromotionStatus, string RejectionReason) ResolvePromotionStatus(
        CrossSymbolLeaderboardRow row,
        decimal netModerate,
        decimal netLatency,
        decimal netStress,
        bool minTradesPassed,
        bool stressPassed,
        bool costStabilityPassed,
        bool dataCoveragePassed,
        bool drawdownRatioBad,
        decimal normalizedPer100,
        string bottleneckRisk)
    {
        var failures = new List<string>();

        if (netModerate <= 0m)
            failures.Add("NetModerate<=0");
        if (netStress <= 0m)
            failures.Add("NetStressPlus<=0");
        if (row.SparseWarning)
            failures.Add("SparseWarning");
        if (row.OverfitWarning)
            failures.Add("OverfitWarning");
        if (row.SingleClusterWarning)
            failures.Add("SingleClusterWarning");
        if (!dataCoveragePassed)
            failures.Add("DataCoverageInsufficient");
        if (drawdownRatioBad)
            failures.Add("DrawdownNetRatioTooHigh");

        if (failures.Count >= 2 || netModerate <= 0m)
            return ("Reject", string.Join("; ", failures.DefaultIfEmpty("MultipleQualityFailures")));

        if (netStress <= 0m || drawdownRatioBad)
            return ("Park", string.Join("; ", failures));

        if (!minTradesPassed || row.SparseWarning)
            return ("NeedsMoreData", !minTradesPassed ? "TradeCount<20" : "SparseWarning");

        if (bottleneckRisk.Contains("NeedsLogicReview", StringComparison.OrdinalIgnoreCase))
            return ("KeepIncubating", bottleneckRisk);

        var shadowGates = new List<string>();
        if (netModerate <= 0m) shadowGates.Add("NetModerate<=0");
        if (netLatency <= 0m) shadowGates.Add("NetModerateLatency002<=0");
        if (netStress <= 0m) shadowGates.Add("NetStressPlus<=0");
        if (row.SparseWarning) shadowGates.Add("SparseWarning");
        if (row.OverfitWarning) shadowGates.Add("OverfitWarning");
        if (row.SingleClusterWarning) shadowGates.Add("SingleClusterWarning");
        if (row.PositiveActivatedPeriodsPercent < CrossSymbolCandidateEngineV2Catalog.MinPositivePeriodRate * 100m)
            shadowGates.Add("PositiveActivatedPeriods<40%");
        if (row.MaxConsecutiveLosses > CrossSymbolCandidateEngineV2Catalog.MaxConsecutiveLossesForShadow)
            shadowGates.Add("MaxConsecutiveLosses>4");
        if (!dataCoveragePassed) shadowGates.Add("DataCoverageInsufficient");
        if (normalizedPer100 <= CrossSymbolCandidateEngineV2Catalog.MinNormalizedNetPer100Usdt)
            shadowGates.Add("NormalizedNetPer100USDT<=0");
        if (!string.IsNullOrEmpty(bottleneckRisk) && bottleneckRisk != "None")
            shadowGates.Add($"BottleneckRisk:{bottleneckRisk}");

        if (shadowGates.Count == 0)
            return ("PromoteToShadow", string.Empty);

        var critical = shadowGates.Any(g =>
            g.StartsWith("NetModerate", StringComparison.Ordinal)
            || g.StartsWith("NetStress", StringComparison.Ordinal)
            || g.StartsWith("Sparse", StringComparison.Ordinal)
            || g.StartsWith("Overfit", StringComparison.Ordinal)
            || g.StartsWith("SingleCluster", StringComparison.Ordinal)
            || g.StartsWith("DataCoverage", StringComparison.Ordinal)
            || g.StartsWith("BottleneckRisk", StringComparison.Ordinal));

        if (critical && shadowGates.Count >= 2)
            return ("Reject", string.Join("; ", shadowGates));

        return ("KeepIncubating", string.Join("; ", shadowGates));
    }

    private static string ResolveBottleneckRisk(
        CrossSymbolLeaderboardRow row,
        BottleneckContext context,
        bool minTradesPassed,
        bool stressPassed,
        IReadOnlyList<CrossSymbolPeriodRow> periods,
        string candidateKey)
    {
        if (context.Rows.Count == 0)
            return "None";

        var risks = new List<string>();

        foreach (var audit in context.Rows)
        {
            if (!MatchesBottleneckScope(row, audit))
                continue;

            switch (audit.BottleneckClassification)
            {
                case "BaseSignalTooRare":
                    if (!minTradesPassed || !stressPassed)
                        risks.Add($"SimilarToFrozenBaseSignalTooRare({audit.ProfileName})");
                    break;
                case "LookbackStarved":
                    if (row.ActivationRule.Contains("Perf_", StringComparison.OrdinalIgnoreCase)
                        || row.ActivationRule.Contains("LB", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!HasSufficientResearchLookback(periods, candidateKey))
                            risks.Add($"PerfLookbackBottleneckRisk({audit.ProfileName})");
                    }
                    break;
                case "ActivationTooStrict":
                    if (row.StressPlusNet > 0m && row.TradeCount >= 15)
                        risks.Add("NeedsLogicReview:ActivationTooStrictButStressPositive");
                    break;
            }
        }

        return risks.Count == 0 ? "None" : string.Join("; ", risks.Distinct());
    }

    private static bool HasSufficientResearchLookback(IReadOnlyList<CrossSymbolPeriodRow> periods, string candidateKey)
    {
        var candidatePeriods = periods
            .Where(p => CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                p.Symbol, p.Interval, p.Direction, p.TargetPercent, p.StopPercent, p.ActivationRule) == candidateKey)
            .ToArray();
        if (candidatePeriods.Length == 0)
            return false;

        var activated = candidatePeriods.Where(p => p.Activated).ToArray();
        if (activated.Length == 0)
            return false;

        var avgLookback = activated.Average(p => p.LookbackTradeCount);
        return avgLookback >= NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.MinLookbackTrades;
    }

    private static bool MatchesBottleneckScope(CrossSymbolLeaderboardRow row, FrozenProfileBottleneckAuditRow audit)
    {
        if (!string.Equals(row.Symbol, audit.Symbol, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(row.Interval, audit.Interval, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(row.Direction, audit.Direction, StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static decimal ComputeCandidateScore(
        CrossSymbolLeaderboardRow row,
        decimal netModerate,
        decimal netLatency,
        decimal netStress,
        decimal normalizedPer100,
        bool dataCoveragePassed,
        string bottleneckRisk)
    {
        var score = 0m;
        if (netModerate > 0m) score += 20m;
        if (netLatency > 0m) score += 15m;
        if (netStress > 0m) score += 20m;
        if (row.TradeCount >= 20) score += 10m;
        if (row.PositiveActivatedPeriodsPercent >= 40m) score += 10m;
        if (!row.SparseWarning && !row.OverfitWarning && !row.SingleClusterWarning) score += 10m;
        if (dataCoveragePassed) score += 5m;
        score += Math.Min(20m, normalizedPer100 * 2m);
        score += Math.Min(10m, row.ProfitFactor * 3m);
        if (!string.IsNullOrEmpty(bottleneckRisk) && bottleneckRisk != "None")
            score -= 15m;
        if (row.Symbol.Equals("BTCUSDT", StringComparison.OrdinalIgnoreCase) && normalizedPer100 < 1m)
            score -= 10m;
        return Math.Round(score, 4);
    }

    private static void ApplyOverlapWarnings(
        CrossSymbolCandidateEngineV2CandidateRow[] candidates,
        IReadOnlyDictionary<string, List<CrossSymbolTradeRow>> tradesByCandidate)
    {
        var promoted = candidates.Where(c => c.ResearchPromotionStatus == "PromoteToShadow").ToArray();
        var overlapPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < promoted.Length; i++)
        for (var j = i + 1; j < promoted.Length; j++)
        {
            var a = promoted[i];
            var b = promoted[j];
            if (!string.Equals(a.Symbol, b.Symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!tradesByCandidate.TryGetValue(a.CandidateKey, out var aTrades)
                || !tradesByCandidate.TryGetValue(b.CandidateKey, out var bTrades))
                continue;

            if (HasOverlappingTrades(aTrades, bTrades))
            {
                overlapPairs.Add(a.CandidateKey);
                overlapPairs.Add(b.CandidateKey);
            }
        }

        for (var i = 0; i < candidates.Length; i++)
        {
            if (!overlapPairs.Contains(candidates[i].CandidateKey))
                continue;
            candidates[i] = candidates[i] with { OverlapWarning = true };
        }
    }

    private static CrossSymbolCandidateEngineV2ShadowPortfolioRow BuildShadowPortfolio(
        CrossSymbolCandidateEngineV2CandidateRow[] candidates,
        IReadOnlyDictionary<string, List<CrossSymbolTradeRow>> tradesByCandidate,
        CrossSymbolCandidateEngineV2Settings settings,
        DateTime runAtUtc,
        bool onlyExecutionReady)
    {
        var promoted = candidates
            .Where(c => onlyExecutionReady
                ? c.CanEnterTestnetOrderMode
                : c.ResearchPromotionStatus == "PromoteToShadow")
            .ToList();

        if (settings.OneCandidatePerSymbol)
        {
            promoted = promoted
                .GroupBy(c => c.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(c => c.CandidateScore).First())
                .ToList();
        }

        promoted = promoted
            .OrderByDescending(c => c.CandidateScore)
            .Take(settings.MaxShadowCandidates)
            .ToList();

        var remainingBudget = settings.MaxTotalShadowNotionalUsdt;
        var selected = new List<(CrossSymbolCandidateEngineV2CandidateRow Candidate, decimal Notional)>();
        foreach (var candidate in promoted)
        {
            if (remainingBudget <= 0m)
                break;
            var notional = Math.Min(settings.MaxPerCandidateNotionalUsdt, remainingBudget);
            selected.Add((candidate, notional));
            remainingBudget -= notional;
        }

        var portfolioTrades = new List<ScaledTrade>();
        var contributions = new List<CrossSymbolCandidateEngineV2ContributionRow>();
        var shadowDecisions = new List<CrossSymbolCandidateEngineV2ShadowDecisionRow>();
        var overlapCount = 0;

        foreach (var (candidate, notional) in selected)
        {
            if (!tradesByCandidate.TryGetValue(candidate.CandidateKey, out var trades))
                trades = [];

            var moderateTrades = trades
                .Where(t => string.Equals(t.CostScenario, CrossSymbolCandidateEngineV2Catalog.PrimaryCostScenario, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.EntryTimeUtc)
                .ToArray();

            var refNotional = CrossSymbolCandidateEngineV2Catalog.ReferenceUnitNotionalUsd(candidate.Symbol);
            var scale = refNotional > 0m ? notional / refNotional : 0m;
            var scaledNets = moderateTrades
                .Select(t => new ScaledTrade(candidate.CandidateKey, candidate.Symbol, t.EntryTimeUtc, t.ExitTimeUtc, t.NetPnlQuote * scale))
                .ToArray();
            portfolioTrades.AddRange(scaledNets);

            var netPer100 = moderateTrades.Sum(t => t.NetPnlQuote * (100m / refNotional));
            var netPer1000 = netPer100 * 10m;

            contributions.Add(new CrossSymbolCandidateEngineV2ContributionRow
            {
                CandidateKey = candidate.CandidateKey,
                Symbol = candidate.Symbol,
                Interval = candidate.Interval,
                Direction = candidate.Direction,
                AssignedNotionalUsdt = notional,
                TradeCount = moderateTrades.Length,
                NetPer100Usdt = Math.Round(netPer100, 6),
                NetPer1000Usdt = Math.Round(netPer1000, 6),
                ShareOfPortfolioNetPer1000Usdt = 0m
            });

            var direction = Enum.TryParse<LongShortDirection>(candidate.Direction, true, out var dir)
                ? dir
                : LongShortDirection.Short;
            shadowDecisions.Add(new CrossSymbolCandidateEngineV2ShadowDecisionRow
            {
                TimestampUtc = runAtUtc,
                CandidateKey = candidate.CandidateKey,
                Symbol = candidate.Symbol,
                Interval = candidate.Interval,
                Direction = candidate.Direction,
                ActivationRule = candidate.ActivationRule,
                ActivationPassed = candidate.LatestShadowActivationPassed ?? !onlyExecutionReady,
                ActivationReason = onlyExecutionReady
                    ? candidate.ExecutionReadinessExplanation
                    : "PromotedFromV1ResearchCandidate",
                EntrySignalPresent = candidate.LatestShadowEntrySignalPresent ?? false,
                EntryReason = onlyExecutionReady
                    ? candidate.LatestShadowReasonIfBlocked
                    : "ShadowPortfolioResearchOnly",
                WouldPlaceOrder = false,
                OrderSide = direction == LongShortDirection.Long ? "BUY" : "SELL",
                AssumedNotionalUsdt = notional,
                NetPnlPer100Usdt = FuturesTestnetShadowEvaluator.EstimateNetPnlPer100Usdt(
                    candidate.TargetPercent, candidate.StopPercent, direction),
                RequiredMarginAtLeverage = notional / CrossSymbolCandidateEngineV2Catalog.DefaultLeverageForMarginEstimate,
                Leverage = CrossSymbolCandidateEngineV2Catalog.DefaultLeverageForMarginEstimate,
                RiskStatus = onlyExecutionReady
                    ? (candidate.CanEnterTestnetOrderMode ? "ExecutionReadyShadowCandidate" : candidate.LatestShadowRiskStatus)
                    : "ShadowResearchOnly",
                ReasonIfBlocked = onlyExecutionReady
                    ? candidate.ExecutionReadinessExplanation
                    : "ResearchCandidateEngineV2DryRun",
                PromotionStatus = candidate.ResearchPromotionStatus,
                BottleneckRisk = candidate.BottleneckRisk,
                CurrentExecutionReadiness = candidate.CurrentExecutionReadiness,
                CanEnterTestnetOrderMode = candidate.CanEnterTestnetOrderMode
            });
        }

        overlapCount = CountPortfolioOverlaps(portfolioTrades);

        var totalNetPer1000 = portfolioTrades.Sum(t => t.ScaledNet) * (1000m / Math.Max(1m, selected.Sum(s => s.Notional)));
        if (selected.Sum(s => s.Notional) > 0m)
        {
            var totalNetPer100 = portfolioTrades.Sum(t => t.ScaledNet) * (100m / selected.Sum(s => s.Notional));
            totalNetPer1000 = totalNetPer100 * 10m;
        }

        var totalNetPer100Final = selected.Sum(s => s.Notional) > 0m
            ? Math.Round(portfolioTrades.Sum(t => t.ScaledNet) * 100m / selected.Sum(s => s.Notional), 6)
            : 0m;
        totalNetPer1000 = Math.Round(totalNetPer100Final * 10m, 6);

        var (maxDd, worstDay, bestDay, maxConsecLosses) = ComputePortfolioStats(portfolioTrades);

        if (contributions.Count > 0 && Math.Abs(totalNetPer1000) > 0.000001m)
        {
            contributions = contributions
                .Select(c => c with
                {
                    ShareOfPortfolioNetPer1000Usdt = Math.Round(c.NetPer1000Usdt / totalNetPer1000 * 100m, 4)
                })
                .ToList();
        }

        for (var i = 0; i < candidates.Length; i++)
        {
            var selectedMatch = selected.FirstOrDefault(s => s.Candidate.CandidateKey == candidates[i].CandidateKey);
            if (selectedMatch.Candidate is null)
                continue;
            candidates[i] = candidates[i] with
            {
                SelectedForShadowPortfolio = !onlyExecutionReady || candidates[i].SelectedForShadowPortfolio,
                SelectedForExecutionReadyPortfolio = onlyExecutionReady || candidates[i].SelectedForExecutionReadyPortfolio,
                AssignedShadowNotionalUsdt = !onlyExecutionReady
                    ? selectedMatch.Notional
                    : candidates[i].AssignedShadowNotionalUsdt
            };
        }

        return new CrossSymbolCandidateEngineV2ShadowPortfolioRow
        {
            PromotedCandidateCount = selected.Count,
            TotalAssignedNotionalUsdt = selected.Sum(s => s.Notional),
            TotalTrades = portfolioTrades.Count,
            NetPer100Usdt = totalNetPer100Final,
            NetPer1000Usdt = totalNetPer1000,
            MaxDrawdownPer1000Usdt = maxDd,
            WorstDayPer1000Usdt = worstDay,
            BestDayPer1000Usdt = bestDay,
            MaxConsecutiveLosses = maxConsecLosses,
            OverlappingSignalCount = overlapCount,
            CandidateContributionBreakdown = contributions,
            ShadowDecisions = shadowDecisions
        };
    }

    private sealed record ScaledTrade(string CandidateKey, string Symbol, DateTime EntryUtc, DateTime ExitUtc, decimal ScaledNet);

    private static (decimal MaxDd, decimal WorstDay, decimal BestDay, int MaxConsecLosses) ComputePortfolioStats(
        IReadOnlyList<ScaledTrade> trades)
    {
        if (trades.Count == 0)
            return (0m, 0m, 0m, 0);

        var ordered = trades.OrderBy(t => t.EntryUtc).ToArray();
        decimal equity = 0m, peak = 0m, maxDd = 0m;
        var maxConsec = 0;
        var currentConsec = 0;

        foreach (var trade in ordered)
        {
            equity += trade.ScaledNet;
            if (equity > peak) peak = equity;
            var dd = peak - equity;
            if (dd > maxDd) maxDd = dd;

            if (trade.ScaledNet <= 0m)
            {
                currentConsec++;
                if (currentConsec > maxConsec) maxConsec = currentConsec;
            }
            else
            {
                currentConsec = 0;
            }
        }

        var daily = trades
            .GroupBy(t => t.EntryUtc.Date)
            .Select(g => g.Sum(t => t.ScaledNet) * 1000m)
            .ToArray();

        var worstDay = daily.Length > 0 ? Math.Round(daily.Min(), 6) : 0m;
        var bestDay = daily.Length > 0 ? Math.Round(daily.Max(), 6) : 0m;

        return (Math.Round(maxDd * 1000m, 6), worstDay, bestDay, maxConsec);
    }

    private static int CountPortfolioOverlaps(IReadOnlyList<ScaledTrade> trades)
    {
        var count = 0;
        for (var i = 0; i < trades.Count; i++)
        for (var j = i + 1; j < trades.Count; j++)
        {
            if (!string.Equals(trades[i].Symbol, trades[j].Symbol, StringComparison.OrdinalIgnoreCase))
                continue;
            if (trades[i].EntryUtc < trades[j].ExitUtc && trades[j].EntryUtc < trades[i].ExitUtc)
                count++;
        }
        return count;
    }

    private static bool HasOverlappingTrades(
        IReadOnlyList<CrossSymbolTradeRow> a,
        IReadOnlyList<CrossSymbolTradeRow> b)
    {
        foreach (var ta in a)
        foreach (var tb in b)
        {
            if (ta.EntryTimeUtc < tb.ExitTimeUtc && tb.EntryTimeUtc < ta.ExitTimeUtc)
                return true;
        }
        return false;
    }

    private static CrossSymbolCandidateEngineV2SummaryRow BuildSummary(
        DateTime runAtUtc,
        CrossSymbolCandidateEngineV2Settings settings,
        IReadOnlyList<CrossSymbolCandidateEngineV2CandidateRow> candidates,
        CrossSymbolCandidateEngineV2ShadowPortfolioRow portfolio,
        CrossSymbolCandidateEngineV2ShadowPortfolioRow executionReadyPortfolio)
    {
        var researchPromoted = candidates.Count(c => c.ResearchPromotionStatus == "PromoteToShadow");
        var executable = candidates.Count(c => c.CurrentExecutionReadiness == "ExecutableShadowCandidate");
        var canEnterTestnet = candidates.Count(c => c.CanEnterTestnetOrderMode);
        var blockedLookback = candidates.Count(c => c.CurrentExecutionReadiness == "LookbackStarvedCurrent");
        var blockedEntry = candidates.Count(c => c.CurrentExecutionReadiness == "EntrySignalCurrentlyMissing");
        var blockedStress = candidates.Count(c => c.CurrentExecutionReadiness == "StressNegativeForward");

        var compact = string.Join(" | ",
            $"Evaluated={candidates.Count}",
            $"ResearchPromoted={researchPromoted}",
            $"ExecutableNow={executable}",
            $"CanEnterTestnet={canEnterTestnet}",
            $"ResearchPortfolio={portfolio.PromotedCandidateCount}",
            $"ExecutionReadyPortfolio={executionReadyPortfolio.PromotedCandidateCount}",
            $"BlockedLookback={blockedLookback}");

        return new CrossSymbolCandidateEngineV2SummaryRow
        {
            RunAtUtc = runAtUtc,
            V1InputDirectory = settings.V1InputDirectory,
            BottleneckAuditDirectory = settings.BottleneckAuditDirectory,
            CandidatesEvaluated = candidates.Count,
            PromoteToShadowCount = researchPromoted,
            KeepIncubatingCount = candidates.Count(c => c.ResearchPromotionStatus == "KeepIncubating"),
            NeedsMoreDataCount = candidates.Count(c => c.ResearchPromotionStatus == "NeedsMoreData"),
            ParkCount = candidates.Count(c => c.ResearchPromotionStatus == "Park"),
            RejectCount = candidates.Count(c => c.ResearchPromotionStatus == "Reject"),
            ShadowPortfolioCandidateCount = portfolio.PromotedCandidateCount,
            ResearchPromotedCount = researchPromoted,
            ExecutableShadowCandidateCount = executable,
            CanEnterTestnetOrderModeCount = canEnterTestnet,
            BlockedByLookbackStarvationCount = blockedLookback,
            BlockedByMissingEntrySignalCount = blockedEntry,
            BlockedByStressNegativeForwardCount = blockedStress,
            ExecutionReadyPortfolioCandidateCount = executionReadyPortfolio.PromotedCandidateCount,
            OneCandidatePerSymbol = settings.OneCandidatePerSymbol,
            MaxShadowCandidates = settings.MaxShadowCandidates,
            MaxTotalShadowNotionalUsdt = settings.MaxTotalShadowNotionalUsdt,
            MaxPerCandidateNotionalUsdt = settings.MaxPerCandidateNotionalUsdt,
            BacktestOnly = true,
            ShadowDryRunOnly = true,
            RealOrdersPlaced = false,
            LiveFuturesRecommended = false,
            CompactSummaryLine = compact
        };
    }

    private sealed record CostTriplet(decimal Moderate, decimal Latency, decimal StressPlus);

    private sealed record BottleneckContext(IReadOnlyList<FrozenProfileBottleneckAuditRow> Rows);

    private static BottleneckContext BuildBottleneckContext(IReadOnlyList<FrozenProfileBottleneckAuditRow> rows)
        => new(rows);

    private static Dictionary<string, CostTriplet> BuildCostLookup(IReadOnlyList<CrossSymbolCostSensitivityRow> rows)
    {
        var lookup = new Dictionary<string, CostTriplet>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in rows.GroupBy(r => CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                     r.Symbol, r.Interval, r.Direction, r.TargetPercent, r.StopPercent, r.ActivationRule)))
        {
            decimal Net(string scenario) => group
                .FirstOrDefault(r => string.Equals(r.CostScenario, scenario, StringComparison.OrdinalIgnoreCase))
                ?.NetPnlQuote ?? 0m;

            lookup[group.Key] = new CostTriplet(
                Net(CrossSymbolCandidateEngineV2Catalog.PrimaryCostScenario),
                Net(CrossSymbolCandidateEngineV2Catalog.ModerateLatencyScenario),
                Net(CrossSymbolCandidateEngineV2Catalog.StressPlusScenario));
        }

        return lookup;
    }

    private static Dictionary<string, MultiSymbolDataCoverageRow> BuildCoverageLookup(
        IReadOnlyList<MultiSymbolDataCoverageRow> rows)
        => rows.ToDictionary(r => $"{r.Symbol}|{r.Interval}", StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, List<CrossSymbolTradeRow>> GroupTrades(IReadOnlyList<CrossSymbolTradeRow> trades)
        => trades
            .GroupBy(t => CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                t.Symbol, t.Interval, t.Direction, t.TargetPercent, t.StopPercent, t.ActivationRule))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}
