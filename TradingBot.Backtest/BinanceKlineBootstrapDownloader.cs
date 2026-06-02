using System.Text.Json;
using System.Globalization;

namespace TradingBot.Backtest;

public sealed class BinanceKlineBootstrapDownloader
{
    private static readonly Uri BaseAddress = new("https://api.binance.com");
    private const int MaxKlineLimit = 1000;

    public async Task<BootstrapMergeResult> DownloadAndMergeToJsonAsync(
        string symbol,
        string outputPath,
        int limit,
        DateTime? startUtc,
        DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(30) };
        var existing = await ReadExistingCacheAsync(outputPath, cancellationToken);
        var downloaded = await DownloadPagesAsync(client, symbol, limit, startUtc, endUtc, cancellationToken);
        var merged = MergeByOpenTime(existing, downloaded);
        await WriteCacheAsync(outputPath, merged, cancellationToken);
        return new BootstrapMergeResult(
            ExistingCount: existing.Count,
            DownloadedCount: downloaded.Count,
            MergedCount: merged.Count,
            NewUniqueCount: Math.Max(0, merged.Count - existing.Count));
    }

    public static IReadOnlyList<KlineWireRow> MergeByOpenTime(
        IReadOnlyList<KlineWireRow> existing,
        IReadOnlyList<KlineWireRow> downloaded)
    {
        var byOpenTime = new Dictionary<long, KlineWireRow>();
        foreach (var row in existing)
            byOpenTime[row.OpenTimeMs] = row;
        foreach (var row in downloaded)
            byOpenTime[row.OpenTimeMs] = row;
        return byOpenTime.Values
            .OrderBy(x => x.OpenTimeMs)
            .ToArray();
    }

    private static async Task<IReadOnlyList<KlineWireRow>> DownloadPagesAsync(
        HttpClient client,
        string symbol,
        int limit,
        DateTime? startUtc,
        DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        var clampedLimit = Math.Clamp(limit, 1, MaxKlineLimit);
        if (!startUtc.HasValue || !endUtc.HasValue)
        {
            var uri = $"/api/v3/klines?symbol={symbol}&interval=1m&limit={clampedLimit}";
            var onePage = await FetchPageAsync(client, uri, cancellationToken);
            return onePage;
        }

        var rows = new List<KlineWireRow>();
        var cursor = new DateTimeOffset(startUtc.Value).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(endUtc.Value).ToUnixTimeMilliseconds();
        while (cursor < endMs)
        {
            var uri = $"/api/v3/klines?symbol={symbol}&interval=1m&limit={clampedLimit}&startTime={cursor}&endTime={endMs}";
            var page = await FetchPageAsync(client, uri, cancellationToken);
            if (page.Count == 0)
                break;

            rows.AddRange(page);
            var lastOpen = page[^1].OpenTimeMs;
            var nextCursor = lastOpen + 60_000L;
            if (nextCursor <= cursor)
                break;

            cursor = nextCursor;
            if (page.Count < clampedLimit)
                break;
        }

        return rows;
    }

    private static async Task<IReadOnlyList<KlineWireRow>> FetchPageAsync(
        HttpClient client,
        string uri,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var rows = new List<KlineWireRow>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 6)
                continue;
            if (!item[0].TryGetInt64(out var openTimeMs))
                continue;

            rows.Add(new KlineWireRow(
                openTimeMs,
                item[1].ToString(),
                item[2].ToString(),
                item[3].ToString(),
                item[4].ToString(),
                item[5].ToString()));
        }

        return rows.OrderBy(x => x.OpenTimeMs).ToArray();
    }

    private static async Task<IReadOnlyList<KlineWireRow>> ReadExistingCacheAsync(string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(outputPath))
            return [];

        await using var stream = File.OpenRead(outputPath);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var rows = new List<KlineWireRow>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 6)
                continue;

            if (!TryParseLong(item[0], out var openTime))
                continue;
            if (!TryGetText(item[1], out var open))
                continue;
            if (!TryGetText(item[2], out var high))
                continue;
            if (!TryGetText(item[3], out var low))
                continue;
            if (!TryGetText(item[4], out var close))
                continue;
            if (!TryGetText(item[5], out var volume))
                continue;

            rows.Add(new KlineWireRow(openTime, open, high, low, close, volume));
        }

        return rows.OrderBy(x => x.OpenTimeMs).ToArray();
    }

    private static async Task WriteCacheAsync(string outputPath, IReadOnlyList<KlineWireRow> rows, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(outputPath);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        foreach (var row in rows)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(row.OpenTimeMs);
            writer.WriteStringValue(row.Open);
            writer.WriteStringValue(row.High);
            writer.WriteStringValue(row.Low);
            writer.WriteStringValue(row.Close);
            writer.WriteStringValue(row.Volume);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken);
    }

    private static bool TryGetText(JsonElement element, out string value)
    {
        value = element.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParseLong(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
            return true;
        if (element.ValueKind == JsonValueKind.String
            && long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return true;
        value = default;
        return false;
    }
}

public sealed record KlineWireRow(
    long OpenTimeMs,
    string Open,
    string High,
    string Low,
    string Close,
    string Volume);

public sealed record BootstrapMergeResult(
    int ExistingCount,
    int DownloadedCount,
    int MergedCount,
    int NewUniqueCount);
