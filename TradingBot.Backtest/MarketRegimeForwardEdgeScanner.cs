using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class MarketRegimeForwardEdgeScanner
{
    public const int MinimumWarmupCandles = 60;
    public const decimal Target050Percent = 0.50m;
    public const decimal Stop050Percent = -0.50m;

    private static readonly decimal[] TargetThresholds = [0.30m, 0.50m, 0.75m, 1.00m];
    private static readonly decimal[] StopThresholds = [-0.25m, -0.50m, -0.75m, -1.00m];

    private static readonly (decimal Target, decimal Stop)[] PrimaryMatrixPairs =
    [
        (0.30m, -0.25m),
        (0.50m, -0.50m),
        (0.75m, -0.75m),
        (1.00m, -1.00m)
    ];

    public static int ResolveSamplingStride(string interval)
        => interval switch
        {
            "1m" => 5,
            "3m" => 2,
            _ => 1
        };

    public static IReadOnlyList<int> ResolveForwardHorizonMinutes(string interval)
        => interval switch
        {
            "1m" or "3m" => [15, 30, 60],
            "5m" => [30, 60, 240],
            "15m" => [60, 240, 480],
            "30m" => [240, 480, 720],
            _ => [60, 240, 480]
        };

    public static IReadOnlyList<MarketRegimeForwardEdgeObservation> ScanSymbolInterval(
        TradingSymbol symbol,
        string interval,
        string windowLabel,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> sourceOneMinuteCandles,
        decimal roundTripCostPercent,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var observations = new List<MarketRegimeForwardEdgeObservation>();
        if (intervalCandles.Count <= MinimumWarmupCandles + 1)
            return observations;

        var horizons = ResolveForwardHorizonMinutes(interval);
        var primaryHorizon = horizons[^1];
        var stride = ResolveSamplingStride(interval);

        for (var i = MinimumWarmupCandles; i < intervalCandles.Count; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candle = intervalCandles[i];
            if (candle.Close <= 0m)
                continue;

            var entryPrice = candle.Close;
            var entryTime = candle.OpenTimeUtc;
            var features = ComputeFeatures(intervalCandles, i, candle, btcContext, marketWideContext, entryTime);
            var forward = ComputeForwardOutcomes(
                sourceOneMinuteCandles,
                entryTime,
                entryPrice,
                horizons,
                primaryHorizon,
                roundTripCostPercent);

            observations.Add(new MarketRegimeForwardEdgeObservation
            {
                WindowLabel = windowLabel,
                Symbol = symbol,
                Interval = interval,
                TimeUtc = entryTime,
                HourOfDayUtc = entryTime.Hour,
                DayOfWeek = entryTime.DayOfWeek,
                SessionBucket = ResolveSessionBucket(entryTime.Hour),
                RecentReturn5CandlesPercent = features.RecentReturn5,
                RecentReturn15CandlesPercent = features.RecentReturn15,
                RecentReturn30CandlesPercent = features.RecentReturn30,
                RecentReturn60CandlesPercent = features.RecentReturn60,
                RangeWidthPercent = features.RangeWidthPercent,
                AtrPercent = features.AtrPercent,
                VolumeExpansionRatio = features.VolumeExpansionRatio,
                VolatilityRegime = features.VolatilityRegime,
                TrendSlopePercent = features.TrendSlopePercent,
                TrendStrengthPercent = features.TrendStrengthPercent,
                TrendRegime = features.TrendRegime,
                DistanceFromRecentHighPercent = features.DistanceFromRecentHighPercent,
                DistanceFromRecentLowPercent = features.DistanceFromRecentLowPercent,
                CandleBodyStrengthPercent = features.CandleBodyStrengthPercent,
                ClosePositionInRange = features.ClosePositionInRange,
                BtcReturn15mPercent = features.BtcReturn15mPercent,
                BtcReturn30mPercent = features.BtcReturn30mPercent,
                BtcReturn60mPercent = features.BtcReturn60mPercent,
                BtcTrendSlopePercent = features.BtcTrendSlopePercent,
                BtcTrendRegime = features.BtcTrendRegime,
                BtcVolatilityRegime = features.BtcVolatilityRegime,
                BtcAboveMediumMa = features.BtcAboveMediumMa,
                BtcMarketDirectionBucket = features.BtcMarketDirectionBucket,
                SymbolReturnRelativeToBtc60mPercent = features.SymbolReturnRelativeToBtc60mPercent,
                MarketWideReturnProxyPercent = features.MarketWideReturnProxyPercent,
                MarketWideDirection = features.MarketWideDirection,
                EntryPrice = entryPrice,
                ForwardReturn15mPercent = forward.Return15m,
                ForwardReturn30mPercent = forward.Return30m,
                ForwardReturn60mPercent = forward.Return60m,
                ForwardReturn4hPercent = forward.Return4h,
                ForwardReturn8hPercent = forward.Return8h,
                ForwardMfePercent = forward.MfePercent,
                ForwardMaePercent = forward.MaePercent,
                PrimaryForwardHorizonMinutes = primaryHorizon,
                Target030BeforeStop025 = forward.Target030BeforeStop025,
                Target050BeforeStop050 = forward.Target050BeforeStop050,
                Target075BeforeStop075 = forward.Target075BeforeStop075,
                Target100BeforeStop100 = forward.Target100BeforeStop100,
                TargetBeforeStopProbability = forward.Target050BeforeStop050Rate,
                ExpectedNetAfterCostPercent = forward.ExpectedNetAfterCostPercent,
                RoundTripCostPercent = roundTripCostPercent,
                LongEdgeScore = forward.LongEdgeScore
            });
        }

        return observations;
    }

    public static IReadOnlyList<TargetBeforeStopMatrixRow> BuildTargetBeforeStopMatrix(
        string windowLabel,
        TradingSymbol symbol,
        string interval,
        IReadOnlyList<MarketRegimeForwardEdgeObservation> observations)
    {
        var rows = new List<TargetBeforeStopMatrixRow>();
        foreach (var (target, stop) in PrimaryMatrixPairs)
        {
            var resolved = observations
                .Select(o => ResolveTargetBeforeStop(o, target, stop))
                .ToArray();
            var sampleCount = resolved.Length;
            if (sampleCount == 0)
                continue;

            var targetFirst = resolved.Count(x => x == TargetStopOutcome.TargetFirst);
            var stopFirst = resolved.Count(x => x == TargetStopOutcome.StopFirst);
            var unresolved = resolved.Count(x => x == TargetStopOutcome.Unresolved);
            var rate = sampleCount == 0 ? 0m : Math.Round((decimal)targetFirst / sampleCount, 6);
            var expectedNet = sampleCount == 0
                ? 0m
                : Math.Round(resolved.Average(x => ExpectedNetForOutcome(x, target, stop, observations[0].RoundTripCostPercent)), 6);

            rows.Add(new TargetBeforeStopMatrixRow
            {
                WindowLabel = windowLabel,
                Symbol = symbol,
                Interval = interval,
                TargetPercent = target,
                StopPercent = stop,
                SampleCount = sampleCount,
                TargetBeforeStopCount = targetFirst,
                StopBeforeTargetCount = stopFirst,
                UnresolvedCount = unresolved,
                TargetBeforeStopRate = rate,
                ExpectedNetAfterCostPercent = expectedNet
            });
        }

        return rows;
    }

    private static TargetStopOutcome ResolveTargetBeforeStop(
        MarketRegimeForwardEdgeObservation observation,
        decimal targetPercent,
        decimal stopPercent)
    {
        if (targetPercent == 0.30m && stopPercent == -0.25m)
            return observation.Target030BeforeStop025 ? TargetStopOutcome.TargetFirst : TargetStopOutcome.StopFirst;
        if (targetPercent == 0.50m && stopPercent == -0.50m)
            return observation.Target050BeforeStop050 ? TargetStopOutcome.TargetFirst : TargetStopOutcome.StopFirst;
        if (targetPercent == 0.75m && stopPercent == -0.75m)
            return observation.Target075BeforeStop075 ? TargetStopOutcome.TargetFirst : TargetStopOutcome.StopFirst;
        if (targetPercent == 1.00m && stopPercent == -1.00m)
            return observation.Target100BeforeStop100 ? TargetStopOutcome.TargetFirst : TargetStopOutcome.StopFirst;
        return TargetStopOutcome.Unresolved;
    }

    private static decimal ExpectedNetForOutcome(TargetStopOutcome outcome, decimal target, decimal stop, decimal cost)
        => outcome switch
        {
            TargetStopOutcome.TargetFirst => target - cost,
            TargetStopOutcome.StopFirst => stop - cost,
            _ => -cost
        };

    public static RegimeCandleFeatures ComputeRegimeCandleFeatures(
        IReadOnlyList<KlineCandle> candles,
        int index,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        DateTime entryTime)
    {
        var candle = candles[index];
        return ComputeFeatures(candles, index, candle, btcContext, marketWideContext, entryTime);
    }

    private static RegimeCandleFeatures ComputeFeatures(
        IReadOnlyList<KlineCandle> candles,
        int index,
        KlineCandle candle,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        DateTime entryTime)
    {
        var close = candle.Close;
        var mediumMa = ComputeSma(candles, index, 20) ?? close;
        var priorMediumMa = ComputeSma(candles, index - 5, 20) ?? mediumMa;
        var trendSlope = close > 0m ? (mediumMa - priorMediumMa) / close * 100m : 0m;
        var trendStrength = close > 0m ? (close - mediumMa) / close * 100m : 0m;
        var atr = ComputeAtr(candles, index, 14);
        var atrPercent = close > 0m ? atr / close * 100m : 0m;
        var swingHigh = ComputeSwingHigh(candles, index, 20);
        var swingLow = ComputeSwingLow(candles, index, 20);
        var rangeWidth = close > 0m && swingHigh > swingLow
            ? (swingHigh - swingLow) / close * 100m
            : 0m;
        var avgVolume = ComputeAverageVolume(candles, index - 1, 20);
        var volumeExpansion = avgVolume > 0m ? candle.Volume / avgVolume : 0m;
        var distanceHigh = swingHigh > 0m ? (swingHigh - close) / close * 100m : 0m;
        var distanceLow = close > 0m ? (close - swingLow) / close * 100m : 0m;
        var bodyStrength = candle.High > candle.Low
            ? Math.Abs(candle.Close - candle.Open) / (candle.High - candle.Low) * 100m
            : 0m;
        var closePosition = candle.High > candle.Low
            ? (candle.Close - candle.Low) / (candle.High - candle.Low)
            : 0.5m;

        var btcSnapshot = btcContext?.GetSnapshot(entryTime);
        var symbolReturn60 = ComputeReturnPercent(candles, index, 60, close);
        var marketProxy = marketWideContext?.GetProxyReturn(entryTime);
        var marketDirection = marketWideContext?.GetDirection(entryTime);
        decimal? relativeToBtc = btcSnapshot?.BtcReturn60mPercent.HasValue == true
            ? Math.Round(symbolReturn60 - btcSnapshot.BtcReturn60mPercent!.Value, 6)
            : null;

        return new RegimeCandleFeatures
        {
            RecentReturn5 = ComputeReturnPercent(candles, index, 5, close),
            RecentReturn15 = ComputeReturnPercent(candles, index, 15, close),
            RecentReturn30 = ComputeReturnPercent(candles, index, 30, close),
            RecentReturn60 = symbolReturn60,
            RangeWidthPercent = Math.Round(rangeWidth, 6),
            AtrPercent = Math.Round(atrPercent, 6),
            VolumeExpansionRatio = Math.Round(volumeExpansion, 6),
            VolatilityRegime = ClassifyVolatilityRegime(atrPercent),
            TrendSlopePercent = Math.Round(trendSlope, 6),
            TrendStrengthPercent = Math.Round(trendStrength, 6),
            TrendRegime = ClassifyTrendRegime(trendSlope),
            DistanceFromRecentHighPercent = Math.Round(distanceHigh, 6),
            DistanceFromRecentLowPercent = Math.Round(distanceLow, 6),
            CandleBodyStrengthPercent = Math.Round(bodyStrength, 6),
            ClosePositionInRange = Math.Round(closePosition, 6),
            BtcReturn15mPercent = btcSnapshot?.BtcReturn15mPercent,
            BtcReturn30mPercent = btcSnapshot?.BtcReturn30mPercent,
            BtcReturn60mPercent = btcSnapshot?.BtcReturn60mPercent,
            BtcTrendSlopePercent = btcSnapshot?.BtcTrendSlopePercent,
            BtcTrendRegime = btcSnapshot?.BtcTrendRegime,
            BtcVolatilityRegime = btcSnapshot?.BtcVolatilityRegime,
            BtcAboveMediumMa = btcSnapshot?.BtcAboveMediumMa,
            BtcMarketDirectionBucket = btcSnapshot?.BtcMarketDirectionBucket,
            SymbolReturnRelativeToBtc60mPercent = relativeToBtc,
            MarketWideReturnProxyPercent = marketProxy,
            MarketWideDirection = marketDirection
        };
    }

    private static ForwardSnapshot ComputeForwardOutcomes(
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        IReadOnlyList<int> horizons,
        int primaryHorizonMinutes,
        decimal roundTripCostPercent)
    {
        if (oneMinuteCandles.Count == 0 || entryPrice <= 0m)
            return ForwardSnapshot.Empty(roundTripCostPercent);

        var entryIdx = FindEntryIndex(oneMinuteCandles, entryTimeUtc);
        if (entryIdx < 0)
            return ForwardSnapshot.Empty(roundTripCostPercent);

        decimal? ReturnAt(int minutes)
        {
            var end = entryTimeUtc.AddMinutes(minutes);
            for (var i = entryIdx; i < oneMinuteCandles.Count; i++)
            {
                if (oneMinuteCandles[i].OpenTimeUtc > end)
                {
                    var prev = oneMinuteCandles[i - 1];
                    return Math.Round((prev.Close - entryPrice) / entryPrice * 100m, 6);
                }
            }

            return null;
        }

        var mfe = CandidateForwardOutcomeAnalyzer.ComputeForwardMfePercent(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes);
        var mae = CandidateForwardOutcomeAnalyzer.ComputeForwardMaePercent(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes);
        var matrix = ComputeTargetBeforeStopMatrix(oneMinuteCandles, entryIdx, entryPrice, primaryHorizonMinutes);
        var target050Rate = matrix.Target050BeforeStop050 ? 1m : 0m;
        var expectedNet = matrix.Target050BeforeStop050
            ? Target050Percent - roundTripCostPercent
            : matrix.StopBeforeTarget050
                ? Stop050Percent - roundTripCostPercent
                : (ReturnAt(primaryHorizonMinutes) ?? 0m) - roundTripCostPercent;
        var longEdgeScore = Math.Round(target050Rate * (Target050Percent - roundTripCostPercent)
            + (1m - target050Rate) * (Stop050Percent - roundTripCostPercent), 6);

        return new ForwardSnapshot
        {
            Return15m = ReturnAt(15),
            Return30m = ReturnAt(30),
            Return60m = ReturnAt(60),
            Return4h = ReturnAt(240),
            Return8h = ReturnAt(480),
            MfePercent = mfe,
            MaePercent = mae,
            Target030BeforeStop025 = matrix.Target030BeforeStop025,
            Target050BeforeStop050 = matrix.Target050BeforeStop050,
            Target075BeforeStop075 = matrix.Target075BeforeStop075,
            Target100BeforeStop100 = matrix.Target100BeforeStop100,
            StopBeforeTarget050 = matrix.StopBeforeTarget050,
            Target050BeforeStop050Rate = target050Rate,
            ExpectedNetAfterCostPercent = Math.Round(expectedNet, 6),
            LongEdgeScore = longEdgeScore
        };
    }

    private static TargetStopMatrixSnapshot ComputeTargetBeforeStopMatrix(
        IReadOnlyList<KlineCandle> candles,
        int entryIdx,
        decimal entryPrice,
        int horizonMinutes)
    {
        var end = candles[entryIdx].OpenTimeUtc.AddMinutes(horizonMinutes);
        var outcomes = new Dictionary<(decimal Target, decimal Stop), TargetStopOutcome>();
        foreach (var (target, stop) in PrimaryMatrixPairs)
            outcomes[(target, stop)] = TargetStopOutcome.Unresolved;

        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
                break;

            foreach (var (target, stop) in PrimaryMatrixPairs)
            {
                if (outcomes[(target, stop)] != TargetStopOutcome.Unresolved)
                    continue;

                var targetPrice = entryPrice * (1m + target / 100m);
                var stopPrice = entryPrice * (1m + stop / 100m);
                var hitTarget = candles[i].High >= targetPrice;
                var hitStop = candles[i].Low <= stopPrice;
                if (hitTarget && hitStop)
                {
                    outcomes[(target, stop)] = candles[i].Open <= entryPrice
                        ? TargetStopOutcome.StopFirst
                        : TargetStopOutcome.TargetFirst;
                }
                else if (hitStop)
                    outcomes[(target, stop)] = TargetStopOutcome.StopFirst;
                else if (hitTarget)
                    outcomes[(target, stop)] = TargetStopOutcome.TargetFirst;
            }
        }

        return new TargetStopMatrixSnapshot
        {
            Target030BeforeStop025 = outcomes[(0.30m, -0.25m)] == TargetStopOutcome.TargetFirst,
            Target050BeforeStop050 = outcomes[(0.50m, -0.50m)] == TargetStopOutcome.TargetFirst,
            Target075BeforeStop075 = outcomes[(0.75m, -0.75m)] == TargetStopOutcome.TargetFirst,
            Target100BeforeStop100 = outcomes[(1.00m, -1.00m)] == TargetStopOutcome.TargetFirst,
            StopBeforeTarget050 = outcomes[(0.50m, -0.50m)] == TargetStopOutcome.StopFirst
        };
    }

    public static string ResolveSessionBucket(int hourUtc)
        => hourUtc switch
        {
            >= 0 and < 8 => "Asia",
            >= 8 and < 13 => "Europe",
            >= 13 and < 21 => "US",
            _ => "LateUS"
        };

    private static string ClassifyVolatilityRegime(decimal atrPercent)
        => atrPercent switch
        {
            < 0.20m => "Low",
            <= 0.60m => "Normal",
            _ => "Elevated"
        };

    private static string ClassifyTrendRegime(decimal trendSlopePercent)
        => trendSlopePercent switch
        {
            > 0.02m => "Uptrend",
            < -0.02m => "Downtrend",
            _ => "Flat"
        };

    private static decimal ComputeReturnPercent(IReadOnlyList<KlineCandle> candles, int index, int lookback, decimal close)
    {
        var start = Math.Max(0, index - lookback);
        var startClose = candles[start].Close;
        return startClose > 0m ? Math.Round((close - startClose) / startClose * 100m, 6) : 0m;
    }

    private static decimal? ComputeSma(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        if (index < period - 1 || index >= candles.Count)
            return null;
        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
            sum += candles[i].Close;
        return sum / period;
    }

    private static decimal ComputeAtr(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        if (index < period)
            return 0m;
        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
        {
            var prevClose = candles[i - 1].Close;
            var tr = Math.Max(candles[i].High - candles[i].Low,
                Math.Max(Math.Abs(candles[i].High - prevClose), Math.Abs(candles[i].Low - prevClose)));
            sum += tr;
        }

        return sum / period;
    }

    private static decimal ComputeSwingHigh(IReadOnlyList<KlineCandle> candles, int index, int lookback)
    {
        var start = Math.Max(0, index - lookback + 1);
        decimal high = 0m;
        for (var i = start; i <= index; i++)
            high = Math.Max(high, candles[i].High);
        return high;
    }

    private static decimal ComputeSwingLow(IReadOnlyList<KlineCandle> candles, int index, int lookback)
    {
        var start = Math.Max(0, index - lookback + 1);
        decimal low = decimal.MaxValue;
        for (var i = start; i <= index; i++)
            low = Math.Min(low, candles[i].Low);
        return low == decimal.MaxValue ? 0m : low;
    }

    private static decimal ComputeAverageVolume(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        var start = Math.Max(0, index - period + 1);
        decimal sum = 0m;
        var count = 0;
        for (var i = start; i <= index; i++)
        {
            sum += candles[i].Volume;
            count++;
        }

        return count == 0 ? 0m : sum / count;
    }

    private static int FindEntryIndex(IReadOnlyList<KlineCandle> candles, DateTime entryTimeUtc)
    {
        for (var i = candles.Count - 1; i >= 0; i--)
        {
            if (candles[i].OpenTimeUtc <= entryTimeUtc)
                return i;
        }

        return -1;
    }

    private enum TargetStopOutcome
    {
        Unresolved,
        TargetFirst,
        StopFirst
    }

    public sealed record RegimeCandleFeatures
    {
        public decimal RecentReturn5 { get; init; }
        public decimal RecentReturn15 { get; init; }
        public decimal RecentReturn30 { get; init; }
        public decimal RecentReturn60 { get; init; }
        public decimal RangeWidthPercent { get; init; }
        public decimal AtrPercent { get; init; }
        public decimal VolumeExpansionRatio { get; init; }
        public string VolatilityRegime { get; init; } = string.Empty;
        public decimal TrendSlopePercent { get; init; }
        public decimal TrendStrengthPercent { get; init; }
        public string TrendRegime { get; init; } = string.Empty;
        public decimal DistanceFromRecentHighPercent { get; init; }
        public decimal DistanceFromRecentLowPercent { get; init; }
        public decimal CandleBodyStrengthPercent { get; init; }
        public decimal ClosePositionInRange { get; init; }
        public decimal? BtcReturn15mPercent { get; init; }
        public decimal? BtcReturn30mPercent { get; init; }
        public decimal? BtcReturn60mPercent { get; init; }
        public decimal? BtcTrendSlopePercent { get; init; }
        public string? BtcTrendRegime { get; init; }
        public string? BtcVolatilityRegime { get; init; }
        public bool? BtcAboveMediumMa { get; init; }
        public string? BtcMarketDirectionBucket { get; init; }
        public decimal? SymbolReturnRelativeToBtc60mPercent { get; init; }
        public decimal? MarketWideReturnProxyPercent { get; init; }
        public string? MarketWideDirection { get; init; }
    }

    private sealed record ForwardSnapshot
    {
        public decimal? Return15m { get; init; }
        public decimal? Return30m { get; init; }
        public decimal? Return60m { get; init; }
        public decimal? Return4h { get; init; }
        public decimal? Return8h { get; init; }
        public decimal? MfePercent { get; init; }
        public decimal? MaePercent { get; init; }
        public bool Target030BeforeStop025 { get; init; }
        public bool Target050BeforeStop050 { get; init; }
        public bool Target075BeforeStop075 { get; init; }
        public bool Target100BeforeStop100 { get; init; }
        public bool StopBeforeTarget050 { get; init; }
        public decimal Target050BeforeStop050Rate { get; init; }
        public decimal ExpectedNetAfterCostPercent { get; init; }
        public decimal LongEdgeScore { get; init; }

        public static ForwardSnapshot Empty(decimal cost)
            => new()
            {
                ExpectedNetAfterCostPercent = -cost,
                LongEdgeScore = Stop050Percent - cost
            };
    }

    private sealed record TargetStopMatrixSnapshot
    {
        public bool Target030BeforeStop025 { get; init; }
        public bool Target050BeforeStop050 { get; init; }
        public bool Target075BeforeStop075 { get; init; }
        public bool Target100BeforeStop100 { get; init; }
        public bool StopBeforeTarget050 { get; init; }
    }
}

