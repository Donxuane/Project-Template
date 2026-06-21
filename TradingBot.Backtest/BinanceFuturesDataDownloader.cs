using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record FuturesDownloadOutcome(
    string Symbol,
    string SourceKey,
    bool Success,
    int RecordCount,
    string Message);

public sealed class BinanceFuturesDataDownloader
{
    private static readonly Uri BaseAddress = new(FuturesMarketDataCatalog.FuturesBaseUrl);
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(45);

    private sealed record NormRow(long T, IReadOnlyList<(string Key, string Value)> Fields);

    public async Task<IReadOnlyList<FuturesDownloadOutcome>> DownloadAllAsync(
        string dataDirectory,
        IReadOnlyList<TradingSymbol> symbols,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(FuturesMarketDataCatalog.FuturesDataDirectory(dataDirectory));
        var outcomes = new List<FuturesDownloadOutcome>();
        using var client = new HttpClient { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(30) };

        foreach (var symbol in symbols)
        {
            await RunSourceAsync(outcomes, dataDirectory, symbol, "funding",
                () => DownloadFundingAsync(client, symbol, startUtc, endUtc, cancellationToken), cancellationToken);
            await RunSourceAsync(outcomes, dataDirectory, symbol, "markPriceKlines",
                () => DownloadPriceKlinesAsync(client, "/fapi/v1/markPriceKlines", "symbol", symbol, startUtc, endUtc, cancellationToken), cancellationToken);
            await RunSourceAsync(outcomes, dataDirectory, symbol, "indexPriceKlines",
                () => DownloadPriceKlinesAsync(client, "/fapi/v1/indexPriceKlines", "pair", symbol, startUtc, endUtc, cancellationToken), cancellationToken);
            await RunSourceAsync(outcomes, dataDirectory, symbol, "openInterestHist",
                () => DownloadFuturesDataAsync(client, "/futures/data/openInterestHist", symbol,
                    el => new NormRow(GetLong(el, "timestamp"),
                    [("oi", Get(el, "sumOpenInterest")), ("oiValue", Get(el, "sumOpenInterestValue"))]), cancellationToken), cancellationToken);
            await RunSourceAsync(outcomes, dataDirectory, symbol, "takerLongShortRatio",
                () => DownloadFuturesDataAsync(client, "/futures/data/takerlongshortRatio", symbol,
                    el => new NormRow(GetLong(el, "timestamp"),
                    [("buySellRatio", Get(el, "buySellRatio")), ("buyVol", Get(el, "buyVol")), ("sellVol", Get(el, "sellVol"))]), cancellationToken), cancellationToken);
            await RunSourceAsync(outcomes, dataDirectory, symbol, "globalLongShortAccountRatio",
                () => DownloadFuturesDataAsync(client, "/futures/data/globalLongShortAccountRatio", symbol,
                    el => new NormRow(GetLong(el, "timestamp"),
                    [("longShortRatio", Get(el, "longShortRatio")), ("longAccount", Get(el, "longAccount")), ("shortAccount", Get(el, "shortAccount"))]), cancellationToken), cancellationToken);
            await RunSourceAsync(outcomes, dataDirectory, symbol, "topLongShortPositionRatio",
                () => DownloadFuturesDataAsync(client, "/futures/data/topLongShortPositionRatio", symbol,
                    el => new NormRow(GetLong(el, "timestamp"),
                    [("longShortRatio", Get(el, "longShortRatio")), ("longAccount", Get(el, "longAccount")), ("shortAccount", Get(el, "shortAccount"))]), cancellationToken), cancellationToken);
        }

        return outcomes;
    }

