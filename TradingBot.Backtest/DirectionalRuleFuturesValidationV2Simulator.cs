using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV2Simulator
{
    private const decimal DefaultQuantity = 1m;

    public static DirectionalRuleV2ScanResult ScanProfile(
        DirectionalRuleV2SimulationProfile profile,
        string windowLabel,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> sourceOneMinuteCandles,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var trades = new List<DirectionalRuleV2TradeRecord>();
        var skipped = new List<DirectionalRuleV2SkippedSignalRecord>();
        var signalCount = 0;
        if (intervalCandles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return new DirectionalRuleV2ScanResult(trades, skipped, signalCount);

        var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(profile.Interval);
        var nextAllowedIndex = 0;

        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signalCandle = intervalCandles[i];
            if (signalCandle.Close <= 0m)
                continue;

            var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                intervalCandles, i, btcContext, marketWideContext, signalCandle.OpenTimeUtc);
            if (!DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(features, profile.Rule))
                continue;

            signalCount++;
            if (ShouldSkipSignal(profile, i, nextAllowedIndex, out var skipReason))
            {
                skipped.Add(BuildSkipped(profile, windowLabel, signalCandle.OpenTimeUtc, skipReason));
                continue;
            }

            var entryCandle = intervalCandles[i + 1];
            var entryPrice = profile.EntryMode == DirectionalRuleEntryMode.NextOpen
                ? entryCandle.Open
                : entryCandle.Close;
            if (entryPrice <= 0m)
                continue;

            var simulation = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
                sourceOneMinuteCandles,
                entryCandle.OpenTimeUtc,
                entryPrice,
                profile.MaxHoldMinutes,
                profile.TargetPercent,
                profile.StopPercent,
                profile.Rule.Direction,
                DefaultQuantity);

            nextAllowedIndex = ResolveNextAllowedIndex(
                profile, intervalCandles, simulation.ExitTimeUtc, nextAllowedIndex);

            trades.Add(new DirectionalRuleV2TradeRecord
            {
                ProfileKey = profile.ProfileKey,
                RuleName = profile.Rule.RuleName,
                Direction = profile.Rule.Direction,
                Symbol = profile.Symbol,
                Interval = profile.Interval,
                WindowLabel = windowLabel,
                TimeUtc = simulation.EntryTimeUtc,
                EntryPrice = simulation.EntryPrice,
                ExitPrice = simulation.ExitPrice,
                ExitReason = simulation.ExitReason,
                TargetPercent = profile.TargetPercent,
                StopPercent = profile.StopPercent,
                MaxHoldMinutes = profile.MaxHoldMinutes,
                GrossPnlQuote = ComputeGrossPnl(simulation, profile.Rule.Direction),
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
                EntryMode = profile.EntryMode.ToString(),
                OverlapPolicy = profile.OverlapPolicy.ToString(),
                CooldownCandlesAfterExit = profile.CooldownCandlesAfterExit
            });
        }

        return new DirectionalRuleV2ScanResult(trades, skipped, signalCount);
    }

    public static IReadOnlyList<DirectionalRuleV2OverlapAnalysisRow> AnalyzeRuleOverlap(
        IReadOnlyList<DirectionalRuleDefinition> rules,
        string windowLabel,
        TradingSymbol symbol,
        string rule01Interval,
        string rule05Interval,
        IReadOnlyList<KlineCandle> rule01Candles,
        IReadOnlyList<KlineCandle> rule05Candles,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var rule01 = DirectionalRuleFuturesValidationV2Catalog.ResolveRule(rules, "Rule01");
        var rule05 = DirectionalRuleFuturesValidationV2Catalog.ResolveRule(rules, "Rule05");
        if (rule01 is null || rule05 is null)
            return [];

        var rule01Signals = CollectSignalTimes(
            rule01, rule01Interval, rule01Candles, btcContext, marketWideContext, cancellationToken);
        var rule05Signals = CollectSignalTimes(
            rule05, rule05Interval, rule05Candles, btcContext, marketWideContext, cancellationToken);
        var coFire = 0;
        foreach (var t01 in rule01Signals)
        {
            if (rule05Signals.Any(t05 => Math.Abs((t05 - t01).TotalMinutes) <= 30))
                coFire++;
        }

        return
        [
            new DirectionalRuleV2OverlapAnalysisRow
            {
                Symbol = symbol,
                Rule01Interval = rule01Interval,
                Rule05Interval = rule05Interval,
                WindowLabel = windowLabel,
                Rule01SignalCount = rule01Signals.Count,
                Rule05SignalCount = rule05Signals.Count,
                CoFireWithin30mCount = coFire,
                CoFireRateVsRule01 = rule01Signals.Count == 0 ? 0m : Math.Round((decimal)coFire / rule01Signals.Count, 6),
                CoFireRateVsRule05 = rule05Signals.Count == 0 ? 0m : Math.Round((decimal)coFire / rule05Signals.Count, 6),
                RulePriorityMode = "SignalOverlapDiagnostic",
                Notes = "Co-fire counts raw rule signals regardless of overlap policy."
            }
        ];
    }

    public static DirectionalRuleV2ScanResult ScanPriorityEth(
        DirectionalRuleV2SimulationProfile profile,
        DirectionalRuleDefinition rule01,
        DirectionalRuleDefinition rule05,
        string windowLabel,
        IReadOnlyList<KlineCandle> rule01Candles,
        IReadOnlyList<KlineCandle> rule05Candles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var trades = new List<DirectionalRuleV2TradeRecord>();
        var skipped = new List<DirectionalRuleV2SkippedSignalRecord>();
        var events = new List<(DateTime TimeUtc, string RuleKey, DirectionalRuleDefinition Rule, string Interval, int CandleIndex, IReadOnlyList<KlineCandle> Candles)>();

        CollectEvents(rule01, "Rule01", "30m", rule01Candles, events, btcContext, marketWideContext, cancellationToken);
        CollectEvents(rule05, "Rule05", "15m", rule05Candles, events, btcContext, marketWideContext, cancellationToken);
        events = events.OrderBy(e => e.TimeUtc).ToList();

        var nextAllowedUtc = DateTime.MinValue;
        var signalCount = events.Count;
        var rule01Wins = 0;
        var rule05Wins = 0;

        for (var eventIndex = 0; eventIndex < events.Count; eventIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evt = events[eventIndex];
            if (evt.TimeUtc < nextAllowedUtc)
            {
                skipped.Add(BuildSkipped(profile with { Rule = evt.Rule }, windowLabel, evt.TimeUtc,
                    DirectionalRuleV2SkipReason.SkippedOverlapOpenTrade));
                continue;
            }

            var competing = events
                .Where((e, idx) => idx > eventIndex
                                  && Math.Abs((e.TimeUtc - evt.TimeUtc).TotalMinutes) <= 30
                                  && e.RuleKey != evt.RuleKey)
                .ToArray();
            if (competing.Length > 0 && profile.RulePriority != DirectionalRuleV2RulePriority.None)
            {
                var chosen = ResolvePriority(evt, competing, profile.RulePriority);
                if (chosen.RuleKey != evt.RuleKey)
                {
                    skipped.Add(BuildSkipped(profile with { Rule = evt.Rule }, windowLabel, evt.TimeUtc,
                        DirectionalRuleV2SkipReason.SkippedPriorityOtherRule));
                    continue;
                }

                if (evt.RuleKey == "Rule01")
                    rule01Wins++;
                else
                    rule05Wins++;
            }

            var entryIndex = evt.CandleIndex + 1;
            if (entryIndex >= evt.Candles.Count)
                continue;
            var entryCandle = evt.Candles[entryIndex];
            var entryPrice = profile.EntryMode == DirectionalRuleEntryMode.NextOpen
                ? entryCandle.Open
                : entryCandle.Close;
            if (entryPrice <= 0m)
                continue;

            var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                evt.Candles, evt.CandleIndex, btcContext, marketWideContext, evt.TimeUtc);
            var simulation = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
                oneMinuteCandles,
                entryCandle.OpenTimeUtc,
                entryPrice,
                profile.MaxHoldMinutes,
                profile.TargetPercent,
                profile.StopPercent,
                evt.Rule.Direction,
                DefaultQuantity);

            nextAllowedUtc = simulation.ExitTimeUtc;

            trades.Add(new DirectionalRuleV2TradeRecord
            {
                ProfileKey = profile.ProfileKey,
                RuleName = evt.Rule.RuleName,
                Direction = evt.Rule.Direction,
                Symbol = profile.Symbol,
                Interval = evt.Interval,
                WindowLabel = windowLabel,
                TimeUtc = simulation.EntryTimeUtc,
                EntryPrice = simulation.EntryPrice,
                ExitPrice = simulation.ExitPrice,
                ExitReason = simulation.ExitReason,
                TargetPercent = profile.TargetPercent,
                StopPercent = profile.StopPercent,
                MaxHoldMinutes = profile.MaxHoldMinutes,
                GrossPnlQuote = ComputeGrossPnl(simulation, evt.Rule.Direction),
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
                EntryMode = profile.EntryMode.ToString(),
                OverlapPolicy = DirectionalRuleV2OverlapPolicy.OneOpenTradePerSymbol.ToString(),
                CooldownCandlesAfterExit = 0
            });
        }

        return new DirectionalRuleV2ScanResult(trades, skipped, signalCount)
        {
            PriorityRule01Wins = rule01Wins,
            PriorityRule05Wins = rule05Wins
        };
    }

    public static IReadOnlyList<DirectionalRuleV2TradeRecord> ApplyCostScenarios(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades)
    {
        if (baseTrades.Count == 0)
            return [];

        var scenarios = DirectionalRuleFuturesValidationV2CostModel.BuildValidationScenarios();
        var expanded = new List<DirectionalRuleV2TradeRecord>(baseTrades.Count * scenarios.Count);
        foreach (var trade in baseTrades)
        {
            var simulation = new DirectionalTradeSimulationResult(
                trade.TimeUtc,
                trade.EntryPrice,
                trade.TimeUtc.AddMinutes((double)trade.DurationMinutes),
                trade.ExitPrice,
                trade.ExitReason,
                trade.MfePercent,
                trade.MaePercent,
                trade.DurationMinutes);
            foreach (var scenario in scenarios)
            {
                var costs = DirectionalRuleFuturesValidationV2CostModel.ComputeCostBreakdown(
                    simulation, trade.Direction, scenario);
                expanded.Add(trade with
                {
                    CostScenarioLabel = scenario.Label,
                    NetPnlQuote = costs.NetPnlQuote,
                    FundingEstimateQuote = costs.FundingEstimateQuote,
                    SlippageEstimateQuote = costs.SlippageEstimateQuote
                });
            }
        }

        return expanded;
    }

    private static (DateTime TimeUtc, string RuleKey, DirectionalRuleDefinition Rule, string Interval, int CandleIndex, IReadOnlyList<KlineCandle> Candles)
        ResolvePriority(
            (DateTime TimeUtc, string RuleKey, DirectionalRuleDefinition Rule, string Interval, int CandleIndex, IReadOnlyList<KlineCandle> Candles) current,
            IReadOnlyList<(DateTime TimeUtc, string RuleKey, DirectionalRuleDefinition Rule, string Interval, int CandleIndex, IReadOnlyList<KlineCandle> Candles)> competing,
            DirectionalRuleV2RulePriority priority)
        => priority switch
        {
            DirectionalRuleV2RulePriority.Rule01First => current.RuleKey == "Rule01" ? current : competing[0],
            DirectionalRuleV2RulePriority.Rule05First => current.RuleKey == "Rule05" ? current : competing[0],
            DirectionalRuleV2RulePriority.StrongerEdgeFirst => current.RuleKey == "Rule01" ? current : competing[0],
            _ => current
        };

    private static void CollectEvents(
        DirectionalRuleDefinition rule,
        string ruleKey,
        string interval,
        IReadOnlyList<KlineCandle> candles,
        List<(DateTime, string, DirectionalRuleDefinition, string, int, IReadOnlyList<KlineCandle>)> destination,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(interval);
        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < candles.Count - 1; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candle = candles[i];
            if (candle.Close <= 0m)
                continue;
            var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                candles, i, btcContext, marketWideContext, candle.OpenTimeUtc);
            if (!DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(features, rule))
                continue;
            destination.Add((candle.OpenTimeUtc, ruleKey, rule, interval, i, candles));
        }
    }

    private static bool ShouldSkipSignal(
        DirectionalRuleV2SimulationProfile profile,
        int candleIndex,
        int nextAllowedIndex,
        out DirectionalRuleV2SkipReason reason)
    {
        if (profile.OverlapPolicy == DirectionalRuleV2OverlapPolicy.AllowOverlap)
        {
            if (profile.CooldownCandlesAfterExit > 0 && candleIndex < nextAllowedIndex)
            {
                reason = DirectionalRuleV2SkipReason.SkippedCooldown;
                return true;
            }

            reason = default;
            return false;
        }

        if (candleIndex < nextAllowedIndex)
        {
            reason = profile.CooldownCandlesAfterExit > 0
                ? DirectionalRuleV2SkipReason.SkippedCooldown
                : DirectionalRuleV2SkipReason.SkippedOverlapOpenTrade;
            return true;
        }

        reason = default;
        return false;
    }

    private static int ResolveNextAllowedIndex(
        DirectionalRuleV2SimulationProfile profile,
        IReadOnlyList<KlineCandle> intervalCandles,
        DateTime exitTimeUtc,
        int currentNextAllowedIndex)
    {
        if (profile.OverlapPolicy == DirectionalRuleV2OverlapPolicy.AllowOverlap
            && profile.CooldownCandlesAfterExit <= 0)
        {
            return 0;
        }

        var exitIndex = ResolveNextAllowedIntervalIndex(intervalCandles, exitTimeUtc);
        return exitIndex + 1 + profile.CooldownCandlesAfterExit;
    }

    private static List<DateTime> CollectSignalTimes(
        DirectionalRuleDefinition rule,
        string interval,
        IReadOnlyList<KlineCandle> candles,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var times = new List<DateTime>();
        var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(interval);
        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < candles.Count - 1; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candle = candles[i];
            if (candle.Close <= 0m)
                continue;
            var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                candles, i, btcContext, marketWideContext, candle.OpenTimeUtc);
            if (DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(features, rule))
                times.Add(candle.OpenTimeUtc);
        }

        return times;
    }

    private static DirectionalRuleV2SkippedSignalRecord BuildSkipped(
        DirectionalRuleV2SimulationProfile profile,
        string windowLabel,
        DateTime timeUtc,
        DirectionalRuleV2SkipReason reason)
        => new()
        {
            ProfileKey = profile.ProfileKey,
            RuleName = profile.Rule.RuleName,
            Symbol = profile.Symbol,
            Interval = profile.Interval,
            WindowLabel = windowLabel,
            TimeUtc = timeUtc,
            SkipReason = reason.ToString(),
            OverlapPolicy = profile.OverlapPolicy.ToString(),
            CooldownCandlesAfterExit = profile.CooldownCandlesAfterExit,
            EntryMode = profile.EntryMode.ToString()
        };

    private static decimal ComputeGrossPnl(DirectionalTradeSimulationResult simulation, LongShortDirection direction)
        => direction == LongShortDirection.Long
            ? Math.Round((simulation.ExitPrice - simulation.EntryPrice) * DefaultQuantity, 8)
            : Math.Round((simulation.EntryPrice - simulation.ExitPrice) * DefaultQuantity, 8);

    private static int ResolveNextAllowedIntervalIndex(IReadOnlyList<KlineCandle> intervalCandles, DateTime exitTimeUtc)
    {
        // Sorted ascending: binary search for the leftmost candle with OpenTimeUtc > exitTimeUtc.
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

        return lo;
    }
}

public sealed record DirectionalRuleV2ScanResult(
    IReadOnlyList<DirectionalRuleV2TradeRecord> Trades,
    IReadOnlyList<DirectionalRuleV2SkippedSignalRecord> Skipped,
    int SignalCount)
{
    public int PriorityRule01Wins { get; init; }
    public int PriorityRule05Wins { get; init; }
}
