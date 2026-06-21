using System.Text.Json;

namespace TradingBot.Backtest;

public sealed record CrossSymbolCandidateEngineV2InputBundle(
    IReadOnlyList<CrossSymbolLeaderboardRow> Leaderboard,
    IReadOnlyList<CrossSymbolCostSensitivityRow> CostSensitivity,
    IReadOnlyList<CrossSymbolSummaryRow> Summary,
    IReadOnlyList<MultiSymbolDataCoverageRow> DataCoverage,
    IReadOnlyList<CrossSymbolTradeRow> Trades,
    IReadOnlyList<CrossSymbolPeriodRow> Periods,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<FrozenProfileBottleneckAuditRow> BottleneckAudit);

public static class CrossSymbolCandidateEngineV2Loader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<CrossSymbolCandidateEngineV2InputBundle> LoadAsync(
        string v1InputDirectory,
        string? bottleneckAuditDirectory,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(v1InputDirectory))
            throw new DirectoryNotFoundException($"Cross-symbol V1 input directory not found: {v1InputDirectory}");

        var leaderboard = await ReadJsonAsync<List<CrossSymbolLeaderboardRow>>(
            Path.Combine(v1InputDirectory, "cross-symbol-v1-leaderboard.json"), cancellationToken) ?? [];
        var costSensitivity = await ReadJsonAsync<List<CrossSymbolCostSensitivityRow>>(
            Path.Combine(v1InputDirectory, "cross-symbol-v1-cost-sensitivity.json"), cancellationToken) ?? [];
        var summary = await ReadJsonAsync<List<CrossSymbolSummaryRow>>(
            Path.Combine(v1InputDirectory, "cross-symbol-v1-summary.json"), cancellationToken) ?? [];
        var dataCoverage = await ReadJsonAsync<List<MultiSymbolDataCoverageRow>>(
            Path.Combine(v1InputDirectory, "cross-symbol-v1-data-coverage.json"), cancellationToken) ?? [];
        var trades = await ReadJsonAsync<List<CrossSymbolTradeRow>>(
            Path.Combine(v1InputDirectory, "cross-symbol-v1-trades.json"), cancellationToken) ?? [];
        var periods = await ReadJsonAsync<List<CrossSymbolPeriodRow>>(
            Path.Combine(v1InputDirectory, "cross-symbol-v1-periods.json"), cancellationToken) ?? [];
        var answers = await ReadJsonAsync<List<ReachabilityResearchAnswer>>(
            Path.Combine(v1InputDirectory, "cross-symbol-v1-research-answers.json"), cancellationToken) ?? [];

        var bottleneck = await LoadBottleneckAuditAsync(bottleneckAuditDirectory, cancellationToken);

        return new CrossSymbolCandidateEngineV2InputBundle(
            leaderboard, costSensitivity, summary, dataCoverage, trades, periods, answers, bottleneck);
    }

    private static async Task<IReadOnlyList<FrozenProfileBottleneckAuditRow>> LoadBottleneckAuditAsync(
        string? bottleneckAuditDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bottleneckAuditDirectory))
            return [];

        var path = Path.Combine(bottleneckAuditDirectory, "frozen-profile-bottleneck-audit.json");
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

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required cross-symbol V1 input file not found: {path}");

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }
}
