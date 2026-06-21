using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class Sol30mNearMissConversionHistoryStudyBuilder
{
    private sealed record NearMissSnapshot(
        int SignalIndex,
        DateTime EventTimeUtc,
        string NearMissClassification,
        string FailedCondition,
        decimal DistanceToEntryPercent,
        decimal LatestClose,
        decimal RecentHigh,
        decimal RecentLow,
        decimal AtrPercent,
        bool ElevatedVolPassed,
        bool CooldownClear,
        bool NoOpenTradeOverlap,
        bool ActivationPassed,
        bool EntryPresent);

    public static Sol30mNearMissConversionHistoryStudyResult Build(
        DateTime runAtUtc,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        CrossSymbolComboKey key,
        CrossSymbolActivationConfig activationConfig,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressPlusTrades,
        ShortWindowFlowFeatureIndex flowIndex,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext,
        int cooldownCandles,
        decimal? currentNearMissDistancePercent)
    {
        var events = new List<Sol30mNearMissConversionHistoryEventRow>();
        var wasNearMiss = false;

        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i++)
        {
            var eventTimeUtc = intervalCandles[i + 1].OpenTimeUtc;
            if (eventTimeUtc < studyStartUtc || eventTimeUtc > studyEndUtc)
                continue;

            var snapshot = EvaluateSnapshot(
                key,
                activationConfig,
                intervalCandles,
                baseTrades,
                moderateTrades,
                flowIndex,
                btcContext,
                marketWideContext,
                studyStartUtc,
                cooldownCandles,
                i,
                eventTimeUtc);

            if (!snapshot.ActivationPassed || snapshot.EntryPresent)
            {
                wasNearMiss = false;
                continue;
            }

            var isTargetNearMiss = snapshot.NearMissClassification == "OneConditionAway"
                                   && string.Equals(snapshot.FailedCondition, "NearExtremeDistance", StringComparison.Ordinal);

            if (!isTargetNearMiss)
            {
                wasNearMiss = false;
                continue;
            }

            if (wasNearMiss)
                continue;

            wasNearMiss = true;
            var conversion = EvaluateConversion(
                key,
                intervalCandles,
                baseTrades,
                moderateTrades,
                stressPlusTrades,
                btcContext,
                marketWideContext,
                studyStartUtc,
                cooldownCandles,
                snapshot,
                studyEndUtc);

            events.Add(new Sol30mNearMissConversionHistoryEventRow
            {
                EventTimeUtc = snapshot.EventTimeUtc,
                Symbol = key.Symbol.ToString(),
                Interval = key.Interval,
                Direction = key.Direction.ToString(),
                ActivationPassed = snapshot.ActivationPassed,
                NearMissClassification = snapshot.NearMissClassification,
                FailedCondition = snapshot.FailedCondition,
                DistanceToEntryPercent = snapshot.DistanceToEntryPercent,
                DistanceBucket = Sol30mNearMissConversionHistoryStudyCatalog.DistanceBucket(snapshot.DistanceToEntryPercent),
                LatestClose = snapshot.LatestClose,
                RecentHigh = snapshot.RecentHigh,
                RecentLow = snapshot.RecentLow,
                AtrPercent = snapshot.AtrPercent,
                ElevatedVolPassed = snapshot.ElevatedVolPassed,
                CooldownClear = snapshot.CooldownClear,
                NoOpenTradeOverlap = snapshot.NoOpenTradeOverlap,
                ConvertedToExactEntry = conversion.Converted,
                ConversionTimeUtc = conversion.ConversionTimeUtc,
                MinutesToConversion = conversion.MinutesToConversion,
                CandlesToConversion = conversion.CandlesToConversion,
                MaxDistanceBeforeConversion = conversion.MaxDistanceBeforeConversion,
                DidPriceMoveTowardEntry = conversion.DidPriceMoveTowardEntry,
                DidPriceMoveAwayFromEntry = conversion.DidPriceMoveAwayFromEntry,
                ConversionEntryPrice = conversion.ConversionEntryPrice,
                ConversionExitTimeUtc = conversion.ConversionExitTimeUtc,
                ConversionExitReason = conversion.ConversionExitReason ?? string.Empty,
                ConversionNetModerate = conversion.ConversionNetModerate,
                ConversionNetStressPlus = conversion.ConversionNetStressPlus,
                IsWinnerModerate = conversion.IsWinnerModerate,
                IsWinnerStressPlus = conversion.IsWinnerStressPlus,
                ConvertedWithin1Candle = conversion.ConvertedWithin1Candle,
                ConvertedWithin2Candles = conversion.ConvertedWithin2Candles,
                ConvertedWithin4Candles = conversion.ConvertedWithin4Candles,
                ConvertedWithin8Candles = conversion.ConvertedWithin8Candles,
                ConvertedWithin24h = conversion.ConvertedWithin24h
            });
        }

        var conversions = events.Where(e => e.ConvertedToExactEntry).ToArray();
        var nonConversions = events.Where(e => !e.ConvertedToExactEntry).ToArray();
        var distances = events.Select(e => e.DistanceToEntryPercent).ToArray();
        var bucketStats = BuildBucketStats(events);
        var currentBucket = currentNearMissDistancePercent.HasValue
            ? Sol30mNearMissConversionHistoryStudyCatalog.DistanceBucket(currentNearMissDistancePercent.Value)
            : string.Empty;

        var total = events.Count;
        var converted24h = events.Count(e => e.ConvertedWithin24h);
        var converted4 = events.Count(e => e.ConvertedWithin4Candles);
        var rate4 = total == 0 ? 0m : Math.Round((decimal)converted4 / total, 6);
        var rate24 = total == 0 ? 0m : Math.Round((decimal)converted24h / total, 6);

        var convertedModerateNet = conversions.Sum(e => e.ConversionNetModerate ?? 0m);
        var convertedStressNet = conversions.Sum(e => e.ConversionNetStressPlus ?? 0m);
        var convertedWinRate = conversions.Length == 0
            ? 0m
            : Math.Round((decimal)conversions.Count(e => e.IsWinnerStressPlus == true) / conversions.Length, 6);
        var convertedPf = ComputeProfitFactor(conversions.Select(e => e.ConversionNetStressPlus ?? 0m).ToArray());

        var recommendation = ResolveRecommendation(
            total,
            rate24,
            convertedStressNet,
            convertedWinRate,
            bucketStats,
            currentBucket);

        var plainEnglish = BuildPlainEnglish(
            total,
            converted24h,
            converted4,
            rate24,
            rate4,
            conversions,
            convertedStressNet,
            bucketStats,
            currentBucket,
            currentNearMissDistancePercent,
            recommendation);

        var summary = new Sol30mNearMissConversionHistorySummaryRow
        {
            RunAtUtc = runAtUtc,
            StudyStartUtc = studyStartUtc,
            StudyEndUtc = studyEndUtc,
            CandidateKey = Sol30mNearMissConversionHistoryStudyCatalog.CandidateKey,
            ActivationRule = Sol30mNearMissConversionHistoryStudyCatalog.ActivationRuleName,
            CurrentNearMissDistancePercent = currentNearMissDistancePercent,
            TotalNearMissEvents = total,
            OneConditionAwayEvents = total,
            ConvertedWithin1Candle = events.Count(e => e.ConvertedWithin1Candle),
            ConvertedWithin2Candles = events.Count(e => e.ConvertedWithin2Candles),
            ConvertedWithin4Candles = converted4,
            ConvertedWithin8Candles = events.Count(e => e.ConvertedWithin8Candles),
            ConvertedWithin24h = converted24h,
            ConversionRateWithin4Candles = rate4,
            ConversionRateWithin24h = rate24,
            ConvertedTradeCount = conversions.Length,
            ConvertedNetModerate = Math.Round(convertedModerateNet, 8),
            ConvertedNetStressPlus = Math.Round(convertedStressNet, 8),
            ConvertedWinRate = convertedWinRate,
            ConvertedProfitFactor = convertedPf,
            NonConvertedCount = nonConversions.Length,
            AverageDistanceToEntry = distances.Length == 0 ? 0m : Math.Round(distances.Average(), 6),
            MedianDistanceToEntry = Median(distances),
            BestDistanceBucket = bucketStats.BestBucket,
            WorstDistanceBucket = bucketStats.WorstBucket,
            CurrentNearMissSimilarityBucket = currentBucket,
            Recommendation = recommendation,
            PlainEnglish = plainEnglish,
            CompactSummaryLine =
                $"SOL 30m near-miss conversion | events={total} converted24h={converted24h} rate24h={rate24:P1} convertedStressNet={convertedStressNet:F2} recommendation={recommendation}"
        };

        return new Sol30mNearMissConversionHistoryStudyResult(summary, events, conversions, nonConversions);
    }

    private sealed record ConversionOutcome(
        bool Converted,
        DateTime? ConversionTimeUtc,
        int? MinutesToConversion,
        int? CandlesToConversion,
        decimal? MaxDistanceBeforeConversion,
        bool DidPriceMoveTowardEntry,
        bool DidPriceMoveAwayFromEntry,
        decimal? ConversionEntryPrice,
        DateTime? ConversionExitTimeUtc,
        string? ConversionExitReason,
        decimal? ConversionNetModerate,
        decimal? ConversionNetStressPlus,
        bool? IsWinnerModerate,
        bool? IsWinnerStressPlus,
        bool ConvertedWithin1Candle,
        bool ConvertedWithin2Candles,
        bool ConvertedWithin4Candles,
        bool ConvertedWithin8Candles,
        bool ConvertedWithin24h);

    private static ConversionOutcome EvaluateConversion(
        CrossSymbolComboKey key,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> stressPlusTrades,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext,
        DateTime studyStartUtc,
        int cooldownCandles,
        NearMissSnapshot snapshot,
        DateTime studyEndUtc)
    {
        var eventIdx = snapshot.SignalIndex;
        var eventDistance = snapshot.DistanceToEntryPercent;
        var deadline24h = snapshot.EventTimeUtc.AddHours(Sol30mNearMissConversionHistoryStudyCatalog.ConversionHoursWindow);
        var maxOffset = Math.Min(
            intervalCandles.Count - 2 - eventIdx,
            (int)Math.Ceiling(Sol30mNearMissConversionHistoryStudyCatalog.ConversionHoursWindow * 60m / IntervalMinutes(key.Interval)));

        decimal? minDistance = eventDistance;
        decimal? maxDistance = eventDistance;
        var movedToward = false;
        var movedAway = false;
        int? conversionCandles = null;
        DateTime? conversionTime = null;
        FuturesTestnetShadowEvaluator.EntryState? conversionEntry = null;

        for (var offset = 1; offset <= maxOffset; offset++)
        {
            var futureIdx = eventIdx + offset;
            if (futureIdx >= intervalCandles.Count - 1)
                break;

            var futureEvalUtc = intervalCandles[futureIdx + 1].OpenTimeUtc;
            if (futureEvalUtc > studyEndUtc || futureEvalUtc > deadline24h)
                break;

            var futureDistance = ComputeDistanceToEntry(
                key.Direction, intervalCandles, futureIdx, btcContext, marketWideContext);
            if (futureDistance.HasValue)
            {
                minDistance = minDistance.HasValue ? Math.Min(minDistance.Value, futureDistance.Value) : futureDistance;
                maxDistance = maxDistance.HasValue ? Math.Max(maxDistance.Value, futureDistance.Value) : futureDistance;
                if (futureDistance.Value < eventDistance)
                    movedToward = true;
                if (futureDistance.Value > eventDistance)
                    movedAway = true;
            }

            var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
                key, intervalCandles, baseTrades, futureEvalUtc, studyStartUtc, cooldownCandles);

            if (!entry.Present)
                continue;

            conversionCandles = offset;
            conversionTime = entry.EntryTimeUtc ?? futureEvalUtc;
            conversionEntry = entry;
            break;
        }

        if (conversionEntry is null || !conversionEntry.Present)
        {
            return new ConversionOutcome(
                false, null, null, null,
                maxDistance,
                movedToward, movedAway,
                null, null, null, null, null, null, null,
                false, false, false, false, false);
        }

        var baseTrade = baseTrades.FirstOrDefault(t =>
            t.TimeUtc == conversionEntry.EntryTimeUtc
            || (conversionEntry.EntryTimeUtc.HasValue
                && Math.Abs((t.TimeUtc - conversionEntry.EntryTimeUtc.Value).TotalMinutes) < 1));

        RegimeDriftDiagnosticTrade? moderate = null;
        RegimeDriftDiagnosticTrade? stress = null;
        if (baseTrade is not null)
        {
            moderate = moderateTrades.FirstOrDefault(t => t.EntryTimeUtc == baseTrade.TimeUtc);
            stress = stressPlusTrades.FirstOrDefault(t => t.EntryTimeUtc == baseTrade.TimeUtc);
        }

        var candles = conversionCandles ?? 0;
        var minutes = conversionTime.HasValue
            ? (int)Math.Round((conversionTime.Value - snapshot.EventTimeUtc).TotalMinutes)
            : (int?)null;

        return new ConversionOutcome(
            true,
            conversionTime,
            minutes,
            conversionCandles,
            maxDistance,
            movedToward,
            movedAway,
            conversionEntry.EntryPrice,
            baseTrade is null ? null : baseTrade.TimeUtc.AddMinutes((double)baseTrade.DurationMinutes),
            baseTrade?.ExitReason,
            moderate?.NetPnlQuote,
            stress?.NetPnlQuote,
            moderate?.IsWinner,
            stress?.IsWinner,
            candles <= 1,
            candles <= 2,
            candles <= 4,
            candles <= 8,
            conversionTime.HasValue && conversionTime.Value <= deadline24h);
    }

    private static NearMissSnapshot EvaluateSnapshot(
        CrossSymbolComboKey key,
        CrossSymbolActivationConfig activationConfig,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        ShortWindowFlowFeatureIndex flowIndex,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext,
        DateTime studyStartUtc,
        int cooldownCandles,
        int signalIndex,
        DateTime eventTimeUtc)
    {
        var activation = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolActivation(
            activationConfig,
            key,
            moderateTrades,
            eventTimeUtc,
            studyStartUtc,
            flowIndex);

        var entry = FuturesTestnetShadowEvaluator.EvaluateCrossSymbolEntryNow(
            key, intervalCandles, baseTrades, eventTimeUtc, studyStartUtc, cooldownCandles);

        var signalCandle = intervalCandles[signalIndex];
        var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
            intervalCandles,
            signalIndex,
            btcContext,
            marketWideContext,
            signalCandle.OpenTimeUtc);

        var swingHigh = signalCandle.Close > 0m
            ? signalCandle.Close * (1m + features.DistanceFromRecentHighPercent / 100m)
            : 0m;
        var swingLow = signalCandle.Close > 0m
            ? signalCandle.Close * (1m - features.DistanceFromRecentLowPercent / 100m)
            : 0m;

        var nearExtremeThreshold = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.NearExtremeAtrMultiple * features.AtrPercent;
        var distanceToExtreme = key.Direction == LongShortDirection.Short
            ? features.DistanceFromRecentHighPercent
            : features.DistanceFromRecentLowPercent;
        var distanceToEntry = Math.Max(0m, Math.Round(distanceToExtreme - nearExtremeThreshold, 6));

        var atrPassed = features.AtrPercent > 0m;
        var nearExtremePassed = key.Direction == LongShortDirection.Short
            ? features.DistanceFromRecentHighPercent <= nearExtremeThreshold
            : features.DistanceFromRecentLowPercent <= nearExtremeThreshold;
        var elevatedVolPassed = IsElevatedVol(features);
        var openTradeBlocked = HasOpenTradeOverlap(baseTrades, eventTimeUtc, studyStartUtc);
        var cooldownBlocked = IsCooldownActive(baseTrades, intervalCandles, eventTimeUtc, studyStartUtc, cooldownCandles);

        var failed = new List<string>();
        if (!atrPassed) failed.Add("AtrPercentPositive");
        if (!nearExtremePassed) failed.Add("NearExtremeDistance");
        if (!elevatedVolPassed) failed.Add("ElevatedVol");
        if (openTradeBlocked) failed.Add("NoOpenTradeOverlap");
        if (cooldownBlocked) failed.Add("CooldownClear");

        var failedCount = failed.Count;
        var classification = entry.Present
            ? "ExactEntrySignal"
            : failedCount == 1
                ? "OneConditionAway"
                : failedCount == 2
                    ? "TwoConditionsAway"
                    : distanceToEntry <= EntryNearMissAuditV1Catalog.PriceDistanceNearThresholdPercent
                        ? "PriceDistanceNear"
                        : "ActivationOnlyFarFromEntry";

        return new NearMissSnapshot(
            signalIndex,
            eventTimeUtc,
            classification,
            failed.FirstOrDefault() ?? string.Empty,
            distanceToEntry,
            signalCandle.Close,
            Math.Round(swingHigh, 8),
            Math.Round(swingLow, 8),
            features.AtrPercent,
            elevatedVolPassed,
            !cooldownBlocked,
            !openTradeBlocked,
            activation.Passed,
            entry.Present);
    }

    private static decimal? ComputeDistanceToEntry(
        LongShortDirection direction,
        IReadOnlyList<KlineCandle> intervalCandles,
        int signalIndex,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext)
    {
        if (signalIndex < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles
            || signalIndex >= intervalCandles.Count - 1)
            return null;

        var signalCandle = intervalCandles[signalIndex];
        var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
            intervalCandles,
            signalIndex,
            btcContext,
            marketWideContext,
            signalCandle.OpenTimeUtc);

        var nearExtremeThreshold = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.NearExtremeAtrMultiple * features.AtrPercent;
        var distanceToExtreme = direction == LongShortDirection.Short
            ? features.DistanceFromRecentHighPercent
            : features.DistanceFromRecentLowPercent;
        return Math.Max(0m, Math.Round(distanceToExtreme - nearExtremeThreshold, 6));
    }

    public static decimal? EvaluateCurrentNearMissDistance(
        CrossSymbolComboKey key,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<RegimeDriftDiagnosticTrade> moderateTrades,
        ShortWindowFlowFeatureIndex flowIndex,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext,
        DateTime studyStartUtc,
        int cooldownCandles,
        DateTime evalUtc)
    {
        var signalIndex = ResolveLatestSignalCandleIndex(intervalCandles, evalUtc);
        var snapshot = EvaluateSnapshot(
            key,
            Sol30mNearMissConversionHistoryStudyCatalog.ResolveActivationConfig(),
            intervalCandles,
            baseTrades,
            moderateTrades,
            flowIndex,
            btcContext,
            marketWideContext,
            studyStartUtc,
            cooldownCandles,
            signalIndex,
            evalUtc);

        if (!snapshot.ActivationPassed
            || snapshot.EntryPresent
            || snapshot.NearMissClassification != "OneConditionAway"
            || !string.Equals(snapshot.FailedCondition, "NearExtremeDistance", StringComparison.Ordinal))
        {
            return null;
        }

        return snapshot.DistanceToEntryPercent;
    }

    private static bool IsElevatedVol(MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features)
        => features.VolatilityRegime == "Elevated"
           || (features.VolatilityRegime == "Normal"
               && features.VolumeExpansionRatio >= NoPaidDataShortWindowMultiSymbolResearchV2Catalog.VolumeExpansionMin);

    private static bool HasOpenTradeOverlap(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        DateTime evalUtc,
        DateTime frozenStartUtc)
        => baseTrades.Any(t =>
            t.TimeUtc <= evalUtc
            && t.TimeUtc.AddMinutes((double)t.DurationMinutes) > evalUtc
            && t.TimeUtc >= frozenStartUtc);

    private static bool IsCooldownActive(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        IReadOnlyList<KlineCandle> intervalCandles,
        DateTime evalUtc,
        DateTime frozenStartUtc,
        int cooldownCandles)
    {
        var lastExit = baseTrades
            .Where(t => t.TimeUtc.AddMinutes((double)t.DurationMinutes) <= evalUtc && t.TimeUtc >= frozenStartUtc)
            .OrderByDescending(t => t.TimeUtc.AddMinutes((double)t.DurationMinutes))
            .FirstOrDefault();
        if (lastExit is null)
            return false;

        var exitUtc = lastExit.TimeUtc.AddMinutes((double)lastExit.DurationMinutes);
        var exitIdx = FindCandleIndex(intervalCandles, exitUtc);
        var nextAllowed = exitIdx + 1 + cooldownCandles;
        var latestIdx = ResolveLatestSignalCandleIndex(intervalCandles, evalUtc);
        return latestIdx < nextAllowed;
    }

    private static int ResolveLatestSignalCandleIndex(IReadOnlyList<KlineCandle> intervalCandles, DateTime evalUtc)
    {
        var maxIndex = intervalCandles.Count - 2;
        if (maxIndex < MarketRegimeForwardEdgeScanner.MinimumWarmupCandles)
            return Math.Max(0, maxIndex);

        for (var i = maxIndex; i >= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i--)
        {
            if (intervalCandles[i + 1].OpenTimeUtc <= evalUtc)
                return i;
        }

        return Math.Max(MarketRegimeForwardEdgeScanner.MinimumWarmupCandles, maxIndex);
    }

    private static int FindCandleIndex(IReadOnlyList<KlineCandle> candles, DateTime timeUtc)
    {
        var lo = 0;
        var hi = candles.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (candles[mid].OpenTimeUtc > timeUtc)
                hi = mid;
            else
                lo = mid + 1;
        }

        return Math.Max(0, lo - 1);
    }

    private static int IntervalMinutes(string interval) => interval switch
    {
        "30m" => 30,
        "15m" => 15,
        "5m" => 5,
        _ => 30
    };

    private sealed record BucketStats(string BestBucket, string WorstBucket, IReadOnlyDictionary<string, decimal> RatesByBucket);

    private static BucketStats BuildBucketStats(IReadOnlyList<Sol30mNearMissConversionHistoryEventRow> events)
    {
        var rates = Sol30mNearMissConversionHistoryStudyCatalog.DistanceBucketLabels
            .ToDictionary(
                b => b,
                b =>
                {
                    var bucketEvents = events.Where(e => e.DistanceBucket == b).ToArray();
                    return bucketEvents.Length == 0
                        ? 0m
                        : Math.Round((decimal)bucketEvents.Count(e => e.ConvertedWithin24h) / bucketEvents.Length, 6);
                },
                StringComparer.Ordinal);

        var ranked = rates.Where(kv => events.Any(e => e.DistanceBucket == kv.Key)).OrderByDescending(kv => kv.Value).ToArray();
        return new BucketStats(
            ranked.FirstOrDefault().Key ?? string.Empty,
            ranked.LastOrDefault().Key ?? string.Empty,
            rates);
    }

    private static string ResolveRecommendation(
        int totalEvents,
        decimal rate24h,
        decimal convertedStressNet,
        decimal convertedWinRate,
        BucketStats bucketStats,
        string currentBucket)
    {
        if (totalEvents == 0)
            return "WatchOnlyExactEntry";

        if (rate24h < Sol30mNearMissConversionHistoryStudyCatalog.LowConversionRateThreshold)
            return "StopManualWatching";

        if (rate24h >= Sol30mNearMissConversionHistoryStudyCatalog.HighConversionRateThreshold
            && convertedStressNet > 0m)
            return "KeepWatchingAggressively";

        if (convertedStressNet > 0m && convertedWinRate >= 0.5m
            && rate24h < Sol30mNearMissConversionHistoryStudyCatalog.HighConversionRateThreshold)
            return "WatchOnlyExactEntry";

        if (convertedStressNet <= 0m || rate24h < Sol30mNearMissConversionHistoryStudyCatalog.HighConversionRateThreshold)
            return "DoNotChaseNearMiss";

        if (!string.IsNullOrEmpty(currentBucket)
            && bucketStats.RatesByBucket.TryGetValue(currentBucket, out var bucketRate)
            && bucketRate < Sol30mNearMissConversionHistoryStudyCatalog.LowConversionRateThreshold)
            return "DoNotChaseNearMiss";

        return "DoNotChaseNearMiss";
    }

    private static Sol30mNearMissConversionHistoryPlainEnglish BuildPlainEnglish(
        int total,
        int converted24h,
        int converted4,
        decimal rate24h,
        decimal rate4,
        IReadOnlyList<Sol30mNearMissConversionHistoryEventRow> conversions,
        decimal convertedStressNet,
        BucketStats bucketStats,
        string currentBucket,
        decimal? currentDistance,
        string recommendation)
    {
        var usuallyConverts = total == 0
            ? "No historical near-miss events matched the exact SOL 30m Short pattern in the study window."
            : rate24h >= Sol30mNearMissConversionHistoryStudyCatalog.HighConversionRateThreshold
                ? $"Yes, relatively often: {converted24h}/{total} ({rate24h:P1}) converted to exact entry within 24 hours."
                : rate24h >= Sol30mNearMissConversionHistoryStudyCatalog.LowConversionRateThreshold
                    ? $"Sometimes, but not reliably: {converted24h}/{total} ({rate24h:P1}) converted within 24 hours."
                    : $"No, they usually fade: only {converted24h}/{total} ({rate24h:P1}) converted within 24 hours.";

        string howFast;
        if (conversions.Count == 0)
            howFast = "No conversions observed in history for this pattern.";
        else
        {
            var medianCandles = MedianInt(conversions.Where(c => c.CandlesToConversion.HasValue).Select(c => c.CandlesToConversion!.Value).ToArray());
            var within4 = conversions.Count(c => c.ConvertedWithin4Candles);
            howFast = $"When conversion happens, median time is ~{medianCandles} candle(s). {converted4}/{total} ({rate4:P1}) converted within 4 candles; {within4}/{conversions.Count} of converted events were within 4 candles.";
        }

        var profitable = conversions.Count == 0
            ? "No converted entries to judge; stress-plus profitability is unknown."
            : convertedStressNet > 0m
                ? $"Yes, in aggregate: converted entries sum to stress-plus net {convertedStressNet:F2} quote over {conversions.Count} trade(s)."
                : $"No, in aggregate: converted entries sum to stress-plus net {convertedStressNet:F2} quote over {conversions.Count} trade(s).";

        string bucketQuality;
        if (string.IsNullOrEmpty(currentBucket))
            bucketQuality = "Current live near-miss distance was not available at study run time.";
        else
        {
            var bucketRate = bucketStats.RatesByBucket.GetValueOrDefault(currentBucket);
            var distText = currentDistance.HasValue ? $"{currentDistance.Value:F4}%" : "n/a";
            bucketQuality = bucketRate >= Sol30mNearMissConversionHistoryStudyCatalog.HighConversionRateThreshold
                ? $"The current {distText} bucket ({currentBucket}) is historically strong: {bucketRate:P1} converted within 24h."
                : bucketRate < Sol30mNearMissConversionHistoryStudyCatalog.LowConversionRateThreshold
                    ? $"The current {distText} bucket ({currentBucket}) is historically weak: only {bucketRate:P1} converted within 24h."
                    : $"The current {distText} bucket ({currentBucket}) is mixed: {bucketRate:P1} converted within 24h.";
        }

        var keepWatcher = recommendation switch
        {
            "KeepWatchingAggressively" => "Yes, keep the diagnostic watcher running, but only to catch an exact entry signal — not to trade the near-miss.",
            "WatchOnlyExactEntry" => "Yes, keep the watcher running for exact-entry detection only; do not chase the near-miss.",
            "StopManualWatching" => "No strong case for manual near-miss watching; near-misses usually fade. Watcher may still run diagnostically for exact entries.",
            _ => "Keep watcher in diagnostic mode only; do not chase near-miss based on history."
        };

        return new Sol30mNearMissConversionHistoryPlainEnglish
        {
            DoesNearMissUsuallyConvert = usuallyConverts,
            HowFastDoesItConvert = howFast,
            AreConvertedEntriesProfitableStressPlus = profitable,
            IsCurrentDistanceBucketGoodOrBad = bucketQuality,
            ShouldKeepWatcherRunning = keepWatcher,
            ShouldTradeNearMissBeforeExactEntry = "No. Never trade near-miss before an exact base entry signal; historical conversion is research only, not forward proof."
        };
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
            return 0m;
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? Math.Round((sorted[mid - 1] + sorted[mid]) / 2m, 6)
            : sorted[mid];
    }

    private static int MedianInt(int[] values)
    {
        if (values.Length == 0)
            return 0;
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    private static decimal ComputeProfitFactor(IReadOnlyList<decimal> nets)
    {
        var grossWin = nets.Where(n => n > 0m).Sum();
        var grossLoss = Math.Abs(nets.Where(n => n <= 0m).Sum());
        return grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : Math.Round(grossWin / grossLoss, 6);
    }
}
