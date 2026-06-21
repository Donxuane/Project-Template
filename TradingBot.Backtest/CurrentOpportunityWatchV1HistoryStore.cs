using System.Text.Json;

namespace TradingBot.Backtest;

public static class CurrentOpportunityWatchV1HistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<IReadOnlyList<CurrentOpportunityWatchV1HistoryRow>> LoadAsync(
        string historyPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(historyPath))
            return [];

        try
        {
            using var stream = File.OpenRead(historyPath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("history", out var historyElement))
            {
                return JsonSerializer.Deserialize<List<CurrentOpportunityWatchV1HistoryRow>>(
                           historyElement.GetRawText(), JsonOptions)
                       ?? [];
            }
        }
        catch
        {
            return [];
        }

        return [];
    }

    public static IReadOnlyList<CurrentOpportunityWatchV1HistoryRow> Append(
        IReadOnlyList<CurrentOpportunityWatchV1HistoryRow> existing,
        CurrentOpportunityWatchV1HistoryRow row)
    {
        var merged = existing.Concat([row]).ToList();
        if (merged.Count > CurrentOpportunityWatchV1Catalog.MaxHistoryRows)
            merged = merged.Skip(merged.Count - CurrentOpportunityWatchV1Catalog.MaxHistoryRows).ToList();
        return merged;
    }
}
