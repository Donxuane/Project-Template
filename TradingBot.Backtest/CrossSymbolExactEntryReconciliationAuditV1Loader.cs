using System.Text.Json;

namespace TradingBot.Backtest;

public static class CrossSymbolExactEntryReconciliationAuditV1Loader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<CrossSymbolExactEntryReconciliationAuditV1InputBundle> LoadAsync(
        string v1InputDirectory,
        string? v2InputDirectory,
        string? frequencyInputDirectory,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(v1InputDirectory))
            throw new DirectoryNotFoundException($"Cross-symbol V1 input directory not found: {v1InputDirectory}");

        var leaderboardPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-leaderboard.json");
        var periodsPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-periods.json");
        var tradesPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-trades.json");

        if (!File.Exists(leaderboardPath))
            throw new FileNotFoundException("Missing cross-symbol-v1-leaderboard.json", leaderboardPath);
        if (!File.Exists(periodsPath))
            throw new FileNotFoundException("Missing cross-symbol-v1-periods.json", periodsPath);
        if (!File.Exists(tradesPath))
            throw new FileNotFoundException("Missing cross-symbol-v1-trades.json", tradesPath);

        var leaderboard = JsonSerializer.Deserialize<List<CrossSymbolLeaderboardRow>>(
                              await File.ReadAllTextAsync(leaderboardPath, cancellationToken), JsonOptions)
                          ?? [];
        var periods = JsonSerializer.Deserialize<List<CrossSymbolPeriodRow>>(
                          await File.ReadAllTextAsync(periodsPath, cancellationToken), JsonOptions)
                      ?? [];
        var trades = JsonSerializer.Deserialize<List<CrossSymbolTradeRow>>(
                         await File.ReadAllTextAsync(tradesPath, cancellationToken), JsonOptions)
                     ?? [];

        DateTime? studyStart = null;
        DateTime? studyEnd = null;
        var v1MetaPath = Path.Combine(v1InputDirectory, "run-metadata.json");
        if (File.Exists(v1MetaPath))
        {
            using var metaStream = File.OpenRead(v1MetaPath);
            var meta = await JsonSerializer.DeserializeAsync<JsonElement>(metaStream, JsonOptions, cancellationToken);
            if (meta.TryGetProperty("studyStartUtc", out var startProp)
                && DateTime.TryParse(startProp.GetString(), out var parsedStart))
            {
                studyStart = parsedStart.ToUniversalTime();
            }

            if (meta.TryGetProperty("studyEndUtc", out var endProp)
                && DateTime.TryParse(endProp.GetString(), out var parsedEnd))
            {
                studyEnd = parsedEnd.ToUniversalTime();
            }
        }

        var v2ByKey = new Dictionary<string, CrossSymbolCandidateEngineV2CandidateRow>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(v2InputDirectory))
        {
            var v2Path = Path.Combine(v2InputDirectory, "cross-symbol-candidate-engine-v2-candidates.json");
            if (File.Exists(v2Path))
            {
                using var stream = File.OpenRead(v2Path);
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
        }

        var frequencyByKey =
            new Dictionary<string, CrossCandidateExactEntryFrequencyStudyV1CandidateRow>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(frequencyInputDirectory))
        {
            var freqPath = Path.Combine(
                frequencyInputDirectory,
                "cross-candidate-exact-entry-frequency-v1-candidates.json");
            if (File.Exists(freqPath))
            {
                using var stream = File.OpenRead(freqPath);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (doc.RootElement.TryGetProperty("candidates", out var candidatesElement))
                {
                    var rows = JsonSerializer.Deserialize<List<CrossCandidateExactEntryFrequencyStudyV1CandidateRow>>(
                                   candidatesElement.GetRawText(), JsonOptions)
                               ?? [];
                    foreach (var row in rows)
                        frequencyByKey[row.CandidateKey] = row;
                }
            }

            var freqSummaryPath = Path.Combine(
                frequencyInputDirectory,
                "cross-candidate-exact-entry-frequency-v1-summary.json");
            if (File.Exists(freqSummaryPath))
            {
                using var stream = File.OpenRead(freqSummaryPath);
                var summary = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOptions, cancellationToken);
                if (studyStart is null
                    && summary.TryGetProperty("StudyStartUtc", out var fs)
                    && DateTime.TryParse(fs.GetString(), out var parsedFs))
                {
                    studyStart = parsedFs.ToUniversalTime();
                }

                if (studyEnd is null
                    && summary.TryGetProperty("StudyEndUtc", out var fe)
                    && DateTime.TryParse(fe.GetString(), out var parsedFe))
                {
                    studyEnd = parsedFe.ToUniversalTime();
                }
            }
        }

        return new CrossSymbolExactEntryReconciliationAuditV1InputBundle
        {
            V1InputDirectory = v1InputDirectory,
            V2InputDirectory = v2InputDirectory,
            FrequencyInputDirectory = frequencyInputDirectory,
            StudyStartUtc = studyStart,
            StudyEndUtc = studyEnd,
            Leaderboard = leaderboard,
            Periods = periods,
            Trades = trades,
            V2ByKey = v2ByKey,
            FrequencyByKey = frequencyByKey
        };
    }

    public static (string V1InputDirectory, string V2InputDirectory, string FrequencyInputDirectory, string OutputRoot)
        ResolveDefaultPaths(
            string outputDirectory,
            string? v1InputOverride,
            string? frequencyInputOverride)
    {
        var outputRoot = Path.GetDirectoryName(Path.GetFullPath(outputDirectory)) ?? outputDirectory;

        var v1Dir = string.IsNullOrWhiteSpace(v1InputOverride)
            ? Path.Combine(outputRoot, CrossSymbolCandidateEngineV2Catalog.DefaultV1InputSubdir)
            : v1InputOverride;
        if (!File.Exists(Path.Combine(v1Dir, "cross-symbol-v1-leaderboard.json")))
        {
            var fallback = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                CrossSymbolCandidateEngineV2Catalog.DefaultV1InputSubdir);
            if (File.Exists(Path.Combine(fallback, "cross-symbol-v1-leaderboard.json")))
                v1Dir = fallback;
        }

        var v2Dir = Path.Combine(outputRoot, CrossSymbolCandidateEngineV2Catalog.DefaultOutputSubdir);
        if (!File.Exists(Path.Combine(v2Dir, "cross-symbol-candidate-engine-v2-candidates.json")))
        {
            var fallback = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                CrossSymbolCandidateEngineV2Catalog.DefaultOutputSubdir);
            if (File.Exists(Path.Combine(fallback, "cross-symbol-candidate-engine-v2-candidates.json")))
                v2Dir = fallback;
        }

        var frequencyDir = string.IsNullOrWhiteSpace(frequencyInputOverride)
            ? Path.Combine(outputRoot, CrossSymbolExactEntryReconciliationAuditV1Catalog.DefaultFrequencyInputSubdir)
            : frequencyInputOverride;
        if (!File.Exists(Path.Combine(frequencyDir, "cross-candidate-exact-entry-frequency-v1-candidates.json")))
        {
            var fallback = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                CrossSymbolExactEntryReconciliationAuditV1Catalog.DefaultFrequencyInputSubdir);
            if (File.Exists(Path.Combine(fallback, "cross-candidate-exact-entry-frequency-v1-candidates.json")))
                frequencyDir = fallback;
        }

        return (v1Dir, v2Dir, frequencyDir, outputRoot);
    }
}
