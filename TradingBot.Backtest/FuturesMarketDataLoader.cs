using System.Globalization;
using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class FuturesMarketDataLoader(string dataDirectory)
{
    public bool Exists(TradingSymbol symbol, string sourceKey)
        => File.Exists(FuturesMarketDataCatalog.FilePath(dataDirectory, symbol, sourceKey));

    public IReadOnlyList<IReadOnlyDictionary<string, string>> LoadRaw(TradingSymbol symbol, string sourceKey)
    {
        var path = FuturesMarketDataCatalog.FilePath(dataDirectory, symbol, sourceKey);
        if (!File.Exists(path))
            return [];
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return [];
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object)
                continue;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in el.EnumerateObject())
                map[prop.Name] = prop.Value.ToString();
            rows.Add(map);
        }

        return rows;
    }

    public IReadOnlyList<FuturesFundingPoint> LoadFunding(TradingSymbol symbol)
        => LoadRaw(symbol, "funding")
            .Select(r => new FuturesFundingPoint(L(r, "t"), D(r, "rate")))
            .Where(p => p.TimestampMs > 0)
            .OrderBy(p => p.TimestampMs)
            .ToArray();

    public IReadOnlyList<FuturesPriceKlinePoint> LoadPriceKlines(TradingSymbol symbol, string sourceKey)
        => LoadRaw(symbol, sourceKey)
            .Select(r => new FuturesPriceKlinePoint(L(r, "t"), D(r, "open"), D(r, "high"), D(r, "low"), D(r, "close")))
            .Where(p => p.TimestampMs > 0)
            .OrderBy(p => p.TimestampMs)
            .ToArray();

    public IReadOnlyList<FuturesOiPoint> LoadOpenInterest(TradingSymbol symbol)
        => LoadRaw(symbol, "openInterestHist")
            .Select(r => new FuturesOiPoint(L(r, "t"), D(r, "oi"), D(r, "oiValue")))
            .Where(p => p.TimestampMs > 0)
            .OrderBy(p => p.TimestampMs)
            .ToArray();

    public IReadOnlyList<FuturesTakerPoint> LoadTaker(TradingSymbol symbol)
        => LoadRaw(symbol, "takerLongShortRatio")
            .Select(r => new FuturesTakerPoint(L(r, "t"), D(r, "buySellRatio"), D(r, "buyVol"), D(r, "sellVol")))
            .Where(p => p.TimestampMs > 0)
            .OrderBy(p => p.TimestampMs)
            .ToArray();

    public IReadOnlyList<FuturesLongShortPoint> LoadGlobalLongShort(TradingSymbol symbol)
        => LoadRaw(symbol, "globalLongShortAccountRatio")
            .Select(r => new FuturesLongShortPoint(L(r, "t"), D(r, "longShortRatio"), D(r, "longAccount"), D(r, "shortAccount")))
            .Where(p => p.TimestampMs > 0)
            .OrderBy(p => p.TimestampMs)
            .ToArray();

    private static long L(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) && long.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : 0L;

    private static decimal D(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
}
