namespace TradingBot.Backtest;

/// <summary>
/// Current forward/shadow/bottleneck reality for overlay gating. Reporting only.
/// </summary>
public sealed record CrossSymbolCandidateForwardRealitySnapshot
{
    public string? MatchedFrozenProfileName { get; init; }
    public string MatchSource { get; init; } = "None";
    public int CurrentForwardTrades { get; init; }
    public decimal CurrentForwardNetModerate { get; init; }
    public decimal? CurrentForwardNetStressPlus { get; init; }
    public decimal CurrentForwardHealthScore { get; init; }
    public string CurrentBottleneckClassification { get; init; } = string.Empty;
    public string CurrentBottleneckRecommendation { get; init; } = string.Empty;
    public bool? LatestShadowActivationPassed { get; init; }
    public bool? LatestShadowEntrySignalPresent { get; init; }
    public bool LatestShadowWouldPlaceOrder { get; init; }
    public string LatestShadowRiskStatus { get; init; } = string.Empty;
    public string LatestShadowReasonIfBlocked { get; init; } = string.Empty;
    public string LatestShadowActivationReason { get; init; } = string.Empty;
    public bool ForwardEvidenceAvailable { get; init; }
    public string ForwardEvidenceNotes { get; init; } = string.Empty;
}

public sealed record CrossSymbolCandidateEngineV2ForwardRealityBundle
{
    public string? BottleneckAuditDirectory { get; init; }
    public string? ShadowRunnerDirectory { get; init; }
    public string IncubationOutputRoot { get; init; } = string.Empty;
    public IReadOnlyList<FrozenProfileBottleneckAuditRow> BottleneckAudit { get; init; } = [];
    public IReadOnlyList<CrossSymbolCandidateEngineV2ShadowDecisionImportRow> ShadowDecisions { get; init; } = [];
    public IReadOnlyDictionary<string, FrozenCandidateSummaryRow> ForwardSummariesByProfile { get; init; }
        = new Dictionary<string, FrozenCandidateSummaryRow>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, FuturesTestnetShadowForwardEvidenceLoader.ForwardEvidenceSnapshot> ForwardEvidenceByProfile { get; init; }
        = new Dictionary<string, FuturesTestnetShadowForwardEvidenceLoader.ForwardEvidenceSnapshot>(StringComparer.Ordinal);
}

