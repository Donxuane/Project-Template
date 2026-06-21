using System.Globalization;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Builds Part B short-window flow features at arbitrary timestamps, using only data with
/// timestamps at or before the requested time (no future leakage). Prefers 5m-period flow
/// series when present and falls back to the 30m-period series.
/// </summary>
public sealed class ShortWindowFlowFeatureIndex
{
    private const int OiZScoreWindow = 288;       // 24h of 5m points
    private const int LsrZScoreWindow = 288;
    private const int FundingZScoreWindow = 30;   // 10 days of 8h funding events
    private const int MinZScoreSamples = 30;
    private const int MinFundingZSamples = 5;
    private const long StaleFlowMs = 45L * 60_000L;      // flow points older than 45m are stale
    private const long StaleMarkIndexMs = 90L * 60_000L; // 30m klines older than 90m are stale

    private readonly long[] _oiT;
    private readonly decimal[] _oiV;
    private readonly long[] _takerT;
    private readonly decimal[] _takerImb;
    private readonly long[] _lsrT;
    private readonly decimal[] _lsrV;
    private readonly long[] _topLsrT;
    private readonly decimal[] _topLsrV;
    private readonly long[] _fundingT;
    private readonly decimal[] _fundingV;
    private readonly long[] _markT;
    private readonly decimal[] _markClose;
    private readonly long[] _indexT;
    private readonly decimal[] _indexClose;
    private readonly IReadOnlyList<KlineCandle> _symbolIntervalCandles;
    private readonly BtcContextIndex _btcContext;
    private readonly IReadOnlyList<KlineCandle> _btcOneMinute;
    private readonly Dictionary<DateTime, ShortWindowFlowSnapshot> _snapshotCache = new();

    public ShortWindowFlowFeatureIndex(
        FuturesMarketDataLoader loader,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> symbolIntervalCandles,
        IReadOnlyList<KlineCandle> btcOneMinuteCandles)
    {
        _symbolIntervalCandles = symbolIntervalCandles;
        _btcOneMinute = btcOneMinuteCandles;
        _btcContext = new BtcContextIndex(btcOneMinuteCandles);

        var oi = LoadSeries(loader, symbol, "openInterestHist", row => Field(row, "oi"));
        (_oiT, _oiV) = oi;

        var taker = LoadTakerSeries(loader, symbol);
        (_takerT, _takerImb) = taker;

        var lsr = LoadSeries(loader, symbol, "globalLongShortAccountRatio", row => Field(row, "longShortRatio"));
        (_lsrT, _lsrV) = lsr;

        var topLsr = LoadSeries(loader, symbol, "topLongShortPositionRatio", row => Field(row, "longShortRatio"));
        (_topLsrT, _topLsrV) = topLsr;

        var funding = loader.LoadFunding(symbol);
        _fundingT = funding.Select(p => p.TimestampMs).ToArray();
        _fundingV = funding.Select(p => p.Rate).ToArray();

        var mark = loader.LoadPriceKlines(symbol, "markPriceKlines");
        _markT = mark.Select(p => p.TimestampMs).ToArray();
        _markClose = mark.Select(p => p.Close).ToArray();

        var index = loader.LoadPriceKlines(symbol, "indexPriceKlines");
        _indexT = index.Select(p => p.TimestampMs).ToArray();
        _indexClose = index.Select(p => p.Close).ToArray();
    }

    public bool HasOpenInterest => _oiT.Length > 0;
    public bool HasTaker => _takerT.Length > 0;
    public bool HasGlobalLongShort => _lsrT.Length > 0;
    public bool HasTopLongShort => _topLsrT.Length > 0;
    public bool HasFunding => _fundingT.Length > 0;
    public bool HasMarkIndex => _markT.Length > 0 && _indexT.Length > 0;

    /// <summary>Earliest timestamp covered by the 30d-limited flow series (OI/taker/long-short).</summary>
    public DateTime? FlowCoverageStartUtc
    {
        get
        {
            var firsts = new List<long>();
            if (_oiT.Length > 0) firsts.Add(_oiT[0]);
            if (_takerT.Length > 0) firsts.Add(_takerT[0]);
            if (_lsrT.Length > 0) firsts.Add(_lsrT[0]);
            return firsts.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(firsts.Min()).UtcDateTime;
        }
    }

