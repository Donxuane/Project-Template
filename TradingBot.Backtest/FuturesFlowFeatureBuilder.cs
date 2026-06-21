using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record FuturesFlowFeatures
{
    public decimal? FundingRate { get; init; }
    public decimal? FundingRateZScore { get; init; }
    public decimal? OpenInterestChange15m { get; init; }
    public decimal? OpenInterestChange30m { get; init; }
    public decimal? OpenInterestChange60m { get; init; }
    public decimal? OpenInterestZScore { get; init; }
    public decimal? TakerBuySellImbalance { get; init; }
    public decimal? LongShortRatioChange { get; init; }
    public decimal? LiquidationSpike { get; init; }
    public decimal? MarkIndexDivergence { get; init; }
}

public sealed class FuturesFlowFeatureBuilder
{
    private const int ZScoreWindow = 30;

    private readonly long[] _fundingT;
    private readonly decimal[] _fundingV;
    private readonly long[] _oiT;
    private readonly decimal[] _oiV;
    private readonly long[] _takerT;
    private readonly decimal[] _takerImb;
    private readonly long[] _lsrT;
    private readonly decimal[] _lsrV;
    private readonly long[] _markT;
    private readonly decimal[] _markClose;
    private readonly long[] _indexT;
    private readonly decimal[] _indexClose;

    public FuturesFlowFeatureBuilder(FuturesMarketDataLoader loader, TradingSymbol symbol)
    {
        var funding = loader.LoadFunding(symbol);
        _fundingT = funding.Select(p => p.TimestampMs).ToArray();
        _fundingV = funding.Select(p => p.Rate).ToArray();

        var oi = loader.LoadOpenInterest(symbol);
        _oiT = oi.Select(p => p.TimestampMs).ToArray();
        _oiV = oi.Select(p => p.OpenInterest).ToArray();

        var taker = loader.LoadTaker(symbol);
        _takerT = taker.Select(p => p.TimestampMs).ToArray();
        _takerImb = taker.Select(p =>
        {
            var total = p.BuyVol + p.SellVol;
            return total > 0m ? Math.Round((p.BuyVol - p.SellVol) / total, 6) : 0m;
        }).ToArray();

        var lsr = loader.LoadGlobalLongShort(symbol);
        _lsrT = lsr.Select(p => p.TimestampMs).ToArray();
        _lsrV = lsr.Select(p => p.LongShortRatio).ToArray();

        var mark = loader.LoadPriceKlines(symbol, "markPriceKlines");
        _markT = mark.Select(p => p.TimestampMs).ToArray();
        _markClose = mark.Select(p => p.Close).ToArray();

        var index = loader.LoadPriceKlines(symbol, "indexPriceKlines");
        _indexT = index.Select(p => p.TimestampMs).ToArray();
        _indexClose = index.Select(p => p.Close).ToArray();
    }

    public bool HasFunding => _fundingT.Length > 0;
    public bool HasMarkIndex => _markT.Length > 0 && _indexT.Length > 0;
    public bool HasOpenInterest => _oiT.Length > 0;
    public bool HasTaker => _takerT.Length > 0;
    public bool HasLongShort => _lsrT.Length > 0;

    public FuturesFlowFeatures Build(DateTime entryTimeUtc)
    {
        var t = new DateTimeOffset(entryTimeUtc).ToUnixTimeMilliseconds();

        var fundingIdx = AsOfIndex(_fundingT, t);
        decimal? funding = fundingIdx >= 0 ? _fundingV[fundingIdx] : null;
        decimal? fundingZ = fundingIdx >= 0 ? ZScore(_fundingV, fundingIdx) : null;

        var oiIdx = AsOfIndex(_oiT, t);
        decimal? oiZ = oiIdx >= 0 ? ZScore(_oiV, oiIdx) : null;
        decimal? oi15 = OiChange(t, 15);
        decimal? oi30 = OiChange(t, 30);
        decimal? oi60 = OiChange(t, 60);

        var takerIdx = AsOfIndex(_takerT, t);
        decimal? takerImb = takerIdx >= 0 ? _takerImb[takerIdx] : null;

        decimal? lsrChange = null;
        var lsrIdx = AsOfIndex(_lsrT, t);
        if (lsrIdx >= 0)
        {
            var prevIdx = AsOfIndex(_lsrT, t - 60L * 60_000L);
            if (prevIdx >= 0 && _lsrV[prevIdx] > 0m)
                lsrChange = Math.Round((_lsrV[lsrIdx] - _lsrV[prevIdx]) / _lsrV[prevIdx] * 100m, 6);
        }

        decimal? markIndexDiv = null;
        var markIdx = AsOfIndex(_markT, t);
        var indexIdx = AsOfIndex(_indexT, t);
        if (markIdx >= 0 && indexIdx >= 0 && _indexClose[indexIdx] > 0m)
            markIndexDiv = Math.Round((_markClose[markIdx] - _indexClose[indexIdx]) / _indexClose[indexIdx] * 100m, 6);

        return new FuturesFlowFeatures
        {
            FundingRate = funding,
            FundingRateZScore = fundingZ,
            OpenInterestChange15m = oi15,
            OpenInterestChange30m = oi30,
            OpenInterestChange60m = oi60,
            OpenInterestZScore = oiZ,
            TakerBuySellImbalance = takerImb,
            LongShortRatioChange = lsrChange,
            LiquidationSpike = null,
            MarkIndexDivergence = markIndexDiv
        };
    }

    private decimal? OiChange(long t, int minutes)
    {
        var nowIdx = AsOfIndex(_oiT, t);
        var pastIdx = AsOfIndex(_oiT, t - (long)minutes * 60_000L);
        if (nowIdx < 0 || pastIdx < 0 || _oiV[pastIdx] <= 0m)
            return null;
        return Math.Round((_oiV[nowIdx] - _oiV[pastIdx]) / _oiV[pastIdx] * 100m, 6);
    }

    private static decimal? ZScore(decimal[] values, int index)
    {
        var start = Math.Max(0, index - ZScoreWindow + 1);
        var count = index - start + 1;
        if (count < 5)
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
}
