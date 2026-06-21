using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Fixed-frequency forward-incubation application for a single promoted candidate (SOL 30m or ETH 15m).
/// Reuses the shared cross-symbol forward-incubation simulation, then applies the fixed-frequency
/// health gates and verdict. Freezes at the current run timestamp so only trades strictly after the
/// freeze count as forward proof. Diagnostic/research only: never places orders, never enables
/// testnet/live trading, never modifies the existing frozen BNB 5m, SOL 5m, BNB 15m, or SOL 15m tracks.
/// </summary>
public sealed class FixedFrequencyForwardIncubationV1Application(BacktestSettings settings, FixedFrequencyTrack track)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<FixedFrequencyForwardIncubationV1RunResult> RunAsync(CancellationToken cancellationToken)
    {
        var core = await CrossSymbolForwardIncubationRunner.RunAsync(
            settings, track.CatalogRefs,
            includeBnb5mProtection: true,
            includeSol5mProtection: true,
            includeBnb15mProtection: true,
            includeSol15mProtection: true,
            cancellationToken);

        var exactEntryPresent = TryReadCurrentExactEntryPresent();

        var result = FixedFrequencyForwardIncubationV1Engine.Build(
            core,
            track.CatalogRefs.FrozenComboKey,
            track.CatalogRefs.BuildFrozenActivationConfig().ActivationRuleName,
            exactEntryPresent,
            track.CatalogRefs.TrackLabel,
            settings.OutputDirectory);

        Directory.CreateDirectory(settings.OutputDirectory);
        await new FixedFrequencyForwardIncubationV1ReportWriter(settings.OutputDirectory)
            .WriteAsync(result, cancellationToken);
        await WriteRunMetadataAsync(core, result, exactEntryPresent, cancellationToken);
        return result;
    }

    private bool TryReadCurrentExactEntryPresent()
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(settings.OutputDirectory));
        if (parent is null)
            return false;

        var watchlistPath = Path.Combine(
            parent,
            CurrentOpportunityWatchV1Catalog.DefaultOutputSubdir,
            "current-opportunity-watch-v1-fixed-frequency-watchlist.json");
        if (!File.Exists(watchlistPath))
            return false;

        try
        {
            using var stream = File.OpenRead(watchlistPath);
            var doc = JsonSerializer.Deserialize<FixedFrequencyWatchlistDocument>(stream, ReadOptions);
            var row = doc?.Watchlist?.FirstOrDefault(r =>
                string.Equals(r.CandidateKey, track.WatchlistCandidateKey, StringComparison.OrdinalIgnoreCase));
            // An exact entry is "present" only when the base entry signal currently fires (shadow actionable).
            return row is not null && (row.BaseEntrySignalPresentNow || row.ActionableShadow);
        }
        catch
        {
            return false;
        }
    }

    private async Task WriteRunMetadataAsync(
        CrossSymbolForwardIncubationRunResult core,
        FixedFrequencyForwardIncubationV1RunResult result,
        bool exactEntryPresent,
        CancellationToken cancellationToken)
    {
        var summary = result.Summary;
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = track.ModeName,
                settings.DataDirectory,
                settings.OutputDirectory,
                frozenProfile = core.FrozenProfileName,
                frozenStatePath = core.FrozenStatePath,
                forwardHistoryPath = core.ForwardHistoryPath,
                forwardHistoryRunCount = core.History.Count,
                frozenStartUtc = summary.FrozenStartUtc,
                forwardWindowStartUtc = summary.ForwardWindowStartUtc,
                forwardWindowEndUtc = summary.ForwardWindowEndUtc,
                forwardSpanDays = summary.ForwardSpanDays,
                forwardTrades = summary.ForwardTrades,
                newTradesSincePreviousRun = summary.NewTradesSincePreviousRun,
                netModerate = summary.NetModerate,
                netStressPlus = summary.NetStressPlus,
                winRate = summary.WinRate,
                profitFactor = summary.ProfitFactor,
                maxDrawdown = summary.MaxDrawdown,
                maxConsecutiveLosses = summary.MaxConsecutiveLosses,
                activationCheckpointCount = summary.ActivationCheckpointCount,
                activatedCheckpointCount = summary.ActivatedCheckpointCount,
                activationFailedCheckpointCount = summary.ActivationFailedCheckpointCount,
                activatedButNoEntryCount = summary.ActivatedButNoEntryCount,
                baseSignalsInsideForwardWindow = summary.BaseSignalsInsideForwardWindow,
                baseSignalsInsideActivatedWindows = summary.BaseSignalsInsideActivatedWindows,
                currentExactEntryPresent = exactEntryPresent,
                latestStatus = summary.LatestStatus,
                healthScore = summary.HealthScore,
                failedHealthGates = summary.FailedHealthGates,
                verdict = summary.Verdict,
                nextAction = summary.NextAction,
                testnetOrderCandidate = summary.TestnetOrderCandidate,
                protectedFrozenFilesByteIdentical = core.ProtectedFrozenFilesByteIdentical,
                protectedFilesChecked = core.ProtectedFilesChecked,
                bootstrapAttempted = core.BootstrapAttempted,
                downloadOutcomes = core.DownloadOutcomes.Select(o => new { o.Symbol, o.SourceKey, o.Success, o.AddedCount, o.TotalCount, o.Message }),
                forwardOnlyJudgment = true,
                discoveryEvidenceIsNotForwardProof = true,
                noNewOptimization = true,
                frozenRuleOnly = true,
                backtestOnly = true,
                liveFuturesRecommended = false,
                testnetOrdersEnabled = false,
                liveTradingEnabled = false,
                realOrdersPlaced = false,
                paidDataUsed = false
            }, JsonOptions),
            cancellationToken);
    }

    private sealed record FixedFrequencyWatchlistDocument
    {
        public List<FixedFrequencyWatchlistEntry>? Watchlist { get; init; }
    }

    private sealed record FixedFrequencyWatchlistEntry
    {
        public string CandidateKey { get; init; } = string.Empty;
        public bool BaseEntrySignalPresentNow { get; init; }
        public bool ActionableShadow { get; init; }
    }
}