public sealed record BtcContextSnapshot
{
    public decimal? BtcReturn15mPercent { get; init; }
    public decimal? BtcReturn30mPercent { get; init; }
    public decimal? BtcReturn60mPercent { get; init; }
    public decimal? BtcTrendSlopePercent { get; init; }
    public string? BtcTrendRegime { get; init; }
    public string? BtcVolatilityRegime { get; init; }
    public bool? BtcAboveMediumMa { get; init; }
    public string? BtcMarketDirectionBucket { get; init; }
}

public sealed class BtcContextIndex
{
    private readonly IReadOnlyList<KlineCandle> _candles;

    public BtcContextIndex(IReadOnlyList<KlineCandle> candles) => _candles = candles;

    public bool HasData => _candles.Count > 60;

    public BtcContextSnapshot? GetSnapshot(DateTime timeUtc)
    {
        var idx = FindIndex(timeUtc);
        if (idx < 60)
            return null;

        var close = _candles[idx].Close;
        var return15 = ComputeReturn(idx, 15);
        var return30 = ComputeReturn(idx, 30);
        var return60 = ComputeReturn(idx, 60);
        var trendSlope = ComputeTrendSlope(idx, close);
        var atrPercent = ComputeAtrPercent(idx, close);
        var mediumMa = ComputeSma(idx, 20);
        return new BtcContextSnapshot
        {
            BtcReturn15mPercent = return15,
            BtcReturn30mPercent = return30,
            BtcReturn60mPercent = return60,
            BtcTrendSlopePercent = trendSlope,
            BtcTrendRegime = ClassifyTrendRegime(trendSlope),
            BtcVolatilityRegime = ClassifyVolatilityRegime(atrPercent),
            BtcAboveMediumMa = mediumMa.HasValue ? close > mediumMa.Value : null,
            BtcMarketDirectionBucket = ClassifyMarketDirection(return30)
        };
    }

