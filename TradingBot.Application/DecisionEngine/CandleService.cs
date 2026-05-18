using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Application.DecisionEngine;

public class CandleService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<CandleService> logger) : ICandleService
{
    private const string ConfigSection = "DecisionEngine:Candles";
    private const int MaxKlineLimit = 1000;

    private readonly string _interval = configuration[$"{ConfigSection}:Interval"] ?? "1m";
    private readonly int _refreshLimit = GetInt(configuration, $"{ConfigSection}:RefreshLimit", 2, 1);
    private readonly int _backfillPadding = GetInt(configuration, $"{ConfigSection}:BackfillPadding", 5, 0);
    private readonly int _maxBufferSize = GetInt(configuration, $"{ConfigSection}:MaxBufferSize", 300, 50);

    private readonly ConcurrentDictionary<TradingSymbol, SymbolCandleBuffer> _buffers = new();

    public int GetBufferedCount(TradingSymbol symbol)
    {
        if (!_buffers.TryGetValue(symbol, out var buffer))
            return 0;

        lock (buffer.SyncRoot)
        {
            return buffer.Candles.Count;
        }
    }

    public async Task<int> EnsureCandlesAsync(TradingSymbol symbol, int requiredCandles, CancellationToken cancellationToken = default)
    {
        var required = Math.Max(requiredCandles, 1);
        var current = GetBufferedCount(symbol);
        if (current >= required)
            return current;

        var fetchLimit = Math.Min(MaxKlineLimit, Math.Max(required + _backfillPadding, required - current + _backfillPadding));
        var fetched = await FetchCandlesAsync(symbol, fetchLimit, cancellationToken);
        if (fetched.Count == 0)
            return current;

        var buffer = _buffers.GetOrAdd(symbol, _ => new SymbolCandleBuffer());
        lock (buffer.SyncRoot)
        {
            buffer.Replace(fetched, _maxBufferSize);
            return buffer.Candles.Count;
        }
    }

