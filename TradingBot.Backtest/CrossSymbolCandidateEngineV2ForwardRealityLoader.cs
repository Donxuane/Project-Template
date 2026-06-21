using System.Text.Json;

namespace TradingBot.Backtest;

public static class CrossSymbolCandidateEngineV2ForwardRealityLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<CrossSymbolCandidateEngineV2ForwardRealityBundle> LoadAsync(
        string dataDirectory,
        string? bottleneckAuditDirectory,
        string? shadowRunnerDirectory,
        CancellationToken cancellationToken)
    {
        var incubationOutputRoot = FuturesTestnetShadowForwardEvidenceLoader.IncubationOutputRootFromDataDirectory(dataDirectory);
        var bottleneck = await LoadBottleneckAuditAsync(bottleneckAuditDirectory, cancellationToken);
        var shadowDecisions = await LoadShadowDecisionsAsync(shadowRunnerDirectory, cancellationToken);

        var summaries = new Dictionary<string, FrozenCandidateSummaryRow>(StringComparer.Ordinal);
        var forwardEvidence = new Dictionary<string, FuturesTestnetShadowForwardEvidenceLoader.ForwardEvidenceSnapshot>(StringComparer.Ordinal);

        foreach (var profile in FuturesTestnetShadowCatalog.Profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profileName = profile.ProfileName;
            var summaryPath = profile.ForwardIncubationSummaryPath(incubationOutputRoot);
            if (File.Exists(summaryPath))
            {
                try
                {
                    var summary = JsonSerializer.Deserialize<FrozenCandidateSummaryRow>(
                        await File.ReadAllTextAsync(summaryPath, cancellationToken), JsonOptions);
                    if (summary is not null)
                        summaries[profileName] = summary;
                }
                catch
                {
                    // Reporting-only: skip unreadable summary.
                }
            }

            forwardEvidence[profileName] = FuturesTestnetShadowForwardEvidenceLoader.Load(
                profile, dataDirectory, incubationOutputRoot, profileName);
        }

        return new CrossSymbolCandidateEngineV2ForwardRealityBundle
        {
            BottleneckAuditDirectory = bottleneckAuditDirectory,
            ShadowRunnerDirectory = shadowRunnerDirectory,
            IncubationOutputRoot = incubationOutputRoot,
            BottleneckAudit = bottleneck,
            ShadowDecisions = shadowDecisions,
            ForwardSummariesByProfile = summaries,
            ForwardEvidenceByProfile = forwardEvidence
        };
    }

    private static async Task<IReadOnlyList<FrozenProfileBottleneckAuditRow>> LoadBottleneckAuditAsync(
        string? directory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return [];

        var path = Path.Combine(directory, "frozen-profile-bottleneck-audit.json");
        if (!File.Exists(path))
            return [];

        try
        {
            using var stream = File.OpenRead(path);
            var doc = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOptions, cancellationToken);
            if (doc.TryGetProperty("profiles", out var profiles))
            {
                return JsonSerializer.Deserialize<List<FrozenProfileBottleneckAuditRow>>(
                    profiles.GetRawText(), JsonOptions) ?? [];
            }
        }
        catch
        {
            return [];
        }

        return [];
    }

    private static async Task<IReadOnlyList<CrossSymbolCandidateEngineV2ShadowDecisionImportRow>> LoadShadowDecisionsAsync(
        string? directory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return [];

        var path = Path.Combine(directory, "futures-testnet-shadow-decisions.json");
        if (!File.Exists(path))
            return [];

        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<List<CrossSymbolCandidateEngineV2ShadowDecisionImportRow>>(text, JsonOptions)
                   ?? [];
        }
        catch
        {
            return [];
        }
    }
}