    public decimal? GetReturn60m(DateTime timeUtc) => GetSnapshot(timeUtc)?.BtcReturn60mPercent;

    public string? GetTrendRegime(DateTime timeUtc) => GetSnapshot(timeUtc)?.BtcTrendRegime;

    private decimal? ComputeReturn(int idx, int lookbackMinutes)
    {
        if (idx < lookbackMinutes)
            return null;
        var start = _candles[idx - lookbackMinutes].Close;
        var end = _candles[idx].Close;
        return start > 0m ? Math.Round((end - start) / start * 100m, 6) : null;
    }

    private decimal? ComputeTrendSlope(int idx, decimal close)
    {
        if (idx < 24 || close <= 0m)
            return null;
        var medium = ComputeSma(idx, 20);
        var priorMedium = ComputeSma(idx - 5, 20);
        if (!medium.HasValue || !priorMedium.HasValue)
            return null;
        return Math.Round((medium.Value - priorMedium.Value) / close * 100m, 6);
    }

    private decimal ComputeAtrPercent(int idx, decimal close)
    {
        if (idx < 14 || close <= 0m)
            return 0m;
        decimal sum = 0m;
        for (var i = idx - 13; i <= idx; i++)
        {
            var prevClose = _candles[i - 1].Close;
            var tr = Math.Max(_candles[i].High - _candles[i].Low,
                Math.Max(Math.Abs(_candles[i].High - prevClose), Math.Abs(_candles[i].Low - prevClose)));
            sum += tr;
        }

        return Math.Round(sum / 14m / close * 100m, 6);
    }