    public DateTime? FlowCoverageEndUtc
    {
        get
        {
            var lasts = new List<long>();
            if (_oiT.Length > 0) lasts.Add(_oiT[^1]);
            if (_takerT.Length > 0) lasts.Add(_takerT[^1]);
            if (_lsrT.Length > 0) lasts.Add(_lsrT[^1]);
            return lasts.Count == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lasts.Max()).UtcDateTime;
        }
    }

    public ShortWindowFlowSnapshot Snapshot(DateTime timeUtc)
    {
        if (_snapshotCache.TryGetValue(timeUtc, out var cached))
            return cached;

        var t = new DateTimeOffset(timeUtc).ToUnixTimeMilliseconds();

        var oi5 = SeriesChangePercent(_oiT, _oiV, t, 5);
        var oi15 = SeriesChangePercent(_oiT, _oiV, t, 15);
        var oi30 = SeriesChangePercent(_oiT, _oiV, t, 30);
        var oi60 = SeriesChangePercent(_oiT, _oiV, t, 60);
        var oiIdx = FreshAsOfIndex(_oiT, t, StaleFlowMs);
        var oiZ = oiIdx >= 0 ? TrailingZScore(_oiV, oiIdx, OiZScoreWindow, MinZScoreSamples) : null;

        var takerIdx = FreshAsOfIndex(_takerT, t, StaleFlowMs);
        decimal? takerImb = takerIdx >= 0 ? _takerImb[takerIdx] : null;
        var taker1h = TrailingMean(_takerT, _takerImb, t, 60);

        var lsrIdx = FreshAsOfIndex(_lsrT, t, StaleFlowMs);
        decimal? lsr = lsrIdx >= 0 ? _lsrV[lsrIdx] : null;
        var lsrChange1h = SeriesChangePercent(_lsrT, _lsrV, t, 60);
        var lsrZ = lsrIdx >= 0 ? TrailingZScore(_lsrV, lsrIdx, LsrZScoreWindow, MinZScoreSamples) : null;

        var topIdx = FreshAsOfIndex(_topLsrT, t, StaleFlowMs);
        decimal? topLsr = topIdx >= 0 ? _topLsrV[topIdx] : null;
        var topZ = topIdx >= 0 ? TrailingZScore(_topLsrV, topIdx, LsrZScoreWindow, MinZScoreSamples) : null;

        var fundingIdx = AsOfIndex(_fundingT, t);
        decimal? funding = fundingIdx >= 0 ? _fundingV[fundingIdx] : null;
        var fundingZ = fundingIdx >= 0 ? TrailingZScore(_fundingV, fundingIdx, FundingZScoreWindow, MinFundingZSamples) : null;

        decimal? markIndexDiv = null;
        var markIdx = FreshAsOfIndex(_markT, t, StaleMarkIndexMs);
        var indexIdx = FreshAsOfIndex(_indexT, t, StaleMarkIndexMs);
        if (markIdx >= 0 && indexIdx >= 0 && _indexClose[indexIdx] > 0m)
            markIndexDiv = Math.Round((_markClose[markIdx] - _indexClose[indexIdx]) / _indexClose[indexIdx] * 100m, 6);

        var btc = _btcContext.GetSnapshot(timeUtc);
        var btcTrendSlope = ComputeBtcTrendSlopePercentPerHour(timeUtc);

        var regime = ComputeRegimeFeatures(timeUtc);

        var snapshot = new ShortWindowFlowSnapshot
        {
            TimestampUtc = timeUtc,
            OiChange5mPercent = oi5,
            OiChange15mPercent = oi15,
            OiChange30mPercent = oi30,
            OiChange60mPercent = oi60,
            OiZScoreRecent = oiZ,
            TakerBuySellImbalance = takerImb,
            TakerImbalance1h = taker1h,
            GlobalLongShortRatio = lsr,
            GlobalLongShortRatioChange1hPercent = lsrChange1h,
            GlobalLongShortZScore = lsrZ,
            TopLongShortRatio = topLsr,
            TopLongShortZScore = topZ,
            FundingRate = funding,
            FundingZScore = fundingZ,
            MarkIndexDivergencePercent = markIndexDiv,
            BtcReturn30mPercent = btc?.BtcReturn30mPercent,
            BtcReturn60mPercent = btc?.BtcReturn60mPercent,
            BtcTrendSlopePercentPerHour = btcTrendSlope,
            VolatilityRegime = regime?.VolatilityRegime,
            AtrPercent = regime?.AtrPercent,
            DistanceFromRecentHighPercent = regime?.DistanceFromRecentHighPercent,
            DistanceFromRecentLowPercent = regime?.DistanceFromRecentLowPercent
        };
        _snapshotCache[timeUtc] = snapshot;
        return snapshot;
    }

    private MarketRegimeForwardEdgeScanner.RegimeCandleFeatures? ComputeRegimeFeatures(DateTime timeUtc)
    {
        if (_symbolIntervalCandles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return null;
        var idx = CandleAsOfIndex(_symbolIntervalCandles, timeUtc);
        if (idx < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles)
            return null;
        return MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
            _symbolIntervalCandles, idx, null, null, _symbolIntervalCandles[idx].OpenTimeUtc);
    }

    private decimal? ComputeBtcTrendSlopePercentPerHour(DateTime timeUtc)
    {
        const int windowMinutes = 240;
        var idx = CandleAsOfIndex(_btcOneMinute, timeUtc);
        if (idx < windowMinutes)
            return null;
        var start = idx - windowMinutes + 1;
        decimal sumX = 0m, sumY = 0m, sumXy = 0m, sumXx = 0m;
        var n = windowMinutes;
        for (var i = 0; i < n; i++)
        {
            var x = (decimal)i;
            var y = _btcOneMinute[start + i].Close;
            sumX += x;
            sumY += y;
            sumXy += x * y;
            sumXx += x * x;
        }

        var denom = n * sumXx - sumX * sumX;
        if (denom == 0m)
            return null;
        var slopePerMinute = (n * sumXy - sumX * sumY) / denom;
        var lastClose = _btcOneMinute[idx].Close;
        if (lastClose <= 0m)
            return null;
        return Math.Round(slopePerMinute * 60m / lastClose * 100m, 6);
    }

    private static (long[] T, decimal[] V) LoadSeries(
        FuturesMarketDataLoader loader,
        TradingSymbol symbol,
        string baseSourceKey,
        Func<IReadOnlyDictionary<string, string>, decimal?> selector)
    {
        var raw = loader.LoadRaw(symbol, ShortWindowFlowDataDownloader.FineSourceKey(baseSourceKey));
        if (raw.Count == 0)
            raw = loader.LoadRaw(symbol, baseSourceKey);
        var points = raw
            .Select(r => (T: Ts(r), V: selector(r)))
            .Where(p => p.T > 0 && p.V.HasValue)
            .OrderBy(p => p.T)
            .ToArray();
        return (points.Select(p => p.T).ToArray(), points.Select(p => p.V!.Value).ToArray());
    }

    private static (long[] T, decimal[] V) LoadTakerSeries(FuturesMarketDataLoader loader, TradingSymbol symbol)
    {
        var raw = loader.LoadRaw(symbol, ShortWindowFlowDataDownloader.FineSourceKey("takerLongShortRatio"));
        if (raw.Count == 0)
            raw = loader.LoadRaw(symbol, "takerLongShortRatio");
        var points = raw
            .Select(r =>
            {
                var buy = Field(r, "buyVol");
                var sell = Field(r, "sellVol");
                decimal? imb = buy.HasValue && sell.HasValue && buy.Value + sell.Value > 0m
                    ? Math.Round((buy.Value - sell.Value) / (buy.Value + sell.Value), 6)
                    : null;
                return (T: Ts(r), V: imb);
            })
            .Where(p => p.T > 0 && p.V.HasValue)
            .OrderBy(p => p.T)
            .ToArray();
        return (points.Select(p => p.T).ToArray(), points.Select(p => p.V!.Value).ToArray());
    }

    private static decimal? SeriesChangePercent(long[] timestamps, decimal[] values, long t, int minutes)
    {
        var nowIdx = FreshAsOfIndex(timestamps, t, StaleFlowMs);
        var pastIdx = AsOfIndex(timestamps, t - (long)minutes * 60_000L);
        if (nowIdx < 0 || pastIdx < 0 || pastIdx >= nowIdx || values[pastIdx] <= 0m)
            return null;
        return Math.Round((values[nowIdx] - values[pastIdx]) / values[pastIdx] * 100m, 6);
    }

    private static decimal? TrailingMean(long[] timestamps, decimal[] values, long t, int minutes)
    {
        var endIdx = FreshAsOfIndex(timestamps, t, StaleFlowMs);
        if (endIdx < 0)
            return null;
        var cutoff = t - (long)minutes * 60_000L;
        decimal sum = 0m;
        var count = 0;
        for (var i = endIdx; i >= 0 && timestamps[i] > cutoff; i--)
        {
            sum += values[i];
            count++;
        }

        return count == 0 ? null : Math.Round(sum / count, 6);
    }

    private static decimal? TrailingZScore(decimal[] values, int index, int window, int minSamples)
    {
        var start = Math.Max(0, index - window + 1);
        var count = index - start + 1;
        if (count < minSamples)
            return null;
        decimal sum = 0m;
        for (var i = start; i <= index; i++)
            sum += values[i];
        var mean = sum / count;
        decimal varSum = 0m;
        for (var i = start; i <= index; i++)
            varSum += (values[i] - mean) * (values[i] - mean);
        var std = (decimal)Math.Sqrt((double)(varSum / count));
        return std > 0m ? Math.Round((values[index] - mean) / std, 6) : 0m;
    }

    private static int FreshAsOfIndex(long[] timestamps, long target, long maxAgeMs)
    {
        var idx = AsOfIndex(timestamps, target);
        if (idx < 0 || target - timestamps[idx] > maxAgeMs)
            return -1;
        return idx;
    }

    private static int AsOfIndex(long[] timestamps, long target)
    {
        if (timestamps.Length == 0 || timestamps[0] > target)
            return -1;
        var lo = 0;
        var hi = timestamps.Length - 1;
        var result = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (timestamps[mid] <= target)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    private static int CandleAsOfIndex(IReadOnlyList<KlineCandle> candles, DateTime timeUtc)
    {
        var lo = 0;
        var hi = candles.Count - 1;
        var result = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (candles[mid].OpenTimeUtc <= timeUtc)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    private static long Ts(IReadOnlyDictionary<string, string> row)
        => row.TryGetValue("t", out var v)
           && long.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var t)
            ? t
            : 0L;

    private static decimal? Field(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out var v)
           && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
}
