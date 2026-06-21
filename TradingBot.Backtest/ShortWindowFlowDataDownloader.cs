using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Downloads free Binance Futures flow data for short-window research and MERGES it with any
/// previously collected local rows (so repeated runs accumulate history beyond the ~30d API limit).
/// Public endpoints only; no API key, no order placement, no account access.
/// </summary>
public sealed class ShortWindowFlowDataDownloader
{
    private static readonly Uri BaseAddress = new(FuturesMarketDataCatalog.FuturesBaseUrl);

    public const string Period5m = "5m";
    public const string Period30m = "30m";

    public static readonly string[] LimitedSourceBaseKeys =
    [
        "openInterestHist",
        "takerLongShortRatio",
        "globalLongShortAccountRatio",
        "topLongShortPositionRatio"
    ];

    public static string FineSourceKey(string baseKey) => baseKey + "5m";

    public async Task<IReadOnlyList<ShortWindowDownloadOutcome>> DownloadAllAsync(
        string dataDirectory,
        IReadOnlyList<TradingSymbol> symbols,
        DateTime fullWindowStartUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(FuturesMarketDataCatalog.FuturesDataDirectory(dataDirectory));
        var outcomes = new List<ShortWindowDownloadOutcome>();
        using var client = new HttpClient { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(30) };

        foreach (var symbol in symbols)
        {
            await RunAsync(outcomes, dataDirectory, symbol, "funding",
                () => DownloadFundingAsync(client, dataDirectory, symbol, fullWindowStartUtc, endUtc, cancellationToken));
            await RunAsync(outcomes, dataDirectory, symbol, "markPriceKlines",
                () => DownloadPriceKlinesAsync(client, dataDirectory, symbol, "markPriceKlines", "/fapi/v1/markPriceKlines", "symbol", fullWindowStartUtc, endUtc, cancellationToken));
            await RunAsync(outcomes, dataDirectory, symbol, "indexPriceKlines",
                () => DownloadPriceKlinesAsync(client, dataDirectory, symbol, "indexPriceKlines", "/fapi/v1/indexPriceKlines", "pair", fullWindowStartUtc, endUtc, cancellationToken));

            foreach (var baseKey in LimitedSourceBaseKeys)
            {
                var endpoint = EndpointFor(baseKey);
                await RunAsync(outcomes, dataDirectory, symbol, baseKey,
                    () => DownloadLimitedAsync(client, dataDirectory, symbol, baseKey, endpoint, Period30m, maxPages: 8, MapRow(baseKey), cancellationToken));
                await RunAsync(outcomes, dataDirectory, symbol, FineSourceKey(baseKey),
                    () => DownloadLimitedAsync(client, dataDirectory, symbol, FineSourceKey(baseKey), endpoint, Period5m, maxPages: 24, MapRow(baseKey), cancellationToken));
            }
        }

        return outcomes;
    }

    private static string EndpointFor(string baseKey) => baseKey switch
    {
        "openInterestHist" => "/futures/data/openInterestHist",
        "takerLongShortRatio" => "/futures/data/takerlongshortRatio",
        "globalLongShortAccountRatio" => "/futures/data/globalLongShortAccountRatio",
        "topLongShortPositionRatio" => "/futures/data/topLongShortPositionRatio",
        _ => throw new ArgumentOutOfRangeException(nameof(baseKey), baseKey, null)
    };

    private static Func<JsonElement, (long T, (string Key, string Value)[] Fields)> MapRow(string baseKey) => baseKey switch
    {
        "openInterestHist" => el => (GetLong(el, "timestamp"),
            [("oi", Get(el, "sumOpenInterest")), ("oiValue", Get(el, "sumOpenInterestValue"))]),
        "takerLongShortRatio" => el => (GetLong(el, "timestamp"),
            [("buySellRatio", Get(el, "buySellRatio")), ("buyVol", Get(el, "buyVol")), ("sellVol", Get(el, "sellVol"))]),
        _ => el => (GetLong(el, "timestamp"),
            [("longShortRatio", Get(el, "longShortRatio")), ("longAccount", Get(el, "longAccount")), ("shortAccount", Get(el, "shortAccount"))])
    };

    private static async Task RunAsync(
        List<ShortWindowDownloadOutcome> outcomes,
        string dataDirectory,
        TradingSymbol symbol,
        string sourceKey,
        Func<Task<(int Added, int Total)>> fetch)
    {
        try
        {
            var (added, total) = await fetch();
            outcomes.Add(new ShortWindowDownloadOutcome(symbol.ToString(), sourceKey, true, added, total, "ok"));
        }
        catch (Exception ex)
        {
            outcomes.Add(new ShortWindowDownloadOutcome(symbol.ToString(), sourceKey, false, 0, 0, ex.Message));
        }
    }