    private decimal? ComputeSma(int idx, int period)
    {
        if (idx < period - 1)
            return null;
        decimal sum = 0m;
        for (var i = idx - period + 1; i <= idx; i++)
            sum += _candles[i].Close;
        return sum / period;
    }

    private static string? ClassifyTrendRegime(decimal? trendSlopePercent)
        => trendSlopePercent switch
        {
            > 0.02m => "BtcUp",
            < -0.02m => "BtcDown",
            null => null,
            _ => "BtcFlat"
        };

    private static string ClassifyVolatilityRegime(decimal atrPercent)
        => atrPercent switch
        {
            < 0.20m => "Low",
            <= 0.60m => "Normal",
            _ => "Elevated"
        };

    private static string? ClassifyMarketDirection(decimal? return30m)
        => return30m switch
        {
            > 0.05m => "RiskOn",
            < -0.05m => "RiskOff",
            null => null,
            _ => "Neutral"
        };

    private int FindIndex(DateTime timeUtc)
    {
        // Candles are sorted ascending by OpenTimeUtc; binary search for the rightmost
        // index whose OpenTimeUtc <= timeUtc. This is called once per strided candle, so a
        // linear scan over the (large) 1m series here is the dominant O(n^2) cost otherwise.
        var lo = 0;
        var hi = _candles.Count - 1;
        var result = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (_candles[mid].OpenTimeUtc <= timeUtc)
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

public sealed class MarketWideContextIndex
{
    private readonly Dictionary<DateTime, decimal> _proxyByMinute;

    public MarketWideContextIndex(IReadOnlyDictionary<TradingSymbol, IReadOnlyList<KlineCandle>> symbolCandles, bool includeBtcInProxy = false)
    {
        _proxyByMinute = new Dictionary<DateTime, decimal>();
        var proxySets = symbolCandles
            .Where(kv => kv.Value.Count > 60)
            .Where(kv => includeBtcInProxy || kv.Key != TradingSymbol.BTCUSDT)
            .Select(kv => kv.Value)
            .ToArray();
        if (proxySets.Length == 0)
            return;

        var minLength = proxySets.Min(c => c.Count);
        for (var i = 60; i < minLength; i++)
        {
            decimal sum = 0m;
            var count = 0;
            DateTime? time = null;
            foreach (var candles in proxySets)
            {
                var start = candles[i - 60].Close;
                var end = candles[i].Close;
                if (start <= 0m)
                    continue;
                sum += (end - start) / start * 100m;
                count++;
                time ??= candles[i].OpenTimeUtc;
            }

            if (count > 0 && time.HasValue)
                _proxyByMinute[time.Value] = Math.Round(sum / count, 6);
        }
    }

    public decimal? GetProxyReturn(DateTime timeUtc)
    {
        var key = AlignMinute(timeUtc);
        return _proxyByMinute.TryGetValue(key, out var value) ? value : null;
    }

    public string? GetDirection(DateTime timeUtc)
    {
        var value = GetProxyReturn(timeUtc);
        if (!value.HasValue)
            return null;
        return value.Value switch
        {
            > 0.05m => "RiskOn",
            < -0.05m => "RiskOff",
            _ => "Neutral"
        };
    }

    private static DateTime AlignMinute(DateTime timeUtc)
        => new(timeUtc.Year, timeUtc.Month, timeUtc.Day, timeUtc.Hour, timeUtc.Minute, 0, DateTimeKind.Utc);
}