    public async Task<MarketSnapshot?> GetSnapshotAsync(TradingSymbol symbol, int requiredCandles, CancellationToken cancellationToken = default)
    {
        await EnsureCandlesAsync(symbol, requiredCandles, cancellationToken);
        await RefreshLatestCandlesAsync(symbol, cancellationToken);
        var count = await EnsureCandlesAsync(symbol, requiredCandles, cancellationToken);

        if (!_buffers.TryGetValue(symbol, out var buffer))
            return null;

        IReadOnlyList<CandlePoint> candles;
        lock (buffer.SyncRoot)
        {
            if (buffer.Candles.Count == 0)
                return null;

            candles = buffer.Candles.ToArray();
        }

        var latestClosedCandle = ResolveLatestClosedCandle(candles);
        var fallbackPrice = latestClosedCandle?.Close ?? candles[^1].Close;
        var currentPrice = await GetCurrentPriceAsync(symbol, fallbackPrice, latestClosedCandle?.CloseTimeUtc, cancellationToken);
        if (count < requiredCandles)
        {
            logger.LogWarning(
                "CandleService snapshot below required window: Symbol={Symbol}, Current={Current}, Required={Required}. Backfill attempted.",
                symbol, count, requiredCandles);
        }

        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = currentPrice.Price,
            CurrentPriceSource = currentPrice.Source,
            CurrentPriceAsOfUtc = currentPrice.AsOfUtc,
            MarketDataAgeSeconds = currentPrice.AgeSeconds,
            LatestClosedCandleOpenTimeUtc = latestClosedCandle?.OpenTimeUtc,
            LatestClosedCandleCloseTimeUtc = latestClosedCandle?.CloseTimeUtc,
            LatestClosedCandleClosePrice = latestClosedCandle?.Close,
            HighPrices = candles.Select(c => c.High).ToArray(),
            LowPrices = candles.Select(c => c.Low).ToArray(),
            ClosePrices = candles.Select(c => c.Close).ToArray(),
            Volumes = candles.Select(c => c.Volume).ToArray(),
            TimestampUtc = DateTime.UtcNow
        };
    }

    private async Task RefreshLatestCandlesAsync(TradingSymbol symbol, CancellationToken cancellationToken)
    {
        var fetched = await FetchCandlesAsync(symbol, Math.Min(_refreshLimit, MaxKlineLimit), cancellationToken);
        if (fetched.Count == 0)
            return;

        var buffer = _buffers.GetOrAdd(symbol, _ => new SymbolCandleBuffer());
        lock (buffer.SyncRoot)
        {
            buffer.AppendOrUpdateLatest(fetched, _maxBufferSize);
        }
    }

    private async Task<ResolvedCurrentPrice> GetCurrentPriceAsync(
        TradingSymbol symbol,
        decimal fallbackPrice,
        DateTime? fallbackAsOfUtc,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var priceCacheService = scope.ServiceProvider.GetRequiredService<IPriceCacheService>();
        var cached = await priceCacheService.GetCachedPriceSnapshotAsync(symbol, cancellationToken);
        if (cached is not null && cached.Price > 0m)
        {
            var ageSeconds = Math.Max(0m, (decimal)(DateTime.UtcNow - cached.AsOfUtc).TotalSeconds);
            return new ResolvedCurrentPrice(
                cached.Price,
                string.IsNullOrWhiteSpace(cached.Source) ? "RedisTicker" : cached.Source,
                cached.AsOfUtc,
                ageSeconds);
        }

        var fallback = fallbackPrice > 0m ? fallbackPrice : 0m;
        var fallbackTimestamp = fallbackAsOfUtc ?? DateTime.UtcNow;
        var fallbackAgeSeconds = Math.Max(0m, (decimal)(DateTime.UtcNow - fallbackTimestamp).TotalSeconds);
        return new ResolvedCurrentPrice(
            fallback,
            "KlineFallback",
            fallbackTimestamp,
            fallbackAgeSeconds);
    }

    private async Task<IReadOnlyList<CandlePoint>> FetchCandlesAsync(TradingSymbol symbol, int limit, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var toolService = scope.ServiceProvider.GetRequiredService<IToolService>();
            var endpoint = toolService.BinanceEndpointsService.GetEndpoint(MarketData.CandlestickDataKline);
            var request = new KlineRequest
            {
                Symbol = symbol.ToString(),
                Interval = _interval,
                Limit = Math.Clamp(limit, 1, MaxKlineLimit)
            };

            var raw = await toolService.BinanceClientService.Call<JsonElement, KlineRequest>(request, endpoint, false);
            return ParseCandles(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CandleService fetch failed for {Symbol}.", symbol);
            return [];
        }
    }

    private static IReadOnlyList<CandlePoint> ParseCandles(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array)
            return [];

        var candles = new List<CandlePoint>();
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() <= 6)
                continue;

            if (!TryParseUnixMs(item[0], out var openTimeMs))
                continue;
            if (!TryParseDecimal(item[2], out var high))
                continue;
            if (!TryParseDecimal(item[3], out var low))
                continue;
            if (!TryParseDecimal(item[4], out var close))
                continue;
            if (!TryParseDecimal(item[5], out var volume))
                continue;
            if (!TryParseUnixMs(item[6], out var closeTimeMs))
                continue;

            candles.Add(new CandlePoint(
                DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs).UtcDateTime,
                DateTimeOffset.FromUnixTimeMilliseconds(closeTimeMs).UtcDateTime,
                high,
                low,
                close,
                volume));
        }

        return candles.OrderBy(x => x.OpenTimeUtc).ToArray();
    }

    private static bool TryParseUnixMs(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out value))
            return true;

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryParseDecimal(JsonElement element, out decimal value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDecimal(out value);
            case JsonValueKind.String:
                return decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            default:
                value = default;
                return false;
        }
    }

    private static int GetInt(IConfiguration configuration, string key, int defaultValue, int minValue)
    {
        var raw = configuration[key];
        if (!int.TryParse(raw, out var value))
            return defaultValue;

        return Math.Max(value, minValue);
    }

    private static CandlePoint? ResolveLatestClosedCandle(IReadOnlyList<CandlePoint> candles)
    {
        if (candles.Count == 0)
            return null;

        var nowUtc = DateTime.UtcNow;
        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].CloseTimeUtc <= nowUtc)
                return candles[i];
        }

        return candles[^1];
    }

    private sealed class SymbolCandleBuffer
    {
        public object SyncRoot { get; } = new();
        public List<CandlePoint> Candles { get; } = [];

        public void Replace(IReadOnlyList<CandlePoint> candles, int maxSize)
        {
            Candles.Clear();
            Candles.AddRange(candles.TakeLast(maxSize));
        }

        public void AppendOrUpdateLatest(IReadOnlyList<CandlePoint> candles, int maxSize)
        {
            if (candles.Count == 0)
                return;

            foreach (var candle in candles)
            {
                if (Candles.Count == 0)
                {
                    Candles.Add(candle);
                    continue;
                }

                var last = Candles[^1];
                if (candle.OpenTimeUtc > last.OpenTimeUtc)
                {
                    Candles.Add(candle);
                }
                else if (candle.OpenTimeUtc == last.OpenTimeUtc)
                {
                    Candles[^1] = candle;
                }
            }

            if (Candles.Count > maxSize)
            {
                var removeCount = Candles.Count - maxSize;
                Candles.RemoveRange(0, removeCount);
            }
        }
    }

    private sealed record CandlePoint(
        DateTime OpenTimeUtc,
        DateTime CloseTimeUtc,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume);

    private sealed record ResolvedCurrentPrice(
        decimal Price,
        string Source,
        DateTime AsOfUtc,
        decimal AgeSeconds);
}
