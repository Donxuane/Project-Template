using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class LongShortFuturesFeasibilityStudyV1Scanner
{
    public static readonly (decimal Target, decimal Stop)[] StudyMatrixPairs =
    [
        (0.50m, 0.50m),
        (0.75m, 0.50m),
        (1.00m, 0.75m),
        (1.50m, 1.00m)
    ];

    private const decimal PrimaryTarget = 0.50m;
    private const decimal PrimaryStop = 0.50m;

    public static int ResolvePrimaryHorizonMinutes(string interval)
        => interval switch
        {
            "5m" => 240,
            "15m" => 480,
            "30m" => 720,
            _ => 480
        };

    public static IReadOnlyList<int> ResolveForwardHorizons(string interval)
        => interval switch
        {
            "5m" => [60, 240, 480],
            "15m" => [60, 240, 480, 720],
            "30m" => [240, 480, 720],
            _ => [240, 480, 720]
        };

    public static LongShortScanBatchResult ScanSymbolInterval(
        TradingSymbol symbol,
        string interval,
        string windowLabel,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> sourceOneMinuteCandles,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var observations = new List<LongShortFuturesFeasibilityObservation>();
        if (intervalCandles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 1)
            return new LongShortScanBatchResult(observations, []);

        var primaryHorizon = ResolvePrimaryHorizonMinutes(interval);
        var scenarios = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios();
        var matrixAccumulator = new MatrixAccumulator(windowLabel, symbol, interval, primaryHorizon, scenarios);
        var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(interval);

        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candle = intervalCandles[i];
            if (candle.Close <= 0m)
                continue;

            var entryPrice = candle.Close;
            var entryTime = candle.OpenTimeUtc;
            var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                intervalCandles, i, btcContext, marketWideContext, entryTime);

            var forward = ComputeDirectionalForward(
                sourceOneMinuteCandles,
                entryTime,
                entryPrice,
                primaryHorizon);

            var longPrimary = forward.LongOutcomes[(PrimaryTarget, PrimaryStop)];
            var shortPrimary = forward.ShortOutcomes[(PrimaryTarget, PrimaryStop)];

            decimal ExpectedForScenario(FeasibilityCostScenario scenario, DirectionalOutcome outcome, LongShortDirection direction)
            {
                var horizonReturn = direction == LongShortDirection.Long
                    ? forward.LongHorizonReturnPercent
                    : forward.ShortHorizonReturnPercent;
                var cost = LongShortFuturesFeasibilityStudyV1CostModel.EstimateTotalCostPercent(scenario, primaryHorizon);
                return LongShortFuturesFeasibilityStudyV1CostModel.ExpectedNetPercent(
                    outcome.TargetBeforeStop,
                    outcome.StopBeforeTarget,
                    horizonReturn,
                    PrimaryTarget,
                    PrimaryStop,
                    cost);
            }

            var longSpot = ExpectedForScenario(scenarios.First(s => s.Label == "spot-conservative"), longPrimary, LongShortDirection.Long);
            var shortSpot = ExpectedForScenario(scenarios.First(s => s.Label == "spot-conservative"), shortPrimary, LongShortDirection.Short);
            var longModerate = ExpectedForScenario(scenarios.First(s => s.Label == "futures-moderate"), longPrimary, LongShortDirection.Long);
            var shortModerate = ExpectedForScenario(scenarios.First(s => s.Label == "futures-moderate"), shortPrimary, LongShortDirection.Short);
            var longLow = ExpectedForScenario(scenarios.First(s => s.Label == "futures-low"), longPrimary, LongShortDirection.Long);
            var shortLow = ExpectedForScenario(scenarios.First(s => s.Label == "futures-low"), shortPrimary, LongShortDirection.Short);
            var longStress = ExpectedForScenario(scenarios.First(s => s.Label == "futures-stress"), longPrimary, LongShortDirection.Long);
            var shortStress = ExpectedForScenario(scenarios.First(s => s.Label == "futures-stress"), shortPrimary, LongShortDirection.Short);

            var bestDir = longModerate >= shortModerate ? LongShortDirection.Long : LongShortDirection.Short;
            observations.Add(new LongShortFuturesFeasibilityObservation
            {
                WindowLabel = windowLabel,
                Symbol = symbol,
                Interval = interval,
                TimeUtc = entryTime,
                HourOfDayUtc = entryTime.Hour,
                SessionBucket = MarketRegimeForwardEdgeScanner.ResolveSessionBucket(entryTime.Hour),
                VolatilityRegime = features.VolatilityRegime,
                TrendRegime = features.TrendRegime,
                TrendSlopePercent = features.TrendSlopePercent,
                RangeWidthPercent = features.RangeWidthPercent,
                DistanceFromRecentHighPercent = features.DistanceFromRecentHighPercent,
                DistanceFromRecentLowPercent = features.DistanceFromRecentLowPercent,
                RecentReturn60CandlesPercent = features.RecentReturn60,
                AtrPercent = features.AtrPercent,
                VolumeExpansionRatio = features.VolumeExpansionRatio,
                BtcReturn30mPercent = features.BtcReturn30mPercent,
                BtcTrendRegime = features.BtcTrendRegime,
                BtcMarketDirectionBucket = features.BtcMarketDirectionBucket,
                MarketWideReturnProxyPercent = features.MarketWideReturnProxyPercent,
                EntryPrice = entryPrice,
                PrimaryForwardHorizonMinutes = primaryHorizon,
                LongForwardMfePercent = forward.LongMfePercent,
                LongForwardMaePercent = forward.LongMaePercent,
                ShortForwardMfePercent = forward.ShortMfePercent,
                ShortForwardMaePercent = forward.ShortMaePercent,
                LongTarget050BeforeStop050 = longPrimary.TargetBeforeStop,
                ShortTarget050BeforeStop050 = shortPrimary.TargetBeforeStop,
                LongExpectedNetSpotConservativePercent = longSpot,
                ShortExpectedNetSpotConservativePercent = shortSpot,
                LongExpectedNetFuturesModeratePercent = longModerate,
                ShortExpectedNetFuturesModeratePercent = shortModerate,
                LongExpectedNetFuturesLowPercent = longLow,
                ShortExpectedNetFuturesLowPercent = shortLow,
                LongExpectedNetFuturesStressPercent = longStress,
                ShortExpectedNetFuturesStressPercent = shortStress,
                BestDirectionExpectedNetFuturesModeratePercent = Math.Max(longModerate, shortModerate),
                BestDirectionFuturesModerate = bestDir
            });

            matrixAccumulator.Add(forward);
        }

        return new LongShortScanBatchResult(observations, matrixAccumulator.BuildRows());
    }

    private static DirectionalForwardSnapshot ComputeDirectionalForward(
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        int primaryHorizonMinutes)
    {
        var entryIdx = FindEntryIndex(oneMinuteCandles, entryTimeUtc);
        if (entryIdx < 0)
            return DirectionalForwardSnapshot.Empty();

        var longOutcomes = new Dictionary<(decimal, decimal), DirectionalOutcome>();
        var shortOutcomes = new Dictionary<(decimal, decimal), DirectionalOutcome>();
        foreach (var pair in StudyMatrixPairs)
        {
            longOutcomes[pair] = SimulatePair(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes, pair.Target, pair.Stop, LongShortDirection.Long);
            shortOutcomes[pair] = SimulatePair(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes, pair.Target, pair.Stop, LongShortDirection.Short);
        }

        return new DirectionalForwardSnapshot
        {
            LongOutcomes = longOutcomes,
            ShortOutcomes = shortOutcomes,
            LongHorizonReturnPercent = ComputeHorizonReturn(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes, true),
            ShortHorizonReturnPercent = ComputeHorizonReturn(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes, false),
            LongMfePercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMfePercent(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes),
            LongMaePercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMaePercent(oneMinuteCandles, entryTimeUtc, entryPrice, primaryHorizonMinutes),
            ShortMfePercent = ComputeShortMfe(oneMinuteCandles, entryIdx, entryPrice, primaryHorizonMinutes),
            ShortMaePercent = ComputeShortMae(oneMinuteCandles, entryIdx, entryPrice, primaryHorizonMinutes)
        };
    }

    private static DirectionalOutcome SimulatePair(
        IReadOnlyList<KlineCandle> candles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        int horizonMinutes,
        decimal targetPercent,
        decimal stopPercent,
        LongShortDirection direction)
    {
        var entryIdx = FindEntryIndex(candles, entryTimeUtc);
        if (entryIdx < 0 || entryPrice <= 0m)
            return DirectionalOutcome.Unresolved;

        var end = candles[entryIdx].OpenTimeUtc.AddMinutes(horizonMinutes);
        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
                break;

            var candle = candles[i];
            bool hitTarget;
            bool hitStop;
            if (direction == LongShortDirection.Long)
            {
                var targetPrice = entryPrice * (1m + targetPercent / 100m);
                var stopPrice = entryPrice * (1m - stopPercent / 100m);
                hitTarget = candle.High >= targetPrice;
                hitStop = candle.Low <= stopPrice;
                if (hitTarget && hitStop)
                    return candle.Open <= entryPrice
                        ? new DirectionalOutcome(false, true)
                        : new DirectionalOutcome(true, false);
            }
            else
            {
                var targetPrice = entryPrice * (1m - targetPercent / 100m);
                var stopPrice = entryPrice * (1m + stopPercent / 100m);
                hitTarget = candle.Low <= targetPrice;
                hitStop = candle.High >= stopPrice;
                if (hitTarget && hitStop)
                    return candle.Open >= entryPrice
                        ? new DirectionalOutcome(false, true)
                        : new DirectionalOutcome(true, false);
            }

            if (hitStop)
                return new DirectionalOutcome(false, true);
            if (hitTarget)
                return new DirectionalOutcome(true, false);
        }

        return DirectionalOutcome.Unresolved;
    }

    private static decimal ComputeHorizonReturn(
        IReadOnlyList<KlineCandle> candles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        int horizonMinutes,
        bool longDirection)
    {
        var entryIdx = FindEntryIndex(candles, entryTimeUtc);
        if (entryIdx < 0 || entryPrice <= 0m)
            return 0m;

        var end = entryTimeUtc.AddMinutes(horizonMinutes);
        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
            {
                var prev = candles[i - 1];
                return longDirection
                    ? Math.Round((prev.Close - entryPrice) / entryPrice * 100m, 6)
                    : Math.Round((entryPrice - prev.Close) / entryPrice * 100m, 6);
            }
        }

        return 0m;
    }

    private static decimal? ComputeShortMfe(IReadOnlyList<KlineCandle> candles, int entryIdx, decimal entryPrice, int horizonMinutes)
    {
        if (entryPrice <= 0m)
            return null;
        var end = candles[entryIdx].OpenTimeUtc.AddMinutes(horizonMinutes);
        decimal best = 0m;
        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
                break;
            var move = (entryPrice - candles[i].Low) / entryPrice * 100m;
            if (move > best)
                best = move;
        }

        return Math.Round(best, 6);
    }

    private static decimal? ComputeShortMae(IReadOnlyList<KlineCandle> candles, int entryIdx, decimal entryPrice, int horizonMinutes)
    {
        if (entryPrice <= 0m)
            return null;
        var end = candles[entryIdx].OpenTimeUtc.AddMinutes(horizonMinutes);
        decimal worst = 0m;
        for (var i = entryIdx; i < candles.Count; i++)
        {
            if (candles[i].OpenTimeUtc > end)
                break;
            var move = (candles[i].High - entryPrice) / entryPrice * 100m;
            if (move > worst)
                worst = move;
        }

        return Math.Round(worst, 6);
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

    private static decimal? Median(IEnumerable<decimal> values)
    {
        var arr = values.OrderBy(v => v).ToArray();
        if (arr.Length == 0)
            return null;
        var mid = arr.Length / 2;
        return arr.Length % 2 == 0
            ? Math.Round((arr[mid - 1] + arr[mid]) / 2m, 6)
            : Math.Round(arr[mid], 6);
    }

    private sealed record DirectionalOutcome(bool TargetBeforeStop, bool StopBeforeTarget)
    {
        public static DirectionalOutcome Unresolved => new(false, false);
    }

    private sealed record DirectionalForwardSnapshot
    {
        public Dictionary<(decimal, decimal), DirectionalOutcome> LongOutcomes { get; init; } = [];
        public Dictionary<(decimal, decimal), DirectionalOutcome> ShortOutcomes { get; init; } = [];
        public decimal LongHorizonReturnPercent { get; init; }
        public decimal ShortHorizonReturnPercent { get; init; }
        public decimal? LongMfePercent { get; init; }
        public decimal? LongMaePercent { get; init; }
        public decimal? ShortMfePercent { get; init; }
        public decimal? ShortMaePercent { get; init; }

        public static DirectionalForwardSnapshot Empty()
            => new();
    }

    private sealed class MatrixAccumulator(
        string windowLabel,
        TradingSymbol symbol,
        string interval,
        int horizonMinutes,
        IReadOnlyList<FeasibilityCostScenario> scenarios)
    {
        private readonly Dictionary<(LongShortDirection Direction, decimal Target, decimal Stop, string Scenario), List<decimal>> _expectedNets = [];
        private readonly Dictionary<(LongShortDirection Direction, decimal Target, decimal Stop), OutcomeCounts> _outcomeCounts = [];

        public void Add(DirectionalForwardSnapshot forward)
        {
            foreach (var direction in new[] { LongShortDirection.Long, LongShortDirection.Short })
            {
                var outcomes = direction == LongShortDirection.Long ? forward.LongOutcomes : forward.ShortOutcomes;
                var horizonReturn = direction == LongShortDirection.Long
                    ? forward.LongHorizonReturnPercent
                    : forward.ShortHorizonReturnPercent;

                foreach (var (target, stop) in StudyMatrixPairs)
                {
                    var outcome = outcomes[(target, stop)];
                    var outcomeKey = (direction, target, stop);
                    if (!_outcomeCounts.TryGetValue(outcomeKey, out var counts))
                    {
                        counts = new OutcomeCounts();
                        _outcomeCounts[outcomeKey] = counts;
                    }

                    if (outcome.TargetBeforeStop)
                        counts.TargetBeforeStopCount++;
                    if (outcome.StopBeforeTarget)
                        counts.StopBeforeTargetCount++;
                    counts.SampleCount++;

                    foreach (var scenario in scenarios)
                    {
                        var key = (direction, target, stop, scenario.Label);
                        if (!_expectedNets.TryGetValue(key, out var nets))
                        {
                            nets = [];
                            _expectedNets[key] = nets;
                        }

                        var totalCost = LongShortFuturesFeasibilityStudyV1CostModel.EstimateTotalCostPercent(scenario, horizonMinutes);
                        nets.Add(LongShortFuturesFeasibilityStudyV1CostModel.ExpectedNetPercent(
                            outcome.TargetBeforeStop,
                            outcome.StopBeforeTarget,
                            horizonReturn,
                            target,
                            stop,
                            totalCost));
                    }
                }
            }
        }

        public IReadOnlyList<LongShortTargetStopMatrixRow> BuildRows()
        {
            if (_expectedNets.Count == 0)
                return [];

            return _expectedNets
                .Select(kv =>
                {
                    var outcomeKey = (kv.Key.Direction, kv.Key.Target, kv.Key.Stop);
                    _outcomeCounts.TryGetValue(outcomeKey, out var counts);
                    counts ??= new OutcomeCounts();
                    return new LongShortTargetStopMatrixRow
                    {
                        WindowLabel = windowLabel,
                        Symbol = symbol,
                        Interval = interval,
                        Direction = kv.Key.Direction,
                        CostScenarioLabel = kv.Key.Scenario,
                        TargetPercent = kv.Key.Target,
                        StopPercent = kv.Key.Stop,
                        ForwardHorizonMinutes = horizonMinutes,
                        SampleCount = counts.SampleCount,
                        TargetBeforeStopCount = counts.TargetBeforeStopCount,
                        StopBeforeTargetCount = counts.StopBeforeTargetCount,
                        TargetBeforeStopRate = counts.SampleCount == 0
                            ? 0m
                            : Math.Round((decimal)counts.TargetBeforeStopCount / counts.SampleCount, 6),
                        MedianExpectedNetPercent = Median(kv.Value) ?? 0m
                    };
                })
                .OrderBy(r => r.Direction)
                .ThenBy(r => r.TargetPercent)
                .ThenBy(r => r.CostScenarioLabel, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private sealed class OutcomeCounts
        {
            public int SampleCount { get; set; }
            public int TargetBeforeStopCount { get; set; }
            public int StopBeforeTargetCount { get; set; }
        }
    }
}
