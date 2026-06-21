using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Base trade generation for the multi-symbol rule families. One scan per (symbol, interval)
/// evaluates every family/direction combo on the same completed signal candle, using the flow
/// snapshot as-of that candle's close. Entry is at next candle close; execution uses 1m candles
/// (same SimulateDirectionalTrade semantics as prior branches). One open trade per combo with a
/// fixed cooldown — mirroring the OneOpenTradePerRuleSymbol policy used before.
/// </summary>
public static class NoPaidDataShortWindowMultiSymbolResearchV2Simulator
{
    private const decimal Quantity = 1m;

    public static IReadOnlyList<MultiSymbolComboScanResult> ScanSymbolInterval(
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
        var geometry = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.GeometryFor(interval);
        var combos = NoPaidDataShortWindowMultiSymbolResearchV2Catalog.FamilyCombos(symbol)
            .Select(c => new ComboState(new MultiSymbolComboKey(symbol, interval, c.Direction, c.Family)))
            .ToArray();

        if (intervalCandles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return combos.Select(c => new MultiSymbolComboScanResult(c.Key, 0, [])).ToArray();

        var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(interval);

        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signalCandle = intervalCandles[i];
            if (signalCandle.Close <= 0m)
                continue;

            var entryCandle = intervalCandles[i + 1];
            // Signal decisions only inside the study window (where flow data exists for all symbols).
            if (entryCandle.OpenTimeUtc < studyStartUtc || entryCandle.OpenTimeUtc >= studyEndUtc)
                continue;

            MarketRegimeForwardEdgeScanner.RegimeCandleFeatures? features = null;
            ShortWindowFlowSnapshot? snapshot = null;

            foreach (var combo in combos)
            {
                features ??= MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                    intervalCandles, i, btcContext, marketWideContext, signalCandle.OpenTimeUtc);
                snapshot ??= flowIndex.Snapshot(entryCandle.OpenTimeUtc); // as-of signal candle close

                if (!NoPaidDataShortWindowMultiSymbolResearchV2Catalog.MatchesFamily(
                        combo.Key.Family, combo.Key.Direction, features, snapshot))
                    continue;

                combo.SignalCount++;
                if (i < combo.NextAllowedIndex)
                    continue; // open trade or cooldown for this combo

                var entryPrice = entryCandle.Close;
                if (entryPrice <= 0m)
                    continue;

                var simulation = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
                    oneMinuteCandles,
                    entryCandle.OpenTimeUtc,
                    entryPrice,
                    geometry.MaxHoldMinutes,
                    geometry.TargetPercent,
                    geometry.StopPercent,
                    combo.Key.Direction,
                    Quantity);

                combo.NextAllowedIndex = ResolveNextAllowedIndex(
                    intervalCandles, simulation.ExitTimeUtc, geometry.CooldownCandles);

                combo.Trades.Add(new DirectionalRuleV2TradeRecord
                {
                    ProfileKey = combo.Key.ToString(),
                    RuleName = combo.Key.Family.ToString(),
                    Direction = combo.Key.Direction,
                    Symbol = symbol,
                    Interval = interval,
                    WindowLabel = "multisymbol-v2-study",
                    TimeUtc = simulation.EntryTimeUtc,
                    EntryPrice = simulation.EntryPrice,
                    ExitPrice = simulation.ExitPrice,
                    ExitReason = simulation.ExitReason,
                    TargetPercent = geometry.TargetPercent,
                    StopPercent = geometry.StopPercent,
                    MaxHoldMinutes = geometry.MaxHoldMinutes,
                    GrossPnlQuote = ComputeGrossPnl(simulation, combo.Key.Direction),
                    BtcReturn30mPercent = features.BtcReturn30mPercent,
                    VolatilityRegime = features.VolatilityRegime,
                    RangeWidthPercent = features.RangeWidthPercent,
                    DistanceFromRecentHighPercent = features.DistanceFromRecentHighPercent,
                    DistanceFromRecentLowPercent = features.DistanceFromRecentLowPercent,
                    AtrPercent = features.AtrPercent,
                    TrendSlopePercent = features.TrendSlopePercent,
                    MfePercent = simulation.MfePercent,
                    MaePercent = simulation.MaePercent,
                    DurationMinutes = simulation.DurationMinutes,
                    EntryMode = "NextClose",
                    OverlapPolicy = "OneOpenTradePerRuleSymbol",
                    CooldownCandlesAfterExit = geometry.CooldownCandles
                });
            }
        }

        return combos.Select(c => new MultiSymbolComboScanResult(c.Key, c.SignalCount, c.Trades)).ToArray();
    }

    private sealed class ComboState(MultiSymbolComboKey key)
    {
        public MultiSymbolComboKey Key { get; } = key;
        public int SignalCount { get; set; }
        public int NextAllowedIndex { get; set; }
        public List<DirectionalRuleV2TradeRecord> Trades { get; } = [];
    }

    private static decimal ComputeGrossPnl(DirectionalTradeSimulationResult simulation, LongShortDirection direction)
        => direction == LongShortDirection.Long
            ? Math.Round((simulation.ExitPrice - simulation.EntryPrice) * Quantity, 8)
            : Math.Round((simulation.EntryPrice - simulation.ExitPrice) * Quantity, 8);

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