    private async Task<(int Added, int Total)> DownloadFundingAsync(
        HttpClient client, string dataDirectory, TradingSymbol symbol, DateTime windowStartUtc, DateTime endUtc, CancellationToken ct)
    {
        var existing = ReadExisting(dataDirectory, symbol, "funding");
        var startMs = existing.Count > 0
            ? existing.Keys.Max() + 1
            : new DateTimeOffset(windowStartUtc).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(endUtc).ToUnixTimeMilliseconds();
        var added = 0;
        var cursor = startMs;
        while (cursor < endMs)
        {
            var uri = $"/fapi/v1/fundingRate?symbol={symbol}&startTime={cursor}&endTime={endMs}&limit=1000";
            var page = await FetchArrayAsync(client, uri, ct);
            if (page.Count == 0)
                break;
            foreach (var el in page)
            {
                var t = GetLong(el, "fundingTime");
                if (t <= 0)
                    continue;
                if (existing.TryAdd(t, [("rate", Get(el, "fundingRate"))]))
                    added++;
            }

            var last = GetLong(page[^1], "fundingTime");
            if (last + 1 <= cursor)
                break;
            cursor = last + 1;
            if (page.Count < 1000)
                break;
        }

        await WriteMergedAsync(dataDirectory, symbol, "funding", existing, ct);
        return (added, existing.Count);
    }

    private async Task<(int Added, int Total)> DownloadPriceKlinesAsync(
        HttpClient client, string dataDirectory, TradingSymbol symbol, string sourceKey, string endpoint,
        string symbolParam, DateTime windowStartUtc, DateTime endUtc, CancellationToken ct)
    {
        var existing = ReadExisting(dataDirectory, symbol, sourceKey);
        var startMs = existing.Count > 0
            ? existing.Keys.Max() + 1
            : new DateTimeOffset(windowStartUtc).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(endUtc).ToUnixTimeMilliseconds();
        var added = 0;
        var cursor = startMs;
        while (cursor < endMs)
        {
            var uri = $"{endpoint}?{symbolParam}={symbol}&interval={Period30m}&startTime={cursor}&endTime={endMs}&limit=1500";
            var page = await FetchArrayAsync(client, uri, ct);
            if (page.Count == 0)
                break;
            long lastOpen = 0;
            foreach (var el in page)
            {
                if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() < 5 || !el[0].TryGetInt64(out var openMs))
                    continue;
                lastOpen = openMs;
                if (existing.TryAdd(openMs,
                    [("open", el[1].ToString()), ("high", el[2].ToString()), ("low", el[3].ToString()), ("close", el[4].ToString())]))
                    added++;
            }

            if (lastOpen + 1 <= cursor)
                break;
            cursor = lastOpen + 1;
            if (page.Count < 1500)
                break;
        }

        await WriteMergedAsync(dataDirectory, symbol, sourceKey, existing, ct);
        return (added, existing.Count);
    }

    private async Task<(int Added, int Total)> DownloadLimitedAsync(
        HttpClient client, string dataDirectory, TradingSymbol symbol, string sourceKey, string endpoint,
        string period, int maxPages, Func<JsonElement, (long T, (string Key, string Value)[] Fields)> map, CancellationToken ct)
    {
        var existing = ReadExisting(dataDirectory, symbol, sourceKey);
        var added = 0;
        long? endCursor = null;
        for (var page = 0; page < maxPages; page++)
        {
            var uri = $"{endpoint}?symbol={symbol}&period={period}&limit=500"
                      + (endCursor.HasValue ? $"&endTime={endCursor.Value}" : string.Empty);
            var elements = await FetchArrayAsync(client, uri, ct);
            if (elements.Count == 0)
                break;
            var minT = long.MaxValue;
            foreach (var el in elements)
            {
                var (t, fields) = map(el);
                if (t <= 0)
                    continue;
                if (existing.TryAdd(t, fields))
                    added++;
                if (t < minT)
                    minT = t;
            }

            if (minT == long.MaxValue || (endCursor.HasValue && minT >= endCursor.Value))
                break;
            endCursor = minT - 1;
            if (elements.Count < 500)
                break;
        }

        await WriteMergedAsync(dataDirectory, symbol, sourceKey, existing, ct);
        return (added, existing.Count);
    }

    private static Dictionary<long, (string Key, string Value)[]> ReadExisting(
        string dataDirectory, TradingSymbol symbol, string sourceKey)
    {
        var path = FuturesMarketDataCatalog.FilePath(dataDirectory, symbol, sourceKey);
        var rows = new Dictionary<long, (string Key, string Value)[]>();
        if (!File.Exists(path))
            return rows;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return rows;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            long t = 0;
            var fields = new List<(string Key, string Value)>();
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, "t", StringComparison.OrdinalIgnoreCase))
                    t = prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt64(out var n)
                        ? n
                        : long.TryParse(prop.Value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;
                else
                    fields.Add((prop.Name, prop.Value.ToString()));
            }

            if (t > 0)
                rows[t] = fields.ToArray();
        }

        return rows;
    }

    private static async Task WriteMergedAsync(
        string dataDirectory, TradingSymbol symbol, string sourceKey,
        Dictionary<long, (string Key, string Value)[]> rows, CancellationToken ct)
    {
        var path = FuturesMarketDataCatalog.FilePath(dataDirectory, symbol, sourceKey);
        await using var stream = File.Create(path);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();
        foreach (var (t, fields) in rows.OrderBy(r => r.Key))
        {
            writer.WriteStartObject();
            writer.WriteNumber("t", t);
            foreach (var (key, value) in fields)
                writer.WriteString(key, value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        await writer.FlushAsync(ct);
    }

    private static async Task<IReadOnlyList<JsonElement>> FetchArrayAsync(HttpClient client, string uri, CancellationToken ct)
    {
        using var response = await client.GetAsync(uri, ct);
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];
        return doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
    }

    private static string Get(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.ToString() : string.Empty;

    private static long GetLong(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
            return 0L;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
            return n;
        return long.TryParse(v.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;
    }
}
