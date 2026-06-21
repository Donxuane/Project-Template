using System.Text.Json;

namespace TradingBot.Backtest;

public static class CrossCandidateExactEntryFrequencyStudyV1Loader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<CrossCandidateExactEntryFrequencyStudyV1InputBundle> LoadAsync(
        string v1InputDirectory,
        string? v2InputDirectory,
        string? scannerInputDirectory,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(v1InputDirectory))
            throw new DirectoryNotFoundException($"Cross-symbol V1 input directory not found: {v1InputDirectory}");

        var leaderboardPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-leaderboard.json");
        if (!File.Exists(leaderboardPath))
            throw new FileNotFoundException("Missing cross-symbol-v1-leaderboard.json", leaderboardPath);

        var leaderboard = JsonSerializer.Deserialize<List<CrossSymbolLeaderboardRow>>(
                              await File.ReadAllTextAsync(leaderboardPath, cancellationToken), JsonOptions)
                          ?? [];

        var periodsPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-periods.json");
        if (!File.Exists(periodsPath))
            throw new FileNotFoundException("Missing cross-symbol-v1-periods.json", periodsPath);

        var periods = JsonSerializer.Deserialize<List<CrossSymbolPeriodRow>>(
                          await File.ReadAllTextAsync(periodsPath, cancellationToken), JsonOptions)
                      ?? [];

        var tradesPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-trades.json");
        if (!File.Exists(tradesPath))
            throw new FileNotFoundException("Missing cross-symbol-v1-trades.json", tradesPath);

        var trades = JsonSerializer.Deserialize<List<CrossSymbolTradeRow>>(
                         await File.ReadAllTextAsync(tradesPath, cancellationToken), JsonOptions)
                     ?? [];

        var costPath = Path.Combine(v1InputDirectory, "cross-symbol-v1-cost-sensitivity.json");
        var costSensitivity = File.Exists(costPath)
            ? JsonSerializer.Deserialize<List<CrossSymbolCostSensitivityRow>>(
                  await File.ReadAllTextAsync(costPath, cancellationToken), JsonOptions) ?? []
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

        var v2ByKey = new Dictionary<string, CrossSymbolCandidateEngineV2CandidateRow>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(v2InputDirectory))
        {
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
        }

        var scannerByKey = new Dictionary<string, CurrentOpportunityScannerV1CandidateRow>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(scannerInputDirectory))
        {
            var scannerPath = Path.Combine(scannerInputDirectory, "current-opportunity-scanner-v1-candidates.json");
            if (File.Exists(scannerPath))
            {
                using var stream = File.OpenRead(scannerPath);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (doc.RootElement.TryGetProperty("candidates", out var candidatesElement))
                {
                    var rows = JsonSerializer.Deserialize<List<CurrentOpportunityScannerV1CandidateRow>>(
                                   candidatesElement.GetRawText(), JsonOptions)
                               ?? [];
                    foreach (var row in rows)
                        scannerByKey[row.CandidateKey] = row;
                }
            }
        }

        return new CrossCandidateExactEntryFrequencyStudyV1InputBundle
        {
            V1InputDirectory = v1InputDirectory,
            V2InputDirectory = v2InputDirectory,
            ScannerInputDirectory = scannerInputDirectory,
            StudyStartUtc = studyStart,
            Leaderboard = leaderboard,
            Periods = periods,
            Trades = trades,
            CostSensitivity = costSensitivity,
            V2ByKey = v2ByKey,
            ScannerByKey = scannerByKey
        };
    }

    public static (string V1InputDirectory, string V2InputDirectory, string ScannerInputDirectory, string OutputRoot)
        ResolveDefaultPaths(string outputDirectory, string? v1InputOverride, string? scannerInputOverride)
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

        var scannerDir = string.IsNullOrWhiteSpace(scannerInputOverride)
            ? Path.Combine(outputRoot, CurrentOpportunityScannerV1Catalog.DefaultOutputSubdir)
            : scannerInputOverride;
        if (!File.Exists(Path.Combine(scannerDir, "current-opportunity-scanner-v1-candidates.json")))
        {
            var fallback = Path.Combine(
                Directory.GetCurrentDirectory(),
                "TradingBot.Backtest",
                "output",
                CurrentOpportunityScannerV1Catalog.DefaultOutputSubdir);
            if (File.Exists(Path.Combine(fallback, "current-opportunity-scanner-v1-candidates.json")))
                scannerDir = fallback;
        }

        return (v1Dir, v2Dir, scannerDir, outputRoot);
    }
}
