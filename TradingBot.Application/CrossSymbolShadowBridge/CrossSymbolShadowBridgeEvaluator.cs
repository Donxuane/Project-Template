namespace TradingBot.Application.CrossSymbolShadowBridge;

public static class CrossSymbolShadowBridgeEvaluator
{
    public static CrossSymbolShadowBridgeRunResult Evaluate(
        CrossSymbolShadowBridgeSettings settings,
        CrossSymbolShadowBridgeInputBundle input,
        DateTime evaluatedAtUtc)
    {
        var executionReadyKeys = BuildExecutionReadyKeySet(settings, input);
        var notionalByKey = BuildNotionalLookup(settings, input, executionReadyKeys);

        var decisions = new List<CrossSymbolShadowBridgeDecisionRow>();
        var executionReady = new List<CrossSymbolShadowBridgeDecisionRow>();
        var researchShadowOnly = new List<CrossSymbolShadowBridgeDecisionRow>();
        var rejected = new List<CrossSymbolShadowBridgeDecisionRow>();

        foreach (var candidate in input.Candidates)
        {
            var researchStatus = string.IsNullOrWhiteSpace(candidate.ResearchPromotionStatus)
                ? candidate.PromotionStatus
                : candidate.ResearchPromotionStatus;

            var category = ClassifyCandidate(settings, candidate, researchStatus, executionReadyKeys);
            var assignedNotional = notionalByKey.GetValueOrDefault(candidate.CandidateKey, 0m);
            var reasonIfBlocked = BuildReasonIfBlocked(settings, category, candidate, researchStatus, assignedNotional);

            var decision = new CrossSymbolShadowBridgeDecisionRow
            {
                EvaluatedAtUtc = evaluatedAtUtc,
                CandidateKey = candidate.CandidateKey,
                Symbol = candidate.Symbol,
                Interval = candidate.Interval,
                Direction = candidate.Direction,
                TargetPercent = candidate.TargetPercent,
                StopPercent = candidate.StopPercent,
                ActivationRule = candidate.ActivationRule,
                Category = category.ToString(),
                ResearchPromotionStatus = researchStatus,
                CurrentExecutionReadiness = candidate.CurrentExecutionReadiness,
                CanEnterTestnetOrderMode = candidate.CanEnterTestnetOrderMode,
                CurrentForwardTrades = candidate.CurrentForwardTrades,
                CurrentForwardNetModerate = candidate.CurrentForwardNetModerate,
                CurrentForwardNetStressPlus = candidate.CurrentForwardNetStressPlus,
                LatestShadowActivationPassed = candidate.LatestShadowActivationPassed,
                LatestShadowEntrySignalPresent = candidate.LatestShadowEntrySignalPresent,
                LatestShadowRiskStatus = candidate.LatestShadowRiskStatus,
                ExecutionReadinessExplanation = candidate.ExecutionReadinessExplanation,
                AssignedShadowNotionalUsdt = assignedNotional,
                WouldPlaceOrder = false,
                ReasonIfBlocked = reasonIfBlocked
            };

            decisions.Add(decision);

            switch (category)
            {
                case CrossSymbolShadowBridgeCandidateCategory.ExecutionReadyCandidates:
                    executionReady.Add(decision);
                    break;
                case CrossSymbolShadowBridgeCandidateCategory.ResearchPromotedShadowOnly:
                    researchShadowOnly.Add(decision);
                    break;
                default:
                    rejected.Add(decision);
                    break;
            }
        }

        var portfolioCount = input.Summary?.ExecutionReadyPortfolioCandidateCount
                             ?? input.ExecutionReadyPortfolio?.Portfolio?.PromotedCandidateCount
                             ?? executionReady.Count;

        var statusCode = ResolveStatus(settings, executionReady.Count, portfolioCount);
        var message = BuildStatusMessage(executionReady.Count, researchShadowOnly.Count, rejected.Count);

        var status = new CrossSymbolShadowBridgeStatus
        {
            EvaluatedAtUtc = evaluatedAtUtc,
            Status = statusCode,
            CompactSummaryLine = input.Summary?.CompactSummaryLine ?? string.Empty,
            Enabled = settings.Enabled,
            ShadowOnly = settings.ShadowOnly,
            BacktestOnly = settings.BacktestOnly,
            DryRunOnly = settings.DryRunOnly,
            AllowOrders = settings.EffectiveAllowOrders,
            AllowTestnetOrders = settings.EffectiveAllowTestnetOrders,
            AllowRealOrders = settings.EffectiveAllowRealOrders,
            RealOrdersPlaced = settings.RealOrdersPlaced,
            LiveFuturesRecommended = settings.LiveFuturesRecommended,
            CandidateInputDirectory = input.CandidateInputDirectory,
            OutputDirectory = settings.OutputDirectory ?? string.Empty,
            TotalCandidatesLoaded = input.Candidates.Count,
            ExecutionReadyCandidateCount = executionReady.Count,
            ResearchPromotedShadowOnlyCount = researchShadowOnly.Count,
            RejectedOrParkedCount = rejected.Count,
            ExecutionReadyPortfolioCandidateCount = portfolioCount,
            ShadowDecisionsAvailable = input.ShadowDecisionsAvailable,
            BottleneckAuditAvailable = input.BottleneckAuditAvailable,
            CandidatesFileExists = true,
            SummaryFileExists = File.Exists(Path.Combine(input.CandidateInputDirectory, "cross-symbol-candidate-engine-v2-summary.json")),
            ExecutionPortfolioFileExists = File.Exists(Path.Combine(input.CandidateInputDirectory, "cross-symbol-candidate-engine-v2-execution-ready-portfolio.json")),
            Message = message
        };

        var riskRows = decisions
            .Select(d => new CrossSymbolShadowBridgeRiskRow
            {
                CandidateKey = d.CandidateKey,
                Symbol = d.Symbol,
                Category = d.Category,
                RiskStatus = d.Category == nameof(CrossSymbolShadowBridgeCandidateCategory.ExecutionReadyCandidates)
                    ? "ShadowEligibleNotPlacing"
                    : "Blocked",
                ReasonIfBlocked = d.ReasonIfBlocked,
                OrdersPermitted = false,
                TestnetOrdersPermitted = false,
                RealOrdersPermitted = false,
                ShadowOnly = true
            })
            .ToArray();

        return new CrossSymbolShadowBridgeRunResult
        {
            Status = status,
            Decisions = decisions,
            RiskRows = riskRows
        };
    }

