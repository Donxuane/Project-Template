using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record FuturesFlowStudyComboResult(
    IReadOnlyList<FuturesFlowRuleCandidateRow> Candidates,
    IReadOnlyList<FuturesFlowSplitPerformanceRow> SplitPerformance,
    int CandidateCount,
    int ValidationSurvivors,
    int HoldoutSurvivors);

public static class FuturesFlowFeatureEdgeStudyV1
{
    public static readonly string[] FlowFeatures =
    [
        "FundingRate",
        "FundingRateZScore",
        "MarkIndexDivergence",
        "OpenInterestChange30m",
        "OpenInterestChange60m",
        "OpenInterestZScore",
        "TakerBuySellImbalance",
        "LongShortRatioChange"
    ];

    private const int MaxReportedPerCombo = 25;

    private sealed class FlowPoint
    {
        public required DiscoveryBasePoint Base { get; init; }
        public required FuturesFlowFeatures Flow { get; init; }
        public required DirectionalTradeSimulationResult Sim { get; init; }
        public decimal ModerateNet { get; init; }
        public int ResolvedExitBase { get; init; }
        public bool Valid { get; init; }
    }

    private sealed record Rule(string Description, IReadOnlyList<string> Features, bool UsesFlow, Func<FlowPoint, bool> Predicate);

    private sealed class Eval
    {
        public required Rule Rule { get; init; }
        public required List<FlowPoint> Taken { get; init; }
        public int TrainTrades { get; set; }
        public int ValidationTrades { get; set; }
        public int HoldoutTrades { get; set; }
        public decimal TrainNet { get; set; }
        public decimal ValidationNet { get; set; }
        public decimal HoldoutNet { get; set; }
        public decimal FullNet { get; set; }
        public int TotalTrades => TrainTrades + ValidationTrades + HoldoutTrades;
        public bool TrainQualified => TrainTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTrainTrades && TrainNet > 0m;
        public bool ValidationSurvivor => TrainQualified
            && ValidationTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumValidationTrades && ValidationNet > 0m;
    }

    public static FuturesFlowStudyComboResult ScanCombo(
        TradingSymbol symbol,
        string interval,
        LongShortDirection direction,
        IReadOnlyList<DiscoveryBasePoint> basePoints,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        FuturesFlowFeatureBuilder flowBuilder,
        CancellationToken cancellationToken)
    {
        var scenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios();
        var moderate = scenarios.First(s => s.Label == FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryCostScenario);
        var config = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig;

        var points = BuildPoints(basePoints, intervalCandles, oneMinuteCandles, flowBuilder, direction, config, moderate);
        var valid = points.Where(p => p.Valid).ToList();
        if (valid.Count < FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTotalTrades)
            return new FuturesFlowStudyComboResult([], [], 0, 0, 0);

        var first = valid[0].Base.EntryTimeUtc;
        var last = valid[^1].Base.EntryTimeUtc;
        var span = last - first;
        if (span <= TimeSpan.Zero)
            return new FuturesFlowStudyComboResult([], [], 0, 0, 0);
        var trainEnd = first + TimeSpan.FromTicks((long)(span.Ticks * 0.5));
        var validationEnd = first + TimeSpan.FromTicks((long)(span.Ticks * 0.75));

        var trainPoints = valid.Where(p => p.Base.EntryTimeUtc < trainEnd).ToList();
        var rules = BuildRules(direction, trainPoints);
        if (rules.Count == 0)
            return new FuturesFlowStudyComboResult([], [], 0, 0, 0);

        var cooldown = config.CooldownCandles;
        var evals = new List<Eval>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(rule.Description))
                continue;
            var taken = GreedyWalk(valid, rule.Predicate, cooldown);
            var eval = new Eval { Rule = rule, Taken = taken };
            foreach (var pt in taken)
            {
                eval.FullNet += pt.ModerateNet;
                var entry = pt.Base.EntryTimeUtc;
                if (entry < trainEnd) { eval.TrainTrades++; eval.TrainNet += pt.ModerateNet; }
                else if (entry < validationEnd) { eval.ValidationTrades++; eval.ValidationNet += pt.ModerateNet; }
                else { eval.HoldoutTrades++; eval.HoldoutNet += pt.ModerateNet; }
            }

