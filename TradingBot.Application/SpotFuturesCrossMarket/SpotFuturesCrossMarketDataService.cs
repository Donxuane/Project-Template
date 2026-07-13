using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Enums.Endpoints;
using TradingBot.Domain.Interfaces.Services;
using TradingBot.Domain.Models.Decision;
using TradingBot.Domain.Models.MarketData;

namespace TradingBot.Application.SpotFuturesCrossMarket;

/// <summary>
/// Builds a synchronized Spot + USD-M Futures snapshot for the cross-market strategy.
/// Spot candles come from the existing Spot REST client (/api/v3/klines); futures candles,
/// funding rate and mark price come from the testnet-host-locked futures client
/// (/fapi/v1/klines and /fapi/v1/premiumIndex). Only fully closed candles are kept, and the
/// two feeds are anchored on the same latest closed candle open time.
/// </summary>
public sealed class SpotFuturesCrossMarketDataService(
    IToolService toolService,
    IFuturesTestnetClient futuresClient,
    ILogger<SpotFuturesCrossMarketDataService> logger)
{
    public async Task<CrossMarketSnapshot> GetSnapshotAsync(
        SpotFuturesCrossMarketSettings settings,
        CancellationToken cancellationToken)
    {
        var symbol = settings.Symbol;

        var spotTask = FetchSpotClosedCandlesAsync(symbol, settings.Interval, settings.CandleHistory, cancellationToken);
        var futuresTask = FetchFuturesClosedCandlesAsync(symbol, settings.Interval, settings.CandleHistory, cancellationToken);
        await Task.WhenAll(spotTask, futuresTask);

        var spotCandles = spotTask.Result;
        var futuresCandles = futuresTask.Result;

        if (spotCandles.Count == 0 || futuresCandles.Count == 0)
        {
            return OutOfSync(settings, spotCandles, futuresCandles,
                $"MissingCandles(spot={spotCandles.Count}, futures={futuresCandles.Count})");
        }

        var required = Math.Max(settings.LongMaPeriod + 2, settings.MomentumLookbackCandles + 2);
        if (spotCandles.Count < required || futuresCandles.Count < required)
        {
            return OutOfSync(settings, spotCandles, futuresCandles,
                $"InsufficientHistory(spot={spotCandles.Count}, futures={futuresCandles.Count}, required={required})");
        }

        // Anchor both feeds on the same latest fully closed candle.
        var lastSpot = spotCandles[^1];
        var lastFutures = futuresCandles[^1];
        var alignedOpen = lastSpot.OpenTimeUtc <= lastFutures.OpenTimeUtc ? lastSpot.OpenTimeUtc : lastFutures.OpenTimeUtc;

        spotCandles = TrimToAnchor(spotCandles, alignedOpen);
        futuresCandles = TrimToAnchor(futuresCandles, alignedOpen);

        if (spotCandles.Count < required || futuresCandles.Count < required)
        {
            return OutOfSync(settings, spotCandles, futuresCandles,
                $"InsufficientAlignedHistory(spot={spotCandles.Count}, futures={futuresCandles.Count}, required={required})");
        }

        var spotAnchor = spotCandles[^1];
        var futuresAnchor = futuresCandles[^1];
        var misalignmentSeconds = Math.Abs((spotAnchor.OpenTimeUtc - futuresAnchor.OpenTimeUtc).TotalSeconds);
        if (misalignmentSeconds > settings.MaxCandleMisalignmentSeconds)
        {
            return OutOfSync(settings, spotCandles, futuresCandles,
                $"CandleMisalignment({misalignmentSeconds:F0}s > {settings.MaxCandleMisalignmentSeconds}s)");
        }

        FuturesTestnetPremiumIndex? premium = null;
        try
        {
            premium = await futuresClient.GetPremiumIndexAsync(symbol.ToString(), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpotFuturesCrossMarket premiumIndex fetch failed for {Symbol}; funding filter degraded.", symbol);
        }

        var basisPercent = spotAnchor.Close > 0m
            ? (futuresAnchor.Close - spotAnchor.Close) / spotAnchor.Close * 100m
            : 0m;

        return new CrossMarketSnapshot
        {
            Symbol = symbol,
            Interval = settings.Interval,
            CandleOpenTimeUtc = futuresAnchor.OpenTimeUtc,
            CandleCloseTimeUtc = futuresAnchor.CloseTimeUtc,
            MarketsInSync = true,
            Spot = ToMarketSnapshot(symbol, spotCandles),
            Futures = ToMarketSnapshot(symbol, futuresCandles),
            SpotClose = spotAnchor.Close,
            FuturesClose = futuresAnchor.Close,
            BasisPercent = basisPercent,
            FundingRate = premium?.LastFundingRate,
            MarkPrice = premium?.MarkPrice
        };
    }

    private CrossMarketSnapshot OutOfSync(
        SpotFuturesCrossMarketSettings settings,
        IReadOnlyList<ClosedCandle> spotCandles,
        IReadOnlyList<ClosedCandle> futuresCandles,
        string issue)
    {
        logger.LogWarning("SpotFuturesCrossMarket snapshot out of sync for {Symbol}: {Issue}", settings.Symbol, issue);
        var anchor = futuresCandles.Count > 0 ? futuresCandles[^1] : spotCandles.Count > 0 ? spotCandles[^1] : null;
        return new CrossMarketSnapshot
        {
            Symbol = settings.Symbol,
            Interval = settings.Interval,
            CandleOpenTimeUtc = anchor?.OpenTimeUtc ?? DateTime.UtcNow,
            CandleCloseTimeUtc = anchor?.CloseTimeUtc ?? DateTime.UtcNow,
            MarketsInSync = false,
            SyncIssue = issue,
            SpotClose = spotCandles.Count > 0 ? spotCandles[^1].Close : 0m,
            FuturesClose = futuresCandles.Count > 0 ? futuresCandles[^1].Close : 0m
        };
    }

    private static IReadOnlyList<ClosedCandle> TrimToAnchor(IReadOnlyList<ClosedCandle> candles, DateTime anchorOpenUtc)
        => candles.Where(c => c.OpenTimeUtc <= anchorOpenUtc).ToList();

    private static MarketSnapshot ToMarketSnapshot(TradingSymbol symbol, IReadOnlyList<ClosedCandle> candles)
    {
        var last = candles[^1];
        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = last.Close,
            CurrentPriceSource = "ClosedCandle",
            CurrentPriceAsOfUtc = last.CloseTimeUtc,
            LatestClosedCandleOpenTimeUtc = last.OpenTimeUtc,
            LatestClosedCandleCloseTimeUtc = last.CloseTimeUtc,
            LatestClosedCandleClosePrice = last.Close,
            LatestClosedCandleAgeSeconds = Math.Max(0m, (decimal)(DateTime.UtcNow - last.CloseTimeUtc).TotalSeconds),
            HighPrices = candles.Select(c => c.High).ToArray(),
            LowPrices = candles.Select(c => c.Low).ToArray(),
            ClosePrices = candles.Select(c => c.Close).ToArray(),
            Volumes = candles.Select(c => c.Volume).ToArray(),
            TimestampUtc = DateTime.UtcNow
        };
    }

    private async Task<IReadOnlyList<ClosedCandle>> FetchSpotClosedCandlesAsync(
        TradingSymbol symbol,
        string interval,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var endpoint = toolService.BinanceEndpointsService.GetEndpoint(MarketData.CandlestickDataKline);
            var request = new KlineRequest
            {
                Symbol = symbol.ToString(),
                Interval = interval,
                Limit = Math.Clamp(limit + 1, 2, 1000)
            };

            var raw = await toolService.BinanceClientService.Call<JsonElement, KlineRequest>(request, endpoint, false);
            return KeepClosed(ParseSpotCandles(raw));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpotFuturesCrossMarket spot kline fetch failed for {Symbol}.", symbol);
            return [];
        }
    }

    private async Task<IReadOnlyList<ClosedCandle>> FetchFuturesClosedCandlesAsync(
        TradingSymbol symbol,
        string interval,
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            var klines = await futuresClient.GetKlinesAsync(symbol.ToString(), interval, Math.Clamp(limit + 1, 2, 1500), cancellationToken);
            return KeepClosed(klines.Select(k => new ClosedCandle(k.OpenTimeUtc, k.CloseTimeUtc, k.High, k.Low, k.Close, k.Volume)).ToList());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SpotFuturesCrossMarket futures kline fetch failed for {Symbol}.", symbol);
            return [];
        }
    }

    /// <summary>Drops the still-forming candle: only candles whose close time has passed are kept.</summary>
    private static IReadOnlyList<ClosedCandle> KeepClosed(IReadOnlyList<ClosedCandle> candles)
    {
        var nowUtc = DateTime.UtcNow;
        return candles.Where(c => c.CloseTimeUtc <= nowUtc).OrderBy(c => c.OpenTimeUtc).ToList();
    }

    private static IReadOnlyList<ClosedCandle> ParseSpotCandles(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Array)
            return [];

        var candles = new List<ClosedCandle>();
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() <= 6)
                continue;

            if (!TryUnixMs(item[0], out var openMs) || !TryUnixMs(item[6], out var closeMs))
                continue;
            if (!TryDec(item[2], out var high) || !TryDec(item[3], out var low) || !TryDec(item[4], out var close) || !TryDec(item[5], out var volume))
                continue;

            candles.Add(new ClosedCandle(
                DateTimeOffset.FromUnixTimeMilliseconds(openMs).UtcDateTime,
                DateTimeOffset.FromUnixTimeMilliseconds(closeMs).UtcDateTime,
                high, low, close, volume));
        }

        return candles;
    }

    private static bool TryUnixMs(JsonElement el, out long value)
    {
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out value))
            return true;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return true;
        value = default;
        return false;
    }

    private static bool TryDec(JsonElement el, out decimal value)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Number:
                return el.TryGetDecimal(out value);
            case JsonValueKind.String:
                return decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
            default:
                value = default;
                return false;
        }
    }

    private sealed record ClosedCandle(
        DateTime OpenTimeUtc,
        DateTime CloseTimeUtc,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume);
}
