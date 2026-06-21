using System.Text.Json;

namespace TradingBot.Backtest;

public static class CurrentOpportunityScannerV1Loader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<CurrentOpportunityScannerV1InputBundle> LoadAsync(
        string v1InputDirectory,
        string v2InputDirectory,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var leaderboardPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-leaderboard.json");
        if (!File.Exists(leaderboardPath))
            throw new FileNotFoundException("Missing cross-symbol-v1-leaderboard.json", leaderboardPath);

        var leaderboard = JsonSerializer.Deserialize<List<CrossSymbolLeaderboardRow>>(
                            await File.ReadAllTextAsync(leaderboardPath, cancellationToken), JsonOptions)
                        ?? [];

        DateTime? studyStart = null;
        var v1MetaPath = Path.Combine(v1InputDirectory, "run-metadata.json");
        if (File.Exists(v1MetaPath))
        {
            using var metaStream = File.OpenRead(v1MetaPath);
            var meta = await JsonSerializer.DeserializeAsync<JsonElement>(metaStream, JsonOptions, cancellationToken);
            if (meta.TryGetProperty("studyStartUtc", out var startProp)
                && DateTime.TryParse(startProp.GetString(), out var parsed))
            {
                studyStart = parsed.ToUniversalTime();
            }
        }

        var v2ByKey = new Dictionary<string, CrossSymbolCandidateEngineV2CandidateRow>(StringComparer.OrdinalIgnoreCase);
        var v2CandidatesPath = Path.Combine(v2InputDirectory, "cross-symbol-candidate-engine-v2-candidates.json");
        if (File.Exists(v2CandidatesPath))
        {
            using var stream = File.OpenRead(v2CandidatesPath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("candidates", out var candidatesElement))
            {
                var rows = JsonSerializer.Deserialize<List<CrossSymbolCandidateEngineV2CandidateRow>>(
                               candidatesElement.GetRawText(), JsonOptions)
                           ?? [];
                foreach (var row in rows)
                    v2ByKey[row.CandidateKey] = row;
            }
        }

        var bottleneckDir = Path.Combine(outputRoot, CurrentOpportunityScannerV1Catalog.DefaultBottleneckAuditSubdir);
        var bottleneck = await LoadBottleneckAuditAsync(bottleneckDir, cancellationToken);

        var shadowDir = Path.Combine(outputRoot, CurrentOpportunityScannerV1Catalog.DefaultShadowRunnerSubdir);
        var shadowByScope = await LoadShadowDecisionsAsync(shadowDir, cancellationToken);

        return new CurrentOpportunityScannerV1InputBundle
        {
            V1InputDirectory = v1InputDirectory,
            V2InputDirectory = v2InputDirectory,
            StudyStartUtc = studyStart,
            Leaderboard = leaderboard,
            V2ByKey = v2ByKey,
            BottleneckAudit = bottleneck,
            ShadowByScope = shadowByScope
        };
    }

    private static async Task<IReadOnlyList<FrozenProfileBottleneckAuditRow>> LoadBottleneckAuditAsync(
        string directory,
        CancellationToken cancellationToken)
    {
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
                           profiles.GetRawText(), JsonOptions)
                       ?? [];
            }
        }
        catch
        {
            return [];
        }

        return [];
    }

    private static async Task<IReadOnlyDictionary<string, CrossSymbolCandidateEngineV2ShadowDecisionImportRow>> LoadShadowDecisionsAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, "futures-testnet-shadow-decisions.json");
        if (!File.Exists(path))
            return new Dictionary<string, CrossSymbolCandidateEngineV2ShadowDecisionImportRow>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var text = await File.ReadAllTextAsync(path, cancellationToken);
            var rows = JsonSerializer.Deserialize<List<CrossSymbolCandidateEngineV2ShadowDecisionImportRow>>(text, JsonOptions)
                       ?? [];
            return rows.ToDictionary(
                r => ScopeKey(r.Symbol, r.Interval, r.Direction),
                StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, CrossSymbolCandidateEngineV2ShadowDecisionImportRow>(StringComparer.OrdinalIgnoreCase);
        }
    }

    internal static string ScopeKey(string symbol, string interval, string direction)
        => $"{symbol}|{interval}|{direction}";
}
