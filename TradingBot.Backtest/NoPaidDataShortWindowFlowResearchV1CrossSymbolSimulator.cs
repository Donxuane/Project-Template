using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Base trade generation for the cross-symbol V1-style research. Signal detection (Rule01-style
/// near-extreme + elevated-volatility family) is geometry-independent and runs once per
/// (symbol, interval, direction); each geometry then replays the same signals with its own
/// overlap/cooldown state. Entry at next candle close; execution on 1m candles.
/// </summary>
public static class NoPaidDataShortWindowFlowResearchV1CrossSymbolSimulator
{
    private const decimal Quantity = 1m;

    public sealed record GeometryScan(
        CrossSymbolComboKey Key,
        int SignalCount,
        IReadOnlyList<DirectionalRuleV2TradeRecord> BaseTrades,
        IReadOnlyList<DateTime> RawSignalEntryTimesUtc);

    public static IReadOnlyList<GeometryScan> ScanSymbolInterval(
        TradingSymbol symbol,
        string interval,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        ShortWindowFlowFeatureIndex flowIndex,
        BtcContextIndex btcContext,
        MarketWideContextIndex marketWideContext,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        CancellationToken cancellationToken)
    {
        var results = new List<GeometryScan>();
        var cooldown = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.CooldownFor(interval);
        var directions = new[] { LongShortDirection.Long, LongShortDirection.Short };

        // Pass 1 — geometry-independent signal indexes per direction.
        var signalIndexes = directions.ToDictionary(d => d, _ => new List<int>());
        if (intervalCandles.Count > MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
        {
            var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(interval);
            for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i += stride)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var signalCandle = intervalCandles[i];
                if (signalCandle.Close <= 0m)
                    continue;
                var entryCandle = intervalCandles[i + 1];
                if (entryCandle.OpenTimeUtc < studyStartUtc || entryCandle.OpenTimeUtc >= studyEndUtc)
                    continue;

                MarketRegimeForwardEdgeScanner.RegimeCandleFeatures? features = null;
                ShortWindowFlowSnapshot? snapshot = null;
                foreach (var direction in directions)
                {
                    features ??= MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                        intervalCandles, i, btcContext, marketWideContext, signalCandle.OpenTimeUtc);
                    snapshot ??= flowIndex.Snapshot(entryCandle.OpenTimeUtc);
                    var family = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.BaseFamilyFor(direction);
                    if (NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MatchesFamily(family, direction, features, snapshot))
                        signalIndexes[direction].Add(i);
                }
            }
        }

        // Pass 2 — replay signals per geometry with independent overlap/cooldown state.
        foreach (var direction in directions)
        foreach (var (target, stop) in NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.GeometryGrid)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new CrossSymbolComboKey(
                symbol, interval, direction, target, stop,
                NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes);
            var trades = new List<DirectionalRuleV2TradeRecord>();
            var nextAllowedIndex = 0;

            foreach (var i in signalIndexes[direction])
            {
                if (i < nextAllowedIndex)
                    continue;
                var entryCandle = intervalCandles[i + 1];
                var entryPrice = entryCandle.Close;
                if (entryPrice <= 0m)
                    continue;

                var simulation = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
                    oneMinuteCandles,
                    entryCandle.OpenTimeUtc,
                    entryPrice,
                    NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes,
                    target,
                    stop,
                    direction,
                    Quantity);

                nextAllowedIndex = ResolveNextAllowedIndex(intervalCandles, simulation.ExitTimeUtc, cooldown);

                trades.Add(new DirectionalRuleV2TradeRecord
                {
                    ProfileKey = key.ToString(),
                    RuleName = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.BaseFamilyFor(direction).ToString(),
                    Direction = direction,
                    Symbol = symbol,
                    Interval = interval,
                    WindowLabel = "cross-symbol-v1-study",
                    TimeUtc = simulation.EntryTimeUtc,
                    EntryPrice = simulation.EntryPrice,
                    ExitPrice = simulation.ExitPrice,
                    ExitReason = simulation.ExitReason,
                    TargetPercent = target,
                    StopPercent = stop,
                    MaxHoldMinutes = NoPaidDataShortWindowFlowResearchV1CrossSymbolCatalog.HoldMinutes,
                    GrossPnlQuote = direction == LongShortDirection.Long
                        ? Math.Round((simulation.ExitPrice - simulation.EntryPrice) * Quantity, 8)
                        : Math.Round((simulation.EntryPrice - simulation.ExitPrice) * Quantity, 8),
                    MfePercent = simulation.MfePercent,
                    MaePercent = simulation.MaePercent,
                    DurationMinutes = simulation.DurationMinutes,
                    EntryMode = "NextClose",
                    OverlapPolicy = "OneOpenTradePerRuleSymbol",
                    CooldownCandlesAfterExit = cooldown
                });
            }

            var rawSignalTimes = signalIndexes[direction]
                .Select(i => intervalCandles[i + 1].OpenTimeUtc)
                .Where(t => t >= studyStartUtc && t < studyEndUtc)
                .ToArray();
            results.Add(new GeometryScan(key, signalIndexes[direction].Count, trades, rawSignalTimes));
        }

        return results;
    }

    private static int ResolveNextAllowedIndex(
        IReadOnlyList<KlineCandle> intervalCandles, DateTime exitTimeUtc, int cooldownCandles)
    {
        var lo = 0;
        var hi = intervalCandles.Count;
        while (lo < hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (intervalCandles[mid].OpenTimeUtc > exitTimeUtc)
                hi = mid;
            else
                lo = mid + 1;
        }

        return lo + 1 + cooldownCandles;
    }
}
