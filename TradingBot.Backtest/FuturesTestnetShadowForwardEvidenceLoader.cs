using System.Text.Json;

namespace TradingBot.Backtest;

/// <summary>
/// Loads true forward-only evidence from forward-incubation history and cross-validates
/// against the stable incubation candidate summary. Never sums discovery/history runs.
/// </summary>
public static class FuturesTestnetShadowForwardEvidenceLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public sealed record ForwardEvidenceSnapshot(
        int ForwardTradeCount,
        decimal? ForwardNetModerate,
        decimal? ForwardNetStressPlus,
        DateTime? WindowStartUtc,
        DateTime? WindowEndUtc,
        string ForwardEvidenceSourceFile,
        string ForwardEvidenceSourceProfileName,
        bool ForwardEvidenceIsTrueForwardOnly,
        bool IsValid,
        bool IsMissingOrAmbiguous,
        bool HasMismatch,
        string Notes);

    public static ForwardEvidenceSnapshot Load(
        FuturesTestnetShadowCatalog.ProfileRef profile,
        string dataDirectory,
        string incubationOutputRoot,
        string expectedProfileName)
    {
        var historyPath = profile.ForwardHistoryPath(dataDirectory);
        var summaryPath = profile.ForwardIncubationSummaryPath(incubationOutputRoot);

        if (!File.Exists(historyPath))
        {
            return MissingOrAmbiguous(
                expectedProfileName,
                historyPath,
                $"Forward history missing: {historyPath}");
        }

        List<ForwardIncubationHistoryEntry>? history;
        try
        {
            history = JsonSerializer.Deserialize<List<ForwardIncubationHistoryEntry>>(
                File.ReadAllText(historyPath), JsonOptions);
        }
        catch (Exception ex)
        {
            return MissingOrAmbiguous(
                expectedProfileName,
                historyPath,
                $"Forward history unreadable: {ex.Message}");
        }

        if (history is null || history.Count == 0)
        {
            return MissingOrAmbiguous(
                expectedProfileName,
                historyPath,
                "Forward history is empty.");
        }

        var latest = history[^1];
        FrozenCandidateSummaryRow? summary = null;
        if (File.Exists(summaryPath))
        {
            try
            {
                summary = JsonSerializer.Deserialize<FrozenCandidateSummaryRow>(
                    File.ReadAllText(summaryPath), JsonOptions);
            }
            catch (Exception ex)
            {
                return MissingOrAmbiguous(
                    expectedProfileName,
                    historyPath,
                    $"Incubation summary unreadable ({summaryPath}): {ex.Message}");
            }
        }

        if (summary is not null
            && !string.Equals(summary.ProfileName, expectedProfileName, StringComparison.Ordinal))
        {
            return MissingOrAmbiguous(
                expectedProfileName,
                historyPath,
                $"Incubation summary profile mismatch: expected {expectedProfileName}, got {summary.ProfileName} ({summaryPath}).");
        }

        var tradeCount = latest.ForwardTrades;
        var netModerate = latest.ForwardNetModerate;
        var netStressPlus = latest.ForwardNetStressPlus;
        var hasMismatch = false;
        var notes = "Latest forward-incubation history entry (true forward-only snapshot).";

        if (summary is not null)
        {
            if (summary.ForwardTrades != tradeCount
                || summary.ForwardNetModerate != netModerate)
            {
                hasMismatch = true;
                notes = $"History/summary mismatch: history trades={tradeCount}, netModerate={netModerate:F8}; " +
                        $"summary trades={summary.ForwardTrades}, netModerate={summary.ForwardNetModerate:F8} " +
                        $"({summaryPath}).";
            }
            else
            {
                notes = $"Validated against incubation summary ({summaryPath}).";
            }
        }
        else
        {
            notes = $"Incubation summary not found ({summaryPath}); using latest forward history only.";
        }

        return new ForwardEvidenceSnapshot(
            tradeCount,
            netModerate,
            netStressPlus,
            latest.FrozenStartUtc,
            latest.ForwardWindowEndUtc,
            historyPath,
            expectedProfileName,
            ForwardEvidenceIsTrueForwardOnly: true,
            IsValid: true,
            IsMissingOrAmbiguous: false,
            HasMismatch: hasMismatch,
            Notes: notes);
    }

    public static string IncubationOutputRootFromDataDirectory(string dataDirectory)
    {
        var dataRoot = Path.GetFullPath(dataDirectory).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(Path.GetDirectoryName(dataRoot)!, "output");
    }

    public static bool ComputeForwardEvidencePassed(
        ForwardEvidenceSnapshot evidence,
        bool requireForwardTradeEvidence)
        => evidence.IsValid
           && !evidence.IsMissingOrAmbiguous
           && !evidence.HasMismatch
           && (!requireForwardTradeEvidence || evidence.ForwardTradeCount > 0);

    public static void ApplyForwardEvidenceBlocks(
        ForwardEvidenceSnapshot evidence,
        bool requireForwardTradeEvidence,
        List<string> blocks)
    {
        if (evidence.IsMissingOrAmbiguous)
            blocks.Add("ForwardEvidenceSourceMissingOrAmbiguous");
        if (evidence.HasMismatch)
            blocks.Add("ForwardEvidenceMismatch");
        if (requireForwardTradeEvidence
            && evidence.IsValid
            && !evidence.IsMissingOrAmbiguous
            && !evidence.HasMismatch
            && evidence.ForwardTradeCount <= 0)
        {
            blocks.Add("NoForwardTradeEvidence");
        }
    }

    private static ForwardEvidenceSnapshot MissingOrAmbiguous(
        string expectedProfileName,
        string sourceFile,
        string notes)
        => new(
            0,
            null,
            null,
            null,
            null,
            sourceFile,
            expectedProfileName,
            ForwardEvidenceIsTrueForwardOnly: false,
            IsValid: false,
            IsMissingOrAmbiguous: true,
            HasMismatch: false,
            Notes: notes);
}
