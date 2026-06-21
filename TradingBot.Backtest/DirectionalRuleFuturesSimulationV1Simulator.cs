using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record DirectionalTradeSimulationResult(
    DateTime EntryTimeUtc,
    decimal EntryPrice,
    DateTime ExitTimeUtc,
    decimal ExitPrice,
    string ExitReason,
    decimal? MfePercent,
    decimal? MaePercent,
    decimal DurationMinutes);

public sealed record DirectionalRuleCostBreakdown(
    decimal FeeEstimateQuote,
    decimal SpreadEstimateQuote,
    decimal SlippageEstimateQuote,
    decimal FundingEstimateQuote,
    decimal NetPnlQuote);

public static class DirectionalRuleFuturesSimulationV1Simulator
{
    public static readonly (decimal Target, decimal Stop)[] TargetStopMatrix =
        LongShortFuturesFeasibilityStudyV1Scanner.StudyMatrixPairs;

    public static readonly int[] MaxHoldMinutesOptions = [60, 240, 480, 720];

    public static readonly DirectionalRuleEntryMode[] EntryModes =
        [DirectionalRuleEntryMode.NextOpen, DirectionalRuleEntryMode.NextClose];

    public static readonly string[] SimulationCostScenarioLabels =
        ["futures-moderate", "futures-low", "futures-stress"];

    private const decimal DefaultQuantity = 1m;

    public static IReadOnlyList<DirectionalRuleFuturesTradeRecord> ScanSymbolInterval(
        TradingSymbol symbol,
        string interval,
        string windowLabel,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> sourceOneMinuteCandles,
        IReadOnlyList<DirectionalRuleDefinition> rules,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var trades = new List<DirectionalRuleFuturesTradeRecord>();
        if (intervalCandles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return trades;

        var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(interval);
        var nextAllowedIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signalCandle = intervalCandles[i];
            if (signalCandle.Close <= 0m)
                continue;

            var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                intervalCandles, i, btcContext, marketWideContext, signalCandle.OpenTimeUtc);

            foreach (var rule in rules)
            {
                if (!DirectionalRuleFuturesSimulationV1RuleCatalog.MatchesRule(features, rule))
                    continue;

                foreach (var entryMode in EntryModes)
                {
                    if (!HasOpenVariantSlot(rule.RuleName, entryMode, i, nextAllowedIndex))
                        continue;

                    var entryCandle = intervalCandles[i + 1];
                    var entryPrice = entryMode == DirectionalRuleEntryMode.NextOpen
                        ? entryCandle.Open
                        : entryCandle.Close;
                    if (entryPrice <= 0m)
                        continue;

                    var entryTimeUtc = entryCandle.OpenTimeUtc;
                    var batchResults = SimulateDirectionalTradeBatch(
                        sourceOneMinuteCandles,
                        entryTimeUtc,
                        entryPrice,
                        rule.Direction,
                        DefaultQuantity);

                    foreach (var ((targetPercent, stopPercent, maxHoldMinutes), simulation) in batchResults)
                    {
                        var positionKey = BuildPositionKey(rule.RuleName, entryMode, targetPercent, stopPercent, maxHoldMinutes);
                        if (nextAllowedIndex.TryGetValue(positionKey, out var allowedFrom) && i < allowedFrom)
                            continue;

                        nextAllowedIndex[positionKey] = ResolveNextAllowedIntervalIndex(intervalCandles, simulation.ExitTimeUtc) + 1;
                        var grossPnl = ComputeGrossPnl(simulation.EntryPrice, simulation.ExitPrice, rule.Direction, DefaultQuantity);
                        trades.Add(new DirectionalRuleFuturesTradeRecord
                        {
                            RuleName = rule.RuleName,
                            Direction = rule.Direction,
                            Symbol = symbol,
                            Interval = interval,
                            WindowLabel = windowLabel,
                            TimeUtc = simulation.EntryTimeUtc,
                            EntryPrice = simulation.EntryPrice,
                            ExitPrice = simulation.ExitPrice,
                            ExitReason = simulation.ExitReason,
                            TargetPercent = targetPercent,
                            StopPercent = stopPercent,
                            MaxHoldMinutes = maxHoldMinutes,
                            GrossPnlQuote = grossPnl,
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
                            EntryMode = entryMode.ToString()
                        });
                    }
                }
            }
        }

