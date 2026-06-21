using System.Text.Json;

namespace TradingBot.Backtest;

public static class EntryNearMissAuditV1Loader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<EntryNearMissAuditV1InputBundle> LoadAsync(
        string scannerInputDirectory,
        string v1InputDirectory,
        string v2InputDirectory,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var candidatesPath = Path.Combine(scannerInputDirectory, "current-opportunity-scanner-v1-candidates.json");
        if (!File.Exists(candidatesPath))
            throw new FileNotFoundException("Missing current-opportunity-scanner-v1-candidates.json", candidatesPath);

        using var candidatesStream = File.OpenRead(candidatesPath);
        using var candidatesDoc = await JsonDocument.ParseAsync(candidatesStream, cancellationToken: cancellationToken);
        var scannerCandidates = candidatesDoc.RootElement.TryGetProperty("candidates", out var candidatesElement)
            ? JsonSerializer.Deserialize<List<CurrentOpportunityScannerV1CandidateRow>>(
                  candidatesElement.GetRawText(), JsonOptions) ?? []
            : [];

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

        var leaderboardByKey = new Dictionary<string, CrossSymbolLeaderboardRow>(StringComparer.OrdinalIgnoreCase);
        var leaderboardPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-leaderboard.json");
        if (File.Exists(leaderboardPath))
        {
            var leaderboard = JsonSerializer.Deserialize<List<CrossSymbolLeaderboardRow>>(
                                  await File.ReadAllTextAsync(leaderboardPath, cancellationToken), JsonOptions)
                              ?? [];
            foreach (var row in leaderboard)
            {
                var key = CrossSymbolCandidateEngineV2Catalog.CandidateKey(
                    row.Symbol, row.Interval, row.Direction,
                    row.TargetPercent, row.StopPercent, row.ActivationRule);
                leaderboardByKey[key] = row;
            }
        }

        var outputRootResolved = Path.GetDirectoryName(Path.GetFullPath(scannerInputDirectory)) ?? outputRoot;
        var bottleneckAuditDir = Path.Combine(outputRootResolved, CurrentOpportunityScannerV1Catalog.DefaultBottleneckAuditSubdir);
        var bottleneck = await LoadBottleneckAuditAsync(bottleneckAuditDir, cancellationToken);

        return new EntryNearMissAuditV1InputBundle
        {
            ScannerInputDirectory = scannerInputDirectory,
            V1InputDirectory = v1InputDirectory,
            V2InputDirectory = v2InputDirectory,
            StudyStartUtc = studyStart,
            ScannerCandidates = scannerCandidates,
            LeaderboardByKey = leaderboardByKey,
            BottleneckAudit = bottleneck
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
}