            if (eval.TrainTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTrainTrades)
                evals.Add(eval);
        }

        if (evals.Count == 0)
            return new FuturesFlowStudyComboResult([], [], 0, 0, 0);

        var qualified = evals.Count(e => e.TrainQualified);
        var survivors = evals.Where(e => e.ValidationSurvivor).ToList();
        var holdoutSurvivors = survivors.Count(e => e.HoldoutTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumHoldoutTrades && e.HoldoutNet > 0m);

        var reported = survivors
            .Concat(evals.Where(e => !e.ValidationSurvivor).OrderByDescending(e => e.TrainNet))
            .Distinct()
            .Take(Math.Max(MaxReportedPerCombo, survivors.Count))
            .ToList();

        var candidates = new List<FuturesFlowRuleCandidateRow>();
        var splitRows = new List<FuturesFlowSplitPerformanceRow>();
        var idx = 0;
        foreach (var eval in reported)
        {
            idx++;
            var ruleName = $"{symbol}_{interval}_{direction}_F{idx:D2}";
            var holdoutPositive = eval.HoldoutTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumHoldoutTrades && eval.HoldoutNet > 0m;
            var allSplits = eval.TrainNet > 0m && eval.ValidationSurvivor && holdoutPositive;
            var sufficient = eval.TotalTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTotalTrades
                             && eval.TrainTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTrainTrades
                             && eval.ValidationTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumValidationTrades
                             && eval.HoldoutTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumHoldoutTrades;
            var wins = eval.Taken.Count(p => p.ModerateNet > 0m);
            var grossWin = eval.Taken.Where(p => p.ModerateNet > 0m).Sum(p => p.ModerateNet);
            var grossLoss = Math.Abs(eval.Taken.Where(p => p.ModerateNet <= 0m).Sum(p => p.ModerateNet));

            candidates.Add(new FuturesFlowRuleCandidateRow
            {
                RuleName = ruleName,
                Symbol = symbol.ToString(),
                Interval = interval,
                Direction = direction.ToString(),
                RuleDescription = eval.Rule.Description,
                FeaturesUsed = string.Join("|", eval.Rule.Features),
                UsesFlowFeature = eval.Rule.UsesFlow,
                FeatureCount = eval.Rule.Features.Count,
                TotalTrades = eval.TotalTrades,
                TrainTrades = eval.TrainTrades,
                ValidationTrades = eval.ValidationTrades,
                HoldoutTrades = eval.HoldoutTrades,
                TrainNet = Math.Round(eval.TrainNet, 8),
                ValidationNet = Math.Round(eval.ValidationNet, 8),
                HoldoutNet = Math.Round(eval.HoldoutNet, 8),
                FullHistoryNet = Math.Round(eval.FullNet, 8),
                WinRate = eval.TotalTrades == 0 ? 0m : Math.Round((decimal)wins / eval.TotalTrades, 6),
                ProfitFactor = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : Math.Round(grossWin / grossLoss, 6),
                TrainPositive = eval.TrainNet > 0m,
                ValidationPositive = eval.ValidationSurvivor,
                HoldoutPositive = holdoutPositive,
                AllSplitsPositive = allSplits,
                TradeCountSufficient = sufficient,
                OverfitWarning = eval.TrainNet > 0m && (!eval.ValidationSurvivor || !holdoutPositive),
                UsesFutureInformation = false,
                SelectionStage = eval.ValidationSurvivor ? "ValidationSurvivor" : eval.TrainQualified ? "TrainQualified" : "Explored",
                Verdict = allSplits ? "SurvivesAllSplits" : eval.ValidationSurvivor ? "FailsHoldout" : eval.TrainQualified ? "FailsValidation" : "FailsTrain"
            });

            AddSplit(splitRows, ruleName, symbol, interval, direction, "Train", eval.Taken.Where(p => p.Base.EntryTimeUtc < trainEnd).ToList());
            AddSplit(splitRows, ruleName, symbol, interval, direction, "Validation", eval.Taken.Where(p => p.Base.EntryTimeUtc >= trainEnd && p.Base.EntryTimeUtc < validationEnd).ToList());
            AddSplit(splitRows, ruleName, symbol, interval, direction, "Holdout", eval.Taken.Where(p => p.Base.EntryTimeUtc >= validationEnd).ToList());
        }

        return new FuturesFlowStudyComboResult(candidates, splitRows, qualified, survivors.Count, holdoutSurvivors);
    }

    private static void AddSplit(
        List<FuturesFlowSplitPerformanceRow> rows,
        string ruleName,
        TradingSymbol symbol,
        string interval,
        LongShortDirection direction,
        string split,
        List<FlowPoint> trades)
    {
        var net = trades.Sum(p => p.ModerateNet);
        var wins = trades.Count(p => p.ModerateNet > 0m);
        rows.Add(new FuturesFlowSplitPerformanceRow
        {
            RuleName = ruleName,
            Symbol = symbol.ToString(),
            Interval = interval,
            Direction = direction.ToString(),
            Split = split,
            CostScenarioLabel = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryCostScenario,
            TradeCount = trades.Count,
            WinCount = wins,
            WinRate = trades.Count == 0 ? 0m : Math.Round((decimal)wins / trades.Count, 6),
            NetPnlQuote = Math.Round(net, 8),
            AvgNetPerTrade = trades.Count == 0 ? null : Math.Round(net / trades.Count, 8),
            Positive = net > 0m
        });
    }

    private static List<Rule> BuildRules(LongShortDirection direction, List<FlowPoint> trainPoints)
    {
        var rules = new List<Rule>();

        // Candle thesis bucket for pairing/baseline (direction-aware).
        var candleFeature = direction == LongShortDirection.Short
            ? nameof(MarketRegimeForwardEdgeScanner.RegimeCandleFeatures.DistanceFromRecentHighPercent)
            : nameof(MarketRegimeForwardEdgeScanner.RegimeCandleFeatures.DistanceFromRecentLowPercent);
        var candleBounds = FuturesDirectionalRuleDiscoveryV2Catalog.ComputeTertiles(
            trainPoints.Select(p => p.Base).ToList(), candleFeature);
        var candleThesis = candleBounds.Valid
            ? new Rule($"{candleFeature} Q1(<={candleBounds.B33:0.0000})",
                [candleFeature], false,
                p => FuturesDirectionalRuleDiscoveryV2Catalog.GetNumeric(p.Base, candleFeature) is { } v && v <= candleBounds.B33)
            : null;
        if (candleThesis is not null)
            rules.Add(candleThesis);

        foreach (var feature in FlowFeatures)
        {
            var values = trainPoints
                .Select(p => GetFlow(p.Flow, feature))
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .OrderBy(v => v)
                .ToArray();
            if (values.Length < 30)
                continue;
            var b33 = values[(int)(values.Length / 3.0)];
            var b66 = values[(int)(values.Length * 2.0 / 3.0)];
            if (b66 <= b33)
                continue;

            var q1 = new Rule($"{feature} Q1(<={b33:0.000000})", [feature], true,
                p => GetFlow(p.Flow, feature) is { } v && v <= b33);
            var q3 = new Rule($"{feature} Q3(>{b66:0.000000})", [feature], true,
                p => GetFlow(p.Flow, feature) is { } v && v > b66);
            rules.Add(q1);
            rules.Add(q3);

            if (candleThesis is not null)
            {
                rules.Add(Combine(candleThesis, q1));
                rules.Add(Combine(candleThesis, q3));
            }
        }

        return rules;
    }

    private static Rule Combine(Rule a, Rule b)
        => new($"{a.Description} AND {b.Description}",
            a.Features.Concat(b.Features).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            a.UsesFlow || b.UsesFlow,
            p => a.Predicate(p) && b.Predicate(p));

    private static List<FlowPoint> GreedyWalk(List<FlowPoint> points, Func<FlowPoint, bool> predicate, int cooldown)
    {
        var taken = new List<FlowPoint>();
        var nextAllowed = 0;
        foreach (var p in points)
        {
            if (p.Base.SignalIndex < nextAllowed)
                continue;
            if (!predicate(p))
                continue;
            taken.Add(p);
            nextAllowed = p.ResolvedExitBase + cooldown;
        }

        return taken;
    }

    private static List<FlowPoint> BuildPoints(
        IReadOnlyList<DiscoveryBasePoint> basePoints,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        FuturesFlowFeatureBuilder flowBuilder,
        LongShortDirection direction,
        DiscoveryRuleConfig config,
        DirectionalRuleV3CostScenario moderate)
    {
        var result = new List<FlowPoint>(basePoints.Count);
        foreach (var bp in basePoints)
        {
            var entryPrice = config.EntryMode == DirectionalRuleEntryMode.NextOpen ? bp.EntryPriceNextOpen : bp.EntryPriceNextClose;
            var flow = flowBuilder.Build(bp.EntryTimeUtc);
            if (entryPrice <= 0m)
            {
                result.Add(new FlowPoint { Base = bp, Flow = flow, Sim = Invalid(bp.EntryTimeUtc, entryPrice), Valid = false });
                continue;
            }

            var sim = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
                oneMinuteCandles, bp.EntryTimeUtc, entryPrice, config.MaxHoldMinutes, config.TargetPercent, config.StopPercent, direction);
            var valid = sim.ExitReason != "InvalidEntry";
            result.Add(new FlowPoint
            {
                Base = bp,
                Flow = flow,
                Sim = sim,
                Valid = valid,
                ModerateNet = valid ? DirectionalRuleFuturesValidationV3CostModel.ComputeCostBreakdown(sim, direction, moderate).NetPnlQuote : 0m,
                ResolvedExitBase = valid ? BinaryFirstGreater(intervalCandles, sim.ExitTimeUtc) + 1 : int.MaxValue
            });
        }

        return result;
    }

    private static DirectionalTradeSimulationResult Invalid(DateTime entryTime, decimal entryPrice)
        => new(entryTime, entryPrice, entryTime, entryPrice, "InvalidEntry", null, null, 0m);

    private static int BinaryFirstGreater(IReadOnlyList<KlineCandle> intervalCandles, DateTime exitTimeUtc)
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

        return lo;
    }

    public static decimal? GetFlow(FuturesFlowFeatures f, string name)
        => name switch
        {
            "FundingRate" => f.FundingRate,
            "FundingRateZScore" => f.FundingRateZScore,
            "MarkIndexDivergence" => f.MarkIndexDivergence,
            "OpenInterestChange15m" => f.OpenInterestChange15m,
            "OpenInterestChange30m" => f.OpenInterestChange30m,
            "OpenInterestChange60m" => f.OpenInterestChange60m,
            "OpenInterestZScore" => f.OpenInterestZScore,
            "TakerBuySellImbalance" => f.TakerBuySellImbalance,
            "LongShortRatioChange" => f.LongShortRatioChange,
            _ => null
        };
}