        return trades;
    }

    public static IReadOnlyList<DirectionalRuleFuturesTradeRecord> ExpandCostScenarios(
        IReadOnlyList<DirectionalRuleFuturesTradeRecord> baseTrades)
    {
        var scenarios = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios()
            .Where(s => SimulationCostScenarioLabels.Contains(s.Label, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (baseTrades.Count == 0)
            return [];

        var expanded = new List<DirectionalRuleFuturesTradeRecord>(baseTrades.Count * scenarios.Length);
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
                var costs = ComputeCostBreakdown(simulation, trade.Direction, scenario, DefaultQuantity);
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

    public static Dictionary<(decimal TargetPercent, decimal StopPercent, int MaxHoldMinutes), DirectionalTradeSimulationResult>
        SimulateDirectionalTradeBatch(
            IReadOnlyList<KlineCandle> oneMinuteCandles,
            DateTime entryTimeUtc,
            decimal entryPrice,
            LongShortDirection direction,
            decimal quantity = DefaultQuantity)
    {
        var variants = new List<VariantSimulationState>();
        foreach (var (targetPercent, stopPercent) in TargetStopMatrix)
        {
            foreach (var maxHoldMinutes in MaxHoldMinutesOptions)
            {
                variants.Add(new VariantSimulationState(targetPercent, stopPercent, maxHoldMinutes));
            }
        }

        var entryIdx = FindEntryIndex(oneMinuteCandles, entryTimeUtc);
        if (entryIdx < 0 || entryPrice <= 0m || quantity <= 0m)
        {
            var invalid = new DirectionalTradeSimulationResult(
                entryTimeUtc, entryPrice, entryTimeUtc, entryPrice, "InvalidEntry", null, null, 0m);
            return variants.ToDictionary(v => v.Key, _ => invalid);
        }

        var globalDeadline = entryTimeUtc.AddMinutes(MaxHoldMinutesOptions.Max());
        for (var i = entryIdx; i < oneMinuteCandles.Count; i++)
        {
            var candle = oneMinuteCandles[i];
            if (candle.OpenTimeUtc > globalDeadline)
                break;

            foreach (var variant in variants)
            {
                if (variant.Resolved)
                    continue;

                var variantDeadline = entryTimeUtc.AddMinutes(variant.MaxHoldMinutes);
                if (candle.OpenTimeUtc > variantDeadline)
                {
                    if (variant.HasProcessedCandle)
                        variant.ResolveTimeStop(variant.LastCandleTimeUtc, variant.LastClose);
                    continue;
                }

                variant.UpdateExcursion(direction, entryPrice, candle);
                variant.MarkProcessed(candle.OpenTimeUtc, candle.Close);

                if (TryResolveTargetStop(direction, entryPrice, candle, variant))
                    continue;
            }

            if (variants.All(v => v.Resolved))
                break;
        }

        foreach (var variant in variants.Where(v => !v.Resolved))
        {
            if (variant.HasProcessedCandle)
                variant.ResolveEndOfData();
            else
                variant.Resolve("EndOfData", entryPrice, entryTimeUtc);
        }

        return variants.ToDictionary(
            v => v.Key,
            v => v.ToResult(entryTimeUtc, entryPrice));
    }

    public static DirectionalTradeSimulationResult SimulateDirectionalTrade(
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        int maxHoldMinutes,
        decimal targetPercent,
        decimal stopPercent,
        LongShortDirection direction,
        decimal quantity = DefaultQuantity)
    {
        var entryIdx = FindEntryIndex(oneMinuteCandles, entryTimeUtc);
        if (entryIdx < 0 || entryPrice <= 0m || quantity <= 0m)
        {
            return new DirectionalTradeSimulationResult(
                entryTimeUtc, entryPrice, entryTimeUtc, entryPrice, "InvalidEntry",
                null, null, 0m);
        }

        var deadline = entryTimeUtc.AddMinutes(maxHoldMinutes);
        decimal? mfe = 0m;
        decimal? mae = 0m;
        DateTime exitTimeUtc = entryTimeUtc;
        decimal exitPrice = entryPrice;
        var exitReason = "EndOfData";

        for (var i = entryIdx; i < oneMinuteCandles.Count; i++)
        {
            var candle = oneMinuteCandles[i];
            if (candle.OpenTimeUtc > deadline)
                break;

            UpdateExcursion(direction, entryPrice, candle, ref mfe, ref mae);

            if (direction == LongShortDirection.Long)
            {
                var targetPrice = entryPrice * (1m + targetPercent / 100m);
                var stopPrice = entryPrice * (1m - stopPercent / 100m);
                var hitTarget = candle.High >= targetPrice;
                var hitStop = candle.Low <= stopPrice;
                if (hitTarget && hitStop)
                {
                    exitTimeUtc = candle.OpenTimeUtc;
                    if (candle.Open <= entryPrice)
                    {
                        exitPrice = stopPrice;
                        exitReason = "StopLoss";
                    }
                    else
                    {
                        exitPrice = targetPrice;
                        exitReason = "ProfitTarget";
                    }

                    return BuildResult(entryTimeUtc, entryPrice, exitTimeUtc, exitPrice, exitReason, mfe, mae);
                }

                if (hitStop)
                {
                    exitTimeUtc = candle.OpenTimeUtc;
                    exitPrice = stopPrice;
                    exitReason = "StopLoss";
                    return BuildResult(entryTimeUtc, entryPrice, exitTimeUtc, exitPrice, exitReason, mfe, mae);
                }

                if (hitTarget)
                {
                    exitTimeUtc = candle.OpenTimeUtc;
                    exitPrice = targetPrice;
                    exitReason = "ProfitTarget";
                    return BuildResult(entryTimeUtc, entryPrice, exitTimeUtc, exitPrice, exitReason, mfe, mae);
                }
            }
            else
            {
                var targetPrice = entryPrice * (1m - targetPercent / 100m);
                var stopPrice = entryPrice * (1m + stopPercent / 100m);
                var hitTarget = candle.Low <= targetPrice;
                var hitStop = candle.High >= stopPrice;
                if (hitTarget && hitStop)
                {
                    exitTimeUtc = candle.OpenTimeUtc;
                    if (candle.Open >= entryPrice)
                    {
                        exitPrice = stopPrice;
                        exitReason = "StopLoss";
                    }
                    else
                    {
                        exitPrice = targetPrice;
                        exitReason = "ProfitTarget";
                    }

                    return BuildResult(entryTimeUtc, entryPrice, exitTimeUtc, exitPrice, exitReason, mfe, mae);
                }

                if (hitStop)
                {
                    exitTimeUtc = candle.OpenTimeUtc;
                    exitPrice = stopPrice;
                    exitReason = "StopLoss";
                    return BuildResult(entryTimeUtc, entryPrice, exitTimeUtc, exitPrice, exitReason, mfe, mae);
                }

                if (hitTarget)
                {
                    exitTimeUtc = candle.OpenTimeUtc;
                    exitPrice = targetPrice;
                    exitReason = "ProfitTarget";
                    return BuildResult(entryTimeUtc, entryPrice, exitTimeUtc, exitPrice, exitReason, mfe, mae);
                }
            }

            exitTimeUtc = candle.OpenTimeUtc;
            exitPrice = candle.Close;
        }

        if (exitTimeUtc > entryTimeUtc && exitTimeUtc <= deadline)
            exitReason = "TimeStop";

        return BuildResult(entryTimeUtc, entryPrice, exitTimeUtc, exitPrice, exitReason, mfe, mae);
    }

    public static DirectionalRuleCostBreakdown ComputeCostBreakdown(
        DirectionalTradeSimulationResult simulation,
        LongShortDirection direction,
        FeasibilityCostScenario scenario,
        decimal quantity)
    {
        var gross = ComputeGrossPnl(simulation.EntryPrice, simulation.ExitPrice, direction, quantity);
        var entryNotional = simulation.EntryPrice * quantity;
        var exitNotional = simulation.ExitPrice * quantity;
        var feeEstimate = (entryNotional + exitNotional) * (scenario.FeeRatePercent / 100m);
        var spreadEstimate = (entryNotional + exitNotional) * (scenario.SpreadPercent / 100m) / 2m;
        var slippageEstimate = (entryNotional + exitNotional) * (scenario.SlippagePercent / 100m);
        var fundingEstimate = entryNotional * (scenario.FundingRatePercentPerHour / 100m)
            * (simulation.DurationMinutes / 60m);
        var net = gross - feeEstimate - spreadEstimate - slippageEstimate - fundingEstimate;
        return new DirectionalRuleCostBreakdown(
            Math.Round(feeEstimate, 8),
            Math.Round(spreadEstimate, 8),
            Math.Round(slippageEstimate, 8),
            Math.Round(fundingEstimate, 8),
            Math.Round(net, 8));
    }

    private static decimal ComputeGrossPnl(
        decimal entryPrice,
        decimal exitPrice,
        LongShortDirection direction,
        decimal quantity)
        => direction == LongShortDirection.Long
            ? Math.Round((exitPrice - entryPrice) * quantity, 8)
            : Math.Round((entryPrice - exitPrice) * quantity, 8);

    private static DirectionalTradeSimulationResult BuildResult(
        DateTime entryTimeUtc,
        decimal entryPrice,
        DateTime exitTimeUtc,
        decimal exitPrice,
        string exitReason,
        decimal? mfe,
        decimal? mae)
    {
        var duration = Math.Round((decimal)(exitTimeUtc - entryTimeUtc).TotalMinutes, 2);
        if (duration < 0m)
            duration = 0m;

        return new DirectionalTradeSimulationResult(
            entryTimeUtc,
            entryPrice,
            exitTimeUtc,
            exitPrice,
            exitReason,
            mfe.HasValue ? Math.Round(mfe.Value, 6) : null,
            mae.HasValue ? Math.Round(mae.Value, 6) : null,
            duration);
    }

    private static void UpdateExcursion(
        LongShortDirection direction,
        decimal entryPrice,
        KlineCandle candle,
        ref decimal? mfe,
        ref decimal? mae)
    {
        if (direction == LongShortDirection.Long)
        {
            var favorable = (candle.High - entryPrice) / entryPrice * 100m;
            var adverse = (entryPrice - candle.Low) / entryPrice * 100m;
            mfe = Math.Max(mfe ?? 0m, favorable);
            mae = Math.Max(mae ?? 0m, adverse);
        }
        else
        {
            var favorable = (entryPrice - candle.Low) / entryPrice * 100m;
            var adverse = (candle.High - entryPrice) / entryPrice * 100m;
            mfe = Math.Max(mfe ?? 0m, favorable);
            mae = Math.Max(mae ?? 0m, adverse);
        }
    }

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

    private static bool HasOpenVariantSlot(
        string ruleName,
        DirectionalRuleEntryMode entryMode,
        int signalIndex,
        Dictionary<string, int> nextAllowedIndex)
    {
        foreach (var (targetPercent, stopPercent) in TargetStopMatrix)
        {
            foreach (var maxHoldMinutes in MaxHoldMinutesOptions)
            {
                var positionKey = BuildPositionKey(ruleName, entryMode, targetPercent, stopPercent, maxHoldMinutes);
                if (!nextAllowedIndex.TryGetValue(positionKey, out var allowedFrom) || signalIndex >= allowedFrom)
                    return true;
            }
        }

        return false;
    }

    private static bool TryResolveTargetStop(
        LongShortDirection direction,
        decimal entryPrice,
        KlineCandle candle,
        VariantSimulationState variant)
    {
        if (direction == LongShortDirection.Long)
        {
            var targetPrice = entryPrice * (1m + variant.TargetPercent / 100m);
            var stopPrice = entryPrice * (1m - variant.StopPercent / 100m);
            var hitTarget = candle.High >= targetPrice;
            var hitStop = candle.Low <= stopPrice;
            if (hitTarget && hitStop)
            {
                if (candle.Open <= entryPrice)
                    variant.Resolve("StopLoss", stopPrice, candle.OpenTimeUtc);
                else
                    variant.Resolve("ProfitTarget", targetPrice, candle.OpenTimeUtc);
                return true;
            }

            if (hitStop)
            {
                variant.Resolve("StopLoss", stopPrice, candle.OpenTimeUtc);
                return true;
            }

            if (hitTarget)
            {
                variant.Resolve("ProfitTarget", targetPrice, candle.OpenTimeUtc);
                return true;
            }
        }
        else
        {
            var targetPrice = entryPrice * (1m - variant.TargetPercent / 100m);
            var stopPrice = entryPrice * (1m + variant.StopPercent / 100m);
            var hitTarget = candle.Low <= targetPrice;
            var hitStop = candle.High >= stopPrice;
            if (hitTarget && hitStop)
            {
                if (candle.Open >= entryPrice)
                    variant.Resolve("StopLoss", stopPrice, candle.OpenTimeUtc);
                else
                    variant.Resolve("ProfitTarget", targetPrice, candle.OpenTimeUtc);
                return true;
            }

            if (hitStop)
            {
                variant.Resolve("StopLoss", stopPrice, candle.OpenTimeUtc);
                return true;
            }

            if (hitTarget)
            {
                variant.Resolve("ProfitTarget", targetPrice, candle.OpenTimeUtc);
                return true;
            }
        }

        return false;
    }

    private static string BuildPositionKey(
        string ruleName,
        DirectionalRuleEntryMode entryMode,
        decimal targetPercent,
        decimal stopPercent,
        int maxHoldMinutes)
        => $"{ruleName}|{entryMode}|{targetPercent:F2}|{stopPercent:F2}|{maxHoldMinutes}";

    private sealed class VariantSimulationState(decimal targetPercent, decimal stopPercent, int maxHoldMinutes)
    {
        public decimal TargetPercent { get; } = targetPercent;
        public decimal StopPercent { get; } = stopPercent;
        public int MaxHoldMinutes { get; } = maxHoldMinutes;
        public (decimal, decimal, int) Key => (TargetPercent, StopPercent, MaxHoldMinutes);
        public bool Resolved { get; private set; }
        public string ExitReason { get; private set; } = "EndOfData";
        public DateTime ExitTimeUtc { get; private set; }
        public decimal ExitPrice { get; private set; }
        public DateTime LastCandleTimeUtc { get; private set; }
        public decimal LastClose { get; private set; }
        public bool HasProcessedCandle { get; private set; }
        private decimal? _mfe;
        private decimal? _mae;

        public void MarkProcessed(DateTime candleTimeUtc, decimal close)
        {
            HasProcessedCandle = true;
            LastCandleTimeUtc = candleTimeUtc;
            LastClose = close;
        }

        public void UpdateExcursion(LongShortDirection direction, decimal entryPrice, KlineCandle candle)
        {
            if (direction == LongShortDirection.Long)
            {
                var favorable = (candle.High - entryPrice) / entryPrice * 100m;
                var adverse = (entryPrice - candle.Low) / entryPrice * 100m;
                _mfe = Math.Max(_mfe ?? 0m, favorable);
                _mae = Math.Max(_mae ?? 0m, adverse);
            }
            else
            {
                var favorable = (entryPrice - candle.Low) / entryPrice * 100m;
                var adverse = (candle.High - entryPrice) / entryPrice * 100m;
                _mfe = Math.Max(_mfe ?? 0m, favorable);
                _mae = Math.Max(_mae ?? 0m, adverse);
            }
        }

        public void Resolve(string exitReason, decimal exitPrice, DateTime exitTimeUtc)
        {
            Resolved = true;
            ExitReason = exitReason;
            ExitPrice = exitPrice;
            ExitTimeUtc = exitTimeUtc;
        }

        public void ResolveTimeStop(DateTime exitTimeUtc, decimal exitPrice)
        {
            if (Resolved)
                return;
            Resolved = true;
            ExitReason = "TimeStop";
            ExitTimeUtc = exitTimeUtc;
            ExitPrice = exitPrice;
        }

        public void ResolveEndOfData()
        {
            if (Resolved)
                return;
            Resolved = true;
            ExitReason = "EndOfData";
            ExitTimeUtc = LastCandleTimeUtc;
            ExitPrice = LastClose;
        }

        public DirectionalTradeSimulationResult ToResult(DateTime entryTimeUtc, decimal entryPrice)
        {
            var duration = Math.Round((decimal)(ExitTimeUtc - entryTimeUtc).TotalMinutes, 2);
            if (duration < 0m)
                duration = 0m;
            return new DirectionalTradeSimulationResult(
                entryTimeUtc,
                entryPrice,
                ExitTimeUtc,
                ExitPrice,
                ExitReason,
                _mfe.HasValue ? Math.Round(_mfe.Value, 6) : null,
                _mae.HasValue ? Math.Round(_mae.Value, 6) : null,
                duration);
        }
    }

    private static int FindEntryIndex(IReadOnlyList<KlineCandle> candles, DateTime entryTimeUtc)
    {
        // Sorted ascending by OpenTimeUtc: binary search for the rightmost candle <= entryTimeUtc.
        var lo = 0;
        var hi = candles.Count - 1;
        var result = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) >> 1);
            if (candles[mid].OpenTimeUtc <= entryTimeUtc)
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