    private static HashSet<string> BuildExecutionReadyKeySet(
        CrossSymbolShadowBridgeSettings settings,
        CrossSymbolShadowBridgeInputBundle input)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var breakdown = input.ExecutionReadyPortfolio?.Portfolio?.CandidateContributionBreakdown
                        ?? input.Summary?.ExecutionReadyPortfolio?.CandidateContributionBreakdown
                        ?? [];

        foreach (var row in breakdown)
        {
            if (!string.IsNullOrWhiteSpace(row.CandidateKey))
                keys.Add(row.CandidateKey);
        }

        if (keys.Count == 0)
        {
            foreach (var candidate in input.Candidates)
            {
                if (candidate.SelectedForExecutionReadyPortfolio)
                    keys.Add(candidate.CandidateKey);
            }
        }

        if (settings.RequireCanEnterTestnetOrderMode)
        {
            keys.IntersectWith(input.Candidates
                .Where(c => c.CanEnterTestnetOrderMode)
                .Select(c => c.CandidateKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
        }

        return keys;
    }

    private static Dictionary<string, decimal> BuildNotionalLookup(
        CrossSymbolShadowBridgeSettings settings,
        CrossSymbolShadowBridgeInputBundle input,
        HashSet<string> executionReadyKeys)
    {
        var lookup = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var breakdown = input.ExecutionReadyPortfolio?.Portfolio?.CandidateContributionBreakdown
                        ?? input.Summary?.ExecutionReadyPortfolio?.CandidateContributionBreakdown
                        ?? [];

        foreach (var row in breakdown)
        {
            if (!string.IsNullOrWhiteSpace(row.CandidateKey))
                lookup[row.CandidateKey] = Math.Min(row.AssignedNotionalUsdt, settings.MaxPerCandidateNotionalUsdt);
        }

        decimal total = 0m;
        var ranked = input.Candidates
            .Where(c => executionReadyKeys.Contains(c.CandidateKey))
            .OrderByDescending(c => c.AssignedShadowNotionalUsdt)
            .ThenBy(c => c.CandidateKey, StringComparer.OrdinalIgnoreCase)
            .Take(settings.MaxShadowCandidates)
            .ToArray();

        foreach (var candidate in ranked)
        {
            if (!lookup.TryGetValue(candidate.CandidateKey, out var notional) || notional <= 0m)
                notional = Math.Min(
                    candidate.AssignedShadowNotionalUsdt > 0m ? candidate.AssignedShadowNotionalUsdt : settings.MaxPerCandidateNotionalUsdt,
                    settings.MaxPerCandidateNotionalUsdt);

            if (total + notional > settings.MaxTotalShadowNotionalUsdt)
                notional = Math.Max(0m, settings.MaxTotalShadowNotionalUsdt - total);

            if (notional > 0m)
            {
                lookup[candidate.CandidateKey] = notional;
                total += notional;
            }
        }

        foreach (var candidate in input.Candidates.Where(c => !executionReadyKeys.Contains(c.CandidateKey)))
        {
            if (candidate.AssignedShadowNotionalUsdt > 0m)
                lookup[candidate.CandidateKey] = Math.Min(candidate.AssignedShadowNotionalUsdt, settings.MaxPerCandidateNotionalUsdt);
        }

        return lookup;
    }

    private static CrossSymbolShadowBridgeCandidateCategory ClassifyCandidate(
        CrossSymbolShadowBridgeSettings settings,
        CrossSymbolShadowBridgeCandidateImport candidate,
        string researchStatus,
        HashSet<string> executionReadyKeys)
    {
        var isExecutionReady = executionReadyKeys.Contains(candidate.CandidateKey)
                               && (!settings.RequireCanEnterTestnetOrderMode || candidate.CanEnterTestnetOrderMode);

        if (isExecutionReady)
            return CrossSymbolShadowBridgeCandidateCategory.ExecutionReadyCandidates;

        if (settings.AllowResearchPromotedShadowOnly
            && string.Equals(researchStatus, "PromoteToShadow", StringComparison.OrdinalIgnoreCase))
        {
            return CrossSymbolShadowBridgeCandidateCategory.ResearchPromotedShadowOnly;
        }

        return CrossSymbolShadowBridgeCandidateCategory.RejectedOrParked;
    }

    private static string BuildReasonIfBlocked(
        CrossSymbolShadowBridgeSettings settings,
        CrossSymbolShadowBridgeCandidateCategory category,
        CrossSymbolShadowBridgeCandidateImport candidate,
        string researchStatus,
        decimal assignedNotional)
    {
        return category switch
        {
            CrossSymbolShadowBridgeCandidateCategory.ExecutionReadyCandidates =>
                settings.EffectiveAllowTestnetOrders
                    ? "ExecutionReadyButTestnetOrdersDisabled"
                    : "ExecutionReadyShadowOnly; AllowTestnetOrders=false; WouldPlaceOrder=false",
            CrossSymbolShadowBridgeCandidateCategory.ResearchPromotedShadowOnly =>
                researchStatus == "PromoteToShadow" && !candidate.CanEnterTestnetOrderMode
                    ? $"ResearchPromotedShadowOnly; CanEnterTestnetOrderMode=false; readiness={candidate.CurrentExecutionReadiness}"
                    : "ResearchPromotedShadowOnly; orders never placed from research shadow lane",
            _ => string.IsNullOrWhiteSpace(candidate.ExecutionReadinessExplanation)
                ? $"RejectedOrParked; status={researchStatus}; readiness={candidate.CurrentExecutionReadiness}"
                : candidate.ExecutionReadinessExplanation
        };
    }

    private static string ResolveStatus(
        CrossSymbolShadowBridgeSettings settings,
        int executionReadyCount,
        int portfolioCount)
    {
        if (settings.BlockIfExecutionReadyPortfolioEmpty && (executionReadyCount == 0 || portfolioCount == 0))
            return "NoExecutionReadyCandidates";

        return executionReadyCount > 0 ? "ShadowBridgeActive" : "ShadowOnlyNoExecutionReady";
    }

    private static string BuildStatusMessage(int executionReady, int researchShadow, int rejected)
    {
        if (executionReady == 0)
            return "No execution-ready candidates. Shadow only. Orders blocked.";

        return $"ExecutionReady={executionReady}, ResearchShadowOnly={researchShadow}, RejectedOrParked={rejected}. Orders blocked (shadow dry-run only).";
    }

    public static CrossSymbolShadowBridgeRunResult BuildInputFilesMissingResult(
        CrossSymbolShadowBridgeSettings settings,
        CrossSymbolShadowBridgeInputProbe probe,
        string outputDirectory,
        DateTime evaluatedAtUtc)
    {
        var message = $"Required candidate engine V2 input files missing. Missing: {string.Join("; ", probe.MissingFiles)}";

        return new CrossSymbolShadowBridgeRunResult
        {
            Status = new CrossSymbolShadowBridgeStatus
            {
                EvaluatedAtUtc = evaluatedAtUtc,
                Status = "InputFilesMissing",
                Message = message,
                Enabled = settings.Enabled,
                ShadowOnly = settings.ShadowOnly,
                BacktestOnly = settings.BacktestOnly,
                DryRunOnly = settings.DryRunOnly,
                AllowOrders = settings.EffectiveAllowOrders,
                AllowTestnetOrders = settings.EffectiveAllowTestnetOrders,
                AllowRealOrders = settings.EffectiveAllowRealOrders,
                RealOrdersPlaced = settings.RealOrdersPlaced,
                LiveFuturesRecommended = settings.LiveFuturesRecommended,
                CandidateInputDirectory = probe.CandidateInputDirectory,
                OutputDirectory = outputDirectory,
                CandidatesFileExists = probe.CandidatesFileExists,
                SummaryFileExists = probe.SummaryFileExists,
                ExecutionPortfolioFileExists = probe.ExecutionPortfolioFileExists,
                MissingInputFiles = probe.MissingFiles
            },
            Decisions = [],
            RiskRows = []
        };
    }
}