    private async Task RunSourceAsync(
        List<FuturesDownloadOutcome> outcomes,
        string dataDirectory,
        TradingSymbol symbol,
        string sourceKey,
        Func<Task<IReadOnlyList<NormRow>>> fetch,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(SourceTimeout);
            var rows = await fetch();
            var path = FuturesMarketDataCatalog.FilePath(dataDirectory, symbol, sourceKey);
            await WriteNormAsync(path, rows, cancellationToken);
            outcomes.Add(new FuturesDownloadOutcome(symbol.ToString(), sourceKey, true, rows.Count, "ok"));
        }
        catch (Exception ex)
        {
            outcomes.Add(new FuturesDownloadOutcome(symbol.ToString(), sourceKey, false, 0, ex.Message));
        }
    }

    private static async Task<IReadOnlyList<NormRow>> DownloadFundingAsync(
        HttpClient client, TradingSymbol symbol, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        var rows = new List<NormRow>();
        var cursor = new DateTimeOffset(startUtc).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(endUtc).ToUnixTimeMilliseconds();
        while (cursor < endMs)
        {
            var uri = $"/fapi/v1/fundingRate?symbol={symbol}&startTime={cursor}&endTime={endMs}&limit=1000";
            var page = await FetchArrayAsync(client, uri, ct);
            if (page.Count == 0)
                break;
            foreach (var el in page)
                rows.Add(new NormRow(GetLong(el, "fundingTime"), [("rate", Get(el, "fundingRate"))]));
            var last = GetLong(page[^1], "fundingTime");
            if (last + 1 <= cursor)
                break;
            cursor = last + 1;
            if (page.Count < 1000)
                break;
        }

        return rows;
    }

    private static async Task<IReadOnlyList<NormRow>> DownloadPriceKlinesAsync(
        HttpClient client, string endpoint, string symbolParam, TradingSymbol symbol,
        DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        var rows = new List<NormRow>();
        var cursor = new DateTimeOffset(startUtc).ToUnixTimeMilliseconds();
        var endMs = new DateTimeOffset(endUtc).ToUnixTimeMilliseconds();
        var interval = FuturesMarketDataCatalog.LimitedPeriod;
        while (cursor < endMs)
        {
            var uri = $"{endpoint}?{symbolParam}={symbol}&interval={interval}&startTime={cursor}&endTime={endMs}&limit=1500";
            var page = await FetchArrayAsync(client, uri, ct);
            if (page.Count == 0)
                break;
            foreach (var el in page)
            {
                if (el.ValueKind != JsonValueKind.Array || el.GetArrayLength() < 5)
                    continue;
                if (!el[0].TryGetInt64(out var openMs))
                    continue;
                rows.Add(new NormRow(openMs,
                    [("open", el[1].ToString()), ("high", el[2].ToString()), ("low", el[3].ToString()), ("close", el[4].ToString())]));
            }

            if (!page[^1][0].TryGetInt64(out var lastOpen))
                break;
            var next = lastOpen + 1;
            if (next <= cursor)
                break;
            cursor = next;
            if (page.Count < 1500)
                break;
        }

        return rows;
    }

    private static async Task<IReadOnlyList<NormRow>> DownloadFuturesDataAsync(
        HttpClient client, string endpoint, TradingSymbol symbol,
        Func<JsonElement, NormRow> map, CancellationToken ct)
    {
        // futures/data/* only retains ~30d; walk endTime backwards to gather what is retained.
        var rows = new Dictionary<long, NormRow>();
        long? endCursor = null;
        for (var page = 0; page < 8; page++)
        {
            var uri = $"{endpoint}?symbol={symbol}&period={FuturesMarketDataCatalog.LimitedPeriod}&limit=500"
                      + (endCursor.HasValue ? $"&endTime={endCursor.Value}" : string.Empty);
            var elements = await FetchArrayAsync(client, uri, ct);
            if (elements.Count == 0)
                break;
            var minT = long.MaxValue;
            foreach (var el in elements)
            {
                var row = map(el);
                rows[row.T] = row;
                if (row.T < minT)
                    minT = row.T;
            }

            if (minT == long.MaxValue || (endCursor.HasValue && minT >= endCursor.Value))
                break;
            endCursor = minT - 1;
            if (elements.Count < 500)
                break;
        }

        return rows.Values.OrderBy(r => r.T).ToArray();
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

    private static async Task WriteNormAsync(string path, IReadOnlyList<NormRow> rows, CancellationToken ct)
    {
        var ordered = rows
            .GroupBy(r => r.T)
            .Select(g => g.Last())
            .OrderBy(r => r.T)
            .ToArray();
        await using var stream = File.Create(path);
        await using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });
        writer.WriteStartArray();
        foreach (var row in ordered)
        {
            writer.WriteStartObject();
            writer.WriteNumber("t", row.T);
            foreach (var (key, value) in row.Fields)
                writer.WriteString(key, value);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        await writer.FlushAsync(ct);
    }

    private static string Get(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.ToString() : string.Empty;

    private static long GetLong(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v))
            return 0L;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n))
            return n;
        return long.TryParse(v.ToString(), out var parsed) ? parsed : 0L;
    }
}