/// <summary>JSON import shape for futures-testnet-shadow-decisions.json.</summary>
public sealed record CrossSymbolCandidateEngineV2ShadowDecisionImportRow
{
    public DateTime TimestampUtc { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public string Interval { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public bool ActivationPassed { get; init; }
    public string ActivationReason { get; init; } = string.Empty;
    public bool EntrySignalPresent { get; init; }
    public string EntryReason { get; init; } = string.Empty;
    public bool WouldPlaceOrder { get; init; }
    public string RiskStatus { get; init; } = string.Empty;
    public string ReasonIfBlocked { get; init; } = string.Empty;
    public int ForwardTradeCount { get; init; }
    public decimal? ForwardNetModerate { get; init; }
    public decimal? ForwardNetStressPlus { get; init; }
    public bool ForwardEvidencePassed { get; init; }
    public bool ShadowRunnerCanPlaceIfSignalAppears { get; init; }
}

public sealed record CrossSymbolCandidateEngineV2OverlayResult
{
    public string ResearchPromotionStatus { get; init; } = string.Empty;
    public string CurrentExecutionReadiness { get; init; } = string.Empty;
    public int CurrentForwardTrades { get; init; }
    public decimal CurrentForwardNetModerate { get; init; }
    public decimal? CurrentForwardNetStressPlus { get; init; }
    public decimal CurrentForwardHealthScore { get; init; }
    public string CurrentBottleneckClassification { get; init; } = string.Empty;
    public string CurrentBottleneckRecommendation { get; init; } = string.Empty;
    public bool? LatestShadowActivationPassed { get; init; }
    public bool? LatestShadowEntrySignalPresent { get; init; }
    public bool LatestShadowWouldPlaceOrder { get; init; }
    public string LatestShadowRiskStatus { get; init; } = string.Empty;
    public string LatestShadowReasonIfBlocked { get; init; } = string.Empty;
    public string ExecutionReadinessExplanation { get; init; } = string.Empty;
    public bool CanEnterTestnetOrderMode { get; init; }
    public string? MatchedFrozenProfileName { get; init; }
}

public static class CrossSymbolCandidateEngineV2ForwardRealityOverlayBuilder
{
    public static CrossSymbolCandidateEngineV2OverlayResult Apply(
        CrossSymbolCandidateEngineV2CandidateRow candidate,
        CrossSymbolCandidateEngineV2ForwardRealityBundle reality)
    {
        var snapshot = ResolveSnapshot(candidate, reality);
        var researchStatus = candidate.PromotionStatus;

        var readiness = ResolveCurrentExecutionReadiness(candidate, snapshot, researchStatus);
        var explanation = BuildExplanation(researchStatus, readiness, snapshot);
        var canEnterTestnet = ResolveCanEnterTestnetOrderMode(researchStatus, readiness, snapshot);

        return new CrossSymbolCandidateEngineV2OverlayResult
        {
            ResearchPromotionStatus = researchStatus,
            CurrentExecutionReadiness = readiness,
            CurrentForwardTrades = snapshot.CurrentForwardTrades,
            CurrentForwardNetModerate = snapshot.CurrentForwardNetModerate,
            CurrentForwardNetStressPlus = snapshot.CurrentForwardNetStressPlus,
            CurrentForwardHealthScore = snapshot.CurrentForwardHealthScore,
            CurrentBottleneckClassification = snapshot.CurrentBottleneckClassification,
            CurrentBottleneckRecommendation = snapshot.CurrentBottleneckRecommendation,
            LatestShadowActivationPassed = snapshot.LatestShadowActivationPassed,
            LatestShadowEntrySignalPresent = snapshot.LatestShadowEntrySignalPresent,
            LatestShadowWouldPlaceOrder = snapshot.LatestShadowWouldPlaceOrder,
            LatestShadowRiskStatus = snapshot.LatestShadowRiskStatus,
            LatestShadowReasonIfBlocked = snapshot.LatestShadowReasonIfBlocked,
            ExecutionReadinessExplanation = explanation,
            CanEnterTestnetOrderMode = canEnterTestnet,
            MatchedFrozenProfileName = snapshot.MatchedFrozenProfileName
        };
    }

    private static CrossSymbolCandidateForwardRealitySnapshot ResolveSnapshot(
        CrossSymbolCandidateEngineV2CandidateRow candidate,
        CrossSymbolCandidateEngineV2ForwardRealityBundle reality)
    {
        var profileName = ResolveMatchedProfileName(candidate);
        FrozenProfileBottleneckAuditRow? bottleneck = null;
        CrossSymbolCandidateEngineV2ShadowDecisionImportRow? shadow = null;
        FrozenCandidateSummaryRow? summary = null;
        FuturesTestnetShadowForwardEvidenceLoader.ForwardEvidenceSnapshot? evidence = null;
        var matchSource = "None";

        if (!string.IsNullOrEmpty(profileName))
        {
            bottleneck = reality.BottleneckAudit.FirstOrDefault(b =>
                string.Equals(b.ProfileName, profileName, StringComparison.Ordinal));
            shadow = reality.ShadowDecisions.FirstOrDefault(d =>
                string.Equals(d.ProfileName, profileName, StringComparison.Ordinal));
            reality.ForwardSummariesByProfile.TryGetValue(profileName, out summary);
            reality.ForwardEvidenceByProfile.TryGetValue(profileName, out evidence);
            matchSource = "FrozenProfileName";
        }

        if (bottleneck is null)
        {
            bottleneck = reality.BottleneckAudit.FirstOrDefault(MatchesCandidateScope(candidate));
            if (bottleneck is not null)
            {
                profileName ??= bottleneck.ProfileName;
                matchSource = matchSource == "None" ? "BottleneckScope" : matchSource;
            }
        }

        if (shadow is null && !string.IsNullOrEmpty(profileName))
            shadow = reality.ShadowDecisions.FirstOrDefault(d =>
                string.Equals(d.ProfileName, profileName, StringComparison.Ordinal));

        if (shadow is null)
            shadow = reality.ShadowDecisions.FirstOrDefault(MatchesShadowScope(candidate));

        var forwardTrades = evidence?.ForwardTradeCount
                            ?? shadow?.ForwardTradeCount
                            ?? summary?.ForwardTrades
                            ?? bottleneck?.ForwardTrades
                            ?? 0;
        var forwardModerate = evidence?.ForwardNetModerate
                              ?? shadow?.ForwardNetModerate
                              ?? summary?.ForwardNetModerate
                              ?? bottleneck?.NetModerate
                              ?? 0m;
        var forwardStress = evidence?.ForwardNetStressPlus
                            ?? shadow?.ForwardNetStressPlus
                            ?? bottleneck?.NetStressPlus;

        var healthScore = ComputeForwardHealthScore(
            forwardTrades, forwardModerate, forwardStress,
            shadow?.ActivationPassed, shadow?.EntrySignalPresent,
            bottleneck?.BottleneckClassification, bottleneck?.Recommendation);

        var notes = evidence?.Notes ?? string.Empty;
        if (evidence?.HasMismatch == true)
            notes = string.IsNullOrEmpty(notes) ? "ForwardEvidenceMismatch" : $"{notes}; ForwardEvidenceMismatch";

        return new CrossSymbolCandidateForwardRealitySnapshot
        {
            MatchedFrozenProfileName = profileName,
            MatchSource = matchSource,
            CurrentForwardTrades = forwardTrades,
            CurrentForwardNetModerate = forwardModerate,
            CurrentForwardNetStressPlus = forwardStress,
            CurrentForwardHealthScore = healthScore,
            CurrentBottleneckClassification = bottleneck?.BottleneckClassification ?? string.Empty,
            CurrentBottleneckRecommendation = bottleneck?.Recommendation ?? string.Empty,
            LatestShadowActivationPassed = shadow?.ActivationPassed ?? bottleneck?.ShadowActivationPassed,
            LatestShadowEntrySignalPresent = shadow?.EntrySignalPresent ?? bottleneck?.ShadowEntrySignalPresent,
            LatestShadowWouldPlaceOrder = shadow?.WouldPlaceOrder ?? false,
            LatestShadowRiskStatus = shadow?.RiskStatus ?? string.Empty,
            LatestShadowReasonIfBlocked = shadow?.ReasonIfBlocked ?? string.Empty,
            LatestShadowActivationReason = shadow?.ActivationReason ?? string.Empty,
            ForwardEvidenceAvailable = evidence?.IsValid == true && !evidence.IsMissingOrAmbiguous,
            ForwardEvidenceNotes = notes
        };
    }

    private static string? ResolveMatchedProfileName(CrossSymbolCandidateEngineV2CandidateRow candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.SuggestedFrozenProfileName))
            return candidate.SuggestedFrozenProfileName;

        return FuturesTestnetShadowCatalog.Profiles
            .FirstOrDefault(p =>
                string.Equals(p.Symbol.ToString(), candidate.Symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(p.Interval, candidate.Interval, StringComparison.OrdinalIgnoreCase)
                && p.ComboKey?.Direction.ToString().Equals(candidate.Direction, StringComparison.OrdinalIgnoreCase) == true
                && p.ComboKey.TargetPercent == candidate.TargetPercent
                && p.ComboKey.StopPercent == candidate.StopPercent)
            ?.ProfileName;
    }

    private static Func<FrozenProfileBottleneckAuditRow, bool> MatchesCandidateScope(CrossSymbolCandidateEngineV2CandidateRow candidate)
        => audit =>
            string.Equals(audit.Symbol, candidate.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(audit.Interval, candidate.Interval, StringComparison.OrdinalIgnoreCase)
            && string.Equals(audit.Direction, candidate.Direction, StringComparison.OrdinalIgnoreCase);

    private static Func<CrossSymbolCandidateEngineV2ShadowDecisionImportRow, bool> MatchesShadowScope(CrossSymbolCandidateEngineV2CandidateRow candidate)
        => d =>
            string.Equals(d.Symbol, candidate.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.Interval, candidate.Interval, StringComparison.OrdinalIgnoreCase)
            && string.Equals(d.Direction, candidate.Direction, StringComparison.OrdinalIgnoreCase);

    private static string ResolveCurrentExecutionReadiness(
        CrossSymbolCandidateEngineV2CandidateRow candidate,
        CrossSymbolCandidateForwardRealitySnapshot snapshot,
        string researchStatus)
    {
        if (string.Equals(snapshot.CurrentBottleneckClassification, "LookbackStarved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.CurrentBottleneckRecommendation, "Park", StringComparison.OrdinalIgnoreCase)
            || IsLookbackStarvationSignal(snapshot))
        {
            return "LookbackStarvedCurrent";
        }

        if (snapshot.CurrentForwardTrades > 0
            && snapshot.CurrentForwardNetModerate > 0m
            && snapshot.CurrentForwardNetStressPlus is <= 0m)
        {
            return "StressNegativeForward";
        }

        if (snapshot.LatestShadowRiskStatus.Contains("Blocked", StringComparison.OrdinalIgnoreCase)
            && (snapshot.LatestShadowReasonIfBlocked.Contains("Safety", StringComparison.OrdinalIgnoreCase)
                || snapshot.LatestShadowReasonIfBlocked.Contains("Key", StringComparison.OrdinalIgnoreCase)))
        {
            return "SafetyBlocked";
        }

        if (snapshot.LatestShadowActivationPassed == false)
            return "ActivationCurrentlyBlocked";

        if (snapshot.LatestShadowActivationPassed == true
            && snapshot.LatestShadowEntrySignalPresent == false)
        {
            return "EntrySignalCurrentlyMissing";
        }

        if (!snapshot.ForwardEvidenceAvailable && snapshot.CurrentForwardTrades < 5)
            return "ForwardEvidencePending";

        if (researchStatus == "PromoteToShadow"
            && snapshot.CurrentForwardTrades >= 5
            && snapshot.CurrentForwardNetModerate > 0m
            && snapshot.CurrentForwardNetStressPlus > 0m
            && snapshot.LatestShadowActivationPassed == true
            && (snapshot.LatestShadowEntrySignalPresent == true || snapshot.LatestShadowWouldPlaceOrder)
            && !IsRiskBlocked(snapshot))
        {
            return "ExecutableShadowCandidate";
        }

        if (string.IsNullOrEmpty(snapshot.MatchedFrozenProfileName))
            return "NotExecutable";

        return "NotExecutable";
    }

    private static bool IsLookbackStarvationSignal(CrossSymbolCandidateForwardRealitySnapshot snapshot)
        => snapshot.LatestShadowReasonIfBlocked.Contains("InsufficientLookbackTrades", StringComparison.OrdinalIgnoreCase)
           || snapshot.LatestShadowActivationReason.Contains("InsufficientLookbackTrades", StringComparison.OrdinalIgnoreCase);

    private static bool IsRiskBlocked(CrossSymbolCandidateForwardRealitySnapshot snapshot)
        => string.Equals(snapshot.LatestShadowRiskStatus, "Blocked", StringComparison.OrdinalIgnoreCase);

    private static bool ResolveCanEnterTestnetOrderMode(
        string researchStatus,
        string readiness,
        CrossSymbolCandidateForwardRealitySnapshot snapshot)
    {
        if (researchStatus != "PromoteToShadow")
            return false;
        if (snapshot.CurrentForwardTrades < 5)
            return false;
        if (snapshot.CurrentForwardNetModerate <= 0m)
            return false;
        if (snapshot.CurrentForwardNetStressPlus is not > 0m)
            return false;
        if (snapshot.LatestShadowEntrySignalPresent != true && !snapshot.LatestShadowWouldPlaceOrder)
            return false;
        if (IsRiskBlocked(snapshot))
            return false;
        if (string.Equals(snapshot.CurrentBottleneckClassification, "LookbackStarved", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(snapshot.CurrentBottleneckRecommendation, "Park", StringComparison.OrdinalIgnoreCase))
            return false;
        if (readiness is "LookbackStarvedCurrent" or "ActivationCurrentlyBlocked" or "EntrySignalCurrentlyMissing"
            or "StressNegativeForward" or "SafetyBlocked" or "NotExecutable" or "ForwardEvidencePending")
            return false;

        return readiness == "ExecutableShadowCandidate";
    }

    private static string BuildExplanation(
        string researchStatus,
        string readiness,
        CrossSymbolCandidateForwardRealitySnapshot snapshot)
    {
        if (researchStatus == "PromoteToShadow"
            && readiness is "LookbackStarvedCurrent" or "ActivationCurrentlyBlocked")
        {
            return $"Research promotes on discovery stats, but current forward reality is {readiness}. " +
                   $"Forward trades={snapshot.CurrentForwardTrades}, netModerate={snapshot.CurrentForwardNetModerate:F2}, " +
                   $"stressPlus={snapshot.CurrentForwardNetStressPlus?.ToString("F2") ?? "n/a"}. " +
                   $"Bottleneck={snapshot.CurrentBottleneckClassification}/{snapshot.CurrentBottleneckRecommendation}. " +
                   $"Shadow: activation={snapshot.LatestShadowActivationPassed}, entry={snapshot.LatestShadowEntrySignalPresent}, risk={snapshot.LatestShadowRiskStatus}.";
        }

        return $"Research={researchStatus}, execution={readiness}, forwardTrades={snapshot.CurrentForwardTrades}, " +
               $"forwardNet={snapshot.CurrentForwardNetModerate:F2}, forwardStress={snapshot.CurrentForwardNetStressPlus?.ToString("F2") ?? "n/a"}, " +
               $"bottleneck={snapshot.CurrentBottleneckClassification}/{snapshot.CurrentBottleneckRecommendation}, " +
               $"shadowActivation={snapshot.LatestShadowActivationPassed}, shadowEntry={snapshot.LatestShadowEntrySignalPresent}.";
    }

    private static decimal ComputeForwardHealthScore(
        int forwardTrades,
        decimal forwardModerate,
        decimal? forwardStress,
        bool? activationPassed,
        bool? entryPresent,
        string? bottleneckClassification,
        string? bottleneckRecommendation)
    {
        decimal score = 0m;
        if (forwardTrades >= 5) score += 20m;
        else if (forwardTrades > 0) score += 10m;
        if (forwardModerate > 0m) score += 20m;
        if (forwardStress > 0m) score += 20m;
        if (activationPassed == true) score += 15m;
        if (entryPresent == true) score += 15m;
        if (string.Equals(bottleneckClassification, "LookbackStarved", StringComparison.OrdinalIgnoreCase)) score -= 30m;
        if (string.Equals(bottleneckRecommendation, "Park", StringComparison.OrdinalIgnoreCase)) score -= 20m;
        return Math.Max(0m, Math.Min(100m, Math.Round(score, 2)));
    }
}
