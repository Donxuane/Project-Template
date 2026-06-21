using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record DiscoveryFeatureContribution(
    string Feature,
    string Direction,
    decimal TrainNet,
    decimal ValidationNet,
    decimal HoldoutNet,
    bool TrainQualified,
    bool ValidationSurvivor,
    bool HoldoutPositive);

public sealed record DiscoveryComboResult(
    IReadOnlyList<DiscoveryRuleCandidateRow> Candidates,
    IReadOnlyList<DiscoveryTradeRow> Trades,
    IReadOnlyList<DiscoverySplitPerformanceRow> SplitPerformance,
    IReadOnlyList<DiscoveryWindowRobustnessRow> WindowRobustness,
    IReadOnlyList<DiscoveryMonthlyPerformanceRow> MonthlyPerformance,
    IReadOnlyList<DiscoveryCostSensitivityRow> CostSensitivity,
    IReadOnlyList<DiscoveryDrawdownRow> Drawdown,
    IReadOnlyList<DiscoveryFeatureContribution> FeatureContributions,
    int CandidateCount,
    int ValidationSurvivorCount,
    int HoldoutSurvivorCount);

public static class FuturesDirectionalRuleDiscoveryV2Engine
{
    private sealed class DirectionalPoint
    {
        public required DiscoveryBasePoint Base { get; init; }
        public required DirectionalTradeSimulationResult Sim { get; init; }
        public decimal ModerateNet { get; init; }
        public int ResolvedExitBase { get; init; }
        public bool Valid { get; init; }
    }

    private sealed class CandidateEval
    {
        public required DiscoveryGeneratedRule Rule { get; init; }
        public required List<DirectionalPoint> Taken { get; init; }
        public int TrainTrades { get; set; }
        public int ValidationTrades { get; set; }
        public int HoldoutTrades { get; set; }
        public int TotalTrades { get; set; }
        public decimal TrainNet { get; set; }
        public decimal ValidationNet { get; set; }
        public decimal HoldoutNet { get; set; }
        public decimal FullNet { get; set; }
        public bool TrainQualified => TrainTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTrainTrades && TrainNet > 0m;
        public bool ValidationSurvivor => TrainQualified
            && ValidationTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumValidationTrades
            && ValidationNet > 0m;
    }

    public static DiscoveryComboResult ScanCombo(
        TradingSymbol symbol,
        string interval,
        LongShortDirection direction,
        IReadOnlyList<DiscoveryBasePoint> basePoints,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        DateTime dataEndUtc,
        CancellationToken cancellationToken)
    {
        var scenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios();
        var moderate = scenarios.First(s => s.Label == FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryCostScenario);
        var primary = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig;

        var points = BuildDirectionalPoints(basePoints, intervalCandles, oneMinuteCandles, direction, primary, moderate);
        var valid = points.Where(p => p.Valid).ToList();
        if (valid.Count < FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTotalTrades)
            return Empty();

        var firstTime = valid[0].Base.EntryTimeUtc;
        var lastTime = valid[^1].Base.EntryTimeUtc;
        var totalSpan = lastTime - firstTime;
        if (totalSpan <= TimeSpan.Zero)
            return Empty();
        var trainEnd = firstTime + TimeSpan.FromTicks((long)(totalSpan.Ticks * 0.5));
        var validationEnd = firstTime + TimeSpan.FromTicks((long)(totalSpan.Ticks * 0.75));

        var trainPoints = valid.Where(p => p.Base.EntryTimeUtc < trainEnd).Select(p => p.Base).ToList();
        if (trainPoints.Count < FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTrainTrades)
            return Empty();

        var tertiles = new Dictionary<string, FuturesDirectionalRuleDiscoveryV2Catalog.TertileBounds>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in FuturesDirectionalRuleDiscoveryV2Catalog.NumericRuleFeatures)
            tertiles[feature] = FuturesDirectionalRuleDiscoveryV2Catalog.ComputeTertiles(trainPoints, feature);

        var allEvals = DiscoverCandidates(valid, trainPoints, tertiles, direction, trainEnd, validationEnd, cancellationToken);
        if (allEvals.Count == 0)
            return Empty();

        var qualified = allEvals.Where(e => e.TrainQualified).ToList();
        var survivors = qualified.Where(e => e.ValidationSurvivor).ToList();
        // Report validation survivors plus the best-explored attempts (by train net) so the search is
        // transparent even when nothing qualifies. Survivor labelling/selection stays strict.
        var reported = survivors
            .Concat(allEvals.Where(e => !e.ValidationSurvivor).OrderByDescending(e => e.TrainNet))
            .Distinct()
            .Take(Math.Max(FuturesDirectionalRuleDiscoveryV2Catalog.MaxReportedCandidatesPerCombo, survivors.Count))
            .ToList();

        var candidates = new List<DiscoveryRuleCandidateRow>();
        var tradeRows = new List<DiscoveryTradeRow>();
        var splitRows = new List<DiscoverySplitPerformanceRow>();
        var windowRows = new List<DiscoveryWindowRobustnessRow>();
        var monthlyRows = new List<DiscoveryMonthlyPerformanceRow>();
        var costRows = new List<DiscoveryCostSensitivityRow>();
        var drawdownRows = new List<DiscoveryDrawdownRow>();
        var contributions = new List<DiscoveryFeatureContribution>();
        var holdoutSurvivors = 0;

        foreach (var eval in allEvals)
        {
            var holdoutPositive = eval.HoldoutTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumHoldoutTrades && eval.HoldoutNet > 0m;
            if (eval.ValidationSurvivor && holdoutPositive)
                holdoutSurvivors++;
            foreach (var feature in eval.Rule.FeaturesUsed)
            {
                contributions.Add(new DiscoveryFeatureContribution(
                    feature, direction.ToString(), eval.TrainNet, eval.ValidationNet, eval.HoldoutNet,
                    eval.TrainQualified, eval.ValidationSurvivor, holdoutPositive));
            }
        }

        var ruleIndex = 0;
        foreach (var eval in reported)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ruleIndex++;
            var ruleName = $"{symbol}_{interval}_{direction}_R{ruleIndex:D2}";
            var emitTrades = eval.ValidationSurvivor || (survivors.Count == 0 && ruleIndex == 1);

            BuildCandidateReports(
                symbol, interval, direction, ruleName, eval, scenarios, moderate,
                trainEnd, validationEnd, dataEndUtc, basePoints, intervalCandles, oneMinuteCandles,
                emitTrades,
                candidates, tradeRows, splitRows, windowRows, monthlyRows, costRows, drawdownRows,
                cancellationToken);
        }

        return new DiscoveryComboResult(
            candidates, tradeRows, splitRows, windowRows, monthlyRows, costRows, drawdownRows,
            contributions, qualified.Count, survivors.Count, holdoutSurvivors);

        static DiscoveryComboResult Empty()
            => new([], [], [], [], [], [], [], [], 0, 0, 0);
    }

    private static List<CandidateEval> DiscoverCandidates(
        List<DirectionalPoint> valid,
        List<DiscoveryBasePoint> trainPoints,
        IReadOnlyDictionary<string, FuturesDirectionalRuleDiscoveryV2Catalog.TertileBounds> tertiles,
        LongShortDirection direction,
        DateTime trainEnd,
        DateTime validationEnd,
        CancellationToken cancellationToken)
    {
        var cooldown = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.CooldownCandles;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var all = new List<CandidateEval>();

        CandidateEval Evaluate(DiscoveryGeneratedRule rule)
        {
            var taken = GreedyWalk(valid, rule.Predicate, cooldown);
            var eval = new CandidateEval { Rule = rule, Taken = taken };
            foreach (var pt in taken)
            {
                var net = pt.ModerateNet;
                eval.TotalTrades++;
                eval.FullNet += net;
                var entry = pt.Base.EntryTimeUtc;
                if (entry < trainEnd) { eval.TrainTrades++; eval.TrainNet += net; }
                else if (entry < validationEnd) { eval.ValidationTrades++; eval.ValidationNet += net; }
                else { eval.HoldoutTrades++; eval.HoldoutNet += net; }
            }

            return eval;
        }

        CandidateEval? Consider(DiscoveryGeneratedRule rule)
        {
            if (!seen.Add(rule.Description))
                return null;
            var eval = Evaluate(rule);
            // Keep only candidates with enough train trades to be meaningful; both qualified and
            // best-effort (negative train) attempts are retained for transparent reporting.
            if (eval.TrainTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTrainTrades)
                all.Add(eval);
            return eval;
        }

        var singleEvals = new List<CandidateEval>();
        foreach (var rule in FuturesDirectionalRuleDiscoveryV2Catalog.BuildSingleFeatureRules(trainPoints, tertiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eval = Consider(rule);
            if (eval is not null)
                singleEvals.Add(eval);
        }

        var qualifiedSingles = singleEvals.Where(e => e.TrainQualified).ToList();
        var topSingles = (qualifiedSingles.Count > 0 ? qualifiedSingles : singleEvals)
            .OrderByDescending(e => e.TrainNet)
            .Take(FuturesDirectionalRuleDiscoveryV2Catalog.TopSinglesForPairs)
            .Select(e => e.Rule)
            .ToList();

        var pairRules = new List<DiscoveryGeneratedRule>();
        pairRules.AddRange(FuturesDirectionalRuleDiscoveryV2Catalog.BuildCuratedPairRules(direction, tertiles));
        for (var a = 0; a < topSingles.Count; a++)
            for (var b = a + 1; b < topSingles.Count; b++)
                pairRules.Add(FuturesDirectionalRuleDiscoveryV2Catalog.Combine(topSingles[a], topSingles[b]));

        var pairEvals = new List<CandidateEval>();
        foreach (var rule in pairRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var eval = Consider(rule);
            if (eval is not null)
                pairEvals.Add(eval);
        }

        var qualifiedPairs = pairEvals.Where(e => e.TrainQualified).ToList();
        var topPairs = (qualifiedPairs.Count > 0 ? qualifiedPairs : pairEvals)
            .OrderByDescending(e => e.TrainNet)
            .Take(FuturesDirectionalRuleDiscoveryV2Catalog.TopPairsForTriples)
            .Select(e => e.Rule)
            .ToList();

        var tripleAdded = 0;
        foreach (var pair in topPairs)
        {
            foreach (var singleRule in topSingles)
            {
                if (tripleAdded >= FuturesDirectionalRuleDiscoveryV2Catalog.MaxTripleRules)
                    break;
                if (pair.FeaturesUsed.Contains(singleRule.FeaturesUsed[0], StringComparer.OrdinalIgnoreCase))
                    continue;
                var triple = FuturesDirectionalRuleDiscoveryV2Catalog.Combine(pair, singleRule);
                if (!seen.Contains(triple.Description))
                {
                    Consider(triple);
                    tripleAdded++;
                }
            }

            if (tripleAdded >= FuturesDirectionalRuleDiscoveryV2Catalog.MaxTripleRules)
                break;
        }

        return all;
    }

    private static void BuildCandidateReports(
        TradingSymbol symbol,
        string interval,
        LongShortDirection direction,
        string ruleName,
        CandidateEval eval,
        IReadOnlyList<DirectionalRuleV3CostScenario> scenarios,
        DirectionalRuleV3CostScenario moderate,
        DateTime trainEnd,
        DateTime validationEnd,
        DateTime dataEndUtc,
        IReadOnlyList<DiscoveryBasePoint> basePoints,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        bool emitTrades,
        List<DiscoveryRuleCandidateRow> candidates,
        List<DiscoveryTradeRow> tradeRows,
        List<DiscoverySplitPerformanceRow> splitRows,
        List<DiscoveryWindowRobustnessRow> windowRows,
        List<DiscoveryMonthlyPerformanceRow> monthlyRows,
        List<DiscoveryCostSensitivityRow> costRows,
        List<DiscoveryDrawdownRow> drawdownRows,
        CancellationToken cancellationToken)
    {
        var ordered = eval.Taken.OrderBy(p => p.Base.EntryTimeUtc).ToList();
        var fullTrades = ordered.Select(p => ToEval(p, p.ModerateNet, trainEnd, validationEnd)).ToList();
        var fullRisk = ComputeRisk(fullTrades);

        var trainTrades = fullTrades.Where(t => t.Split == "Train").ToList();
        var valTrades = fullTrades.Where(t => t.Split == "Validation").ToList();
        var holdoutTrades = fullTrades.Where(t => t.Split == "Holdout").ToList();

        var monthly = fullTrades
            .GroupBy(t => t.MonthKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Month: g.Key, Net: g.Sum(t => t.Net), Count: g.Count(), Wins: g.Count(t => t.Net > 0m)))
            .ToList();
        var positiveMonths = monthly.Count(m => m.Net > 0m);
        var totalMonths = monthly.Count;
        var monthlyPass = totalMonths >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumActiveMonths
                          && positiveMonths * 2 >= totalMonths;

        var stressNet = SplitNet(ordered, scenarios.First(s => s.Label == "futures-stress"), direction);
        var stressPlusNet = SplitNet(ordered, scenarios.First(s => s.Label == "futures-stress-plus"), direction);

        var trainPositive = eval.TrainNet > 0m;
        var validationPositive = eval.ValidationSurvivor;
        var holdoutPositive = holdoutTrades.Count >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumHoldoutTrades && eval.HoldoutNet > 0m;
        var allSplitsPositive = trainPositive && validationPositive && holdoutPositive;
        var fullPositive = eval.FullNet > 0m;
        var tradeCountSufficient = eval.TotalTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTotalTrades
                                   && eval.TrainTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumTrainTrades
                                   && eval.ValidationTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumValidationTrades
                                   && eval.HoldoutTrades >= FuturesDirectionalRuleDiscoveryV2Catalog.MinimumHoldoutTrades;
        var overfitWarning = trainPositive && (!validationPositive || !holdoutPositive);

        // Config robustness sweep (only for validation survivors).
        var configVariantsTested = 0;
        var configVariantsPositive = 0;
        var bestConfigLabel = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.Label;
        var bestConfigTrain = eval.TrainNet;
        var bestConfigValidation = eval.ValidationNet;
        var bestConfigHoldout = eval.HoldoutNet;
        var bestConfigFull = eval.FullNet;
        if (eval.ValidationSurvivor)
        {
            var bestScore = eval.TrainNet + eval.ValidationNet;
            foreach (var config in FuturesDirectionalRuleDiscoveryV2Catalog.BuildConfigMatrix())
            {
                cancellationToken.ThrowIfCancellationRequested();
                configVariantsTested++;
                var (cTrain, cVal, cHoldout, cFull) = EvaluateConfig(
                    eval.Rule.Predicate, basePoints, intervalCandles, oneMinuteCandles, direction, config, moderate, trainEnd, validationEnd);
                if (cFull > 0m)
                    configVariantsPositive++;
                var score = cTrain + cVal;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestConfigLabel = config.Label;
                    bestConfigTrain = cTrain;
                    bestConfigValidation = cVal;
                    bestConfigHoldout = cHoldout;
                    bestConfigFull = cFull;
                }
            }
        }

        candidates.Add(new DiscoveryRuleCandidateRow
        {
            RuleName = ruleName,
            Symbol = symbol.ToString(),
            Interval = interval,
            Direction = direction.ToString(),
            RuleDescription = eval.Rule.Description,
            FeaturesUsed = string.Join("|", eval.Rule.FeaturesUsed),
            FeatureCount = eval.Rule.FeaturesUsed.Count,
            ConfigLabel = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.Label,
            EntryMode = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.EntryMode.ToString(),
            TargetPercent = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.TargetPercent,
            StopPercent = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.StopPercent,
            MaxHoldMinutes = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.MaxHoldMinutes,
            CooldownCandles = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.CooldownCandles,
            TotalTrades = eval.TotalTrades,
            TrainTrades = eval.TrainTrades,
            ValidationTrades = eval.ValidationTrades,
            HoldoutTrades = eval.HoldoutTrades,
            TrainNet = Math.Round(eval.TrainNet, 8),
            ValidationNet = Math.Round(eval.ValidationNet, 8),
            HoldoutNet = Math.Round(eval.HoldoutNet, 8),
            FullHistoryNet = Math.Round(eval.FullNet, 8),
            PositiveMonths = positiveMonths,
            TotalMonths = totalMonths,
            ProfitFactor = fullRisk.ProfitFactor,
            WinRate = fullRisk.WinRate,
            MaxDrawdownQuote = fullRisk.MaxDrawdownQuote,
            TrainPositive = trainPositive,
            ValidationPositive = validationPositive,
            HoldoutPositive = holdoutPositive,
            AllSplitsPositive = allSplitsPositive,
            FullHistoryPositive = fullPositive,
            StressPositive = stressNet > 0m,
            StressPlusPositive = stressPlusNet > 0m,
            MonthlyConsistencyPass = monthlyPass,
            TradeCountSufficient = tradeCountSufficient,
            OverfitWarning = overfitWarning,
            UsesFutureInformation = false,
            ConfigVariantsTested = configVariantsTested,
            ConfigVariantsFullHistoryPositive = configVariantsPositive,
            BestConfigLabel = bestConfigLabel,
            BestConfigTrainNet = Math.Round(bestConfigTrain, 8),
            BestConfigValidationNet = Math.Round(bestConfigValidation, 8),
            BestConfigHoldoutNet = Math.Round(bestConfigHoldout, 8),
            BestConfigFullHistoryNet = Math.Round(bestConfigFull, 8),
            SelectionStage = eval.ValidationSurvivor ? "ValidationSurvivor" : eval.TrainQualified ? "TrainQualified" : "Explored",
            Verdict = ClassifyVerdict(allSplitsPositive, validationPositive, fullPositive, overfitWarning, tradeCountSufficient)
        });

        AddSplitRow(splitRows, symbol, interval, direction, ruleName, "Train", trainTrades);
        AddSplitRow(splitRows, symbol, interval, direction, ruleName, "Validation", valTrades);
        AddSplitRow(splitRows, symbol, interval, direction, ruleName, "Holdout", holdoutTrades);
        AddSplitRow(splitRows, symbol, interval, direction, ruleName, "FullHistory", fullTrades);

        foreach (var windowDays in new[] { 30, 60, 90, 180, 365 })
        {
            var windowStart = dataEndUtc.AddDays(-windowDays);
            var windowTrades = fullTrades.Where(t => t.Entry >= windowStart).ToList();
            var net = windowTrades.Sum(t => t.Net);
            windowRows.Add(new DiscoveryWindowRobustnessRow
            {
                RuleName = ruleName,
                Symbol = symbol.ToString(),
                Interval = interval,
                Direction = direction.ToString(),
                WindowLabel = $"{windowDays}d",
                CostScenarioLabel = moderate.Label,
                TradeCount = windowTrades.Count,
                NetPnlQuote = Math.Round(net, 8),
                WinRate = windowTrades.Count == 0 ? 0m : Math.Round((decimal)windowTrades.Count(t => t.Net > 0m) / windowTrades.Count, 6),
                Positive = net > 0m
            });
        }

        foreach (var m in monthly)
        {
            monthlyRows.Add(new DiscoveryMonthlyPerformanceRow
            {
                RuleName = ruleName,
                Symbol = symbol.ToString(),
                Interval = interval,
                Direction = direction.ToString(),
                MonthKey = m.Month,
                CostScenarioLabel = moderate.Label,
                TradeCount = m.Count,
                NetPnlQuote = Math.Round(m.Net, 8),
                WinRate = m.Count == 0 ? 0m : Math.Round((decimal)m.Wins / m.Count, 6),
                Positive = m.Net > 0m
            });
        }

        foreach (var scenario in scenarios)
        {
            var trainNet = 0m;
            var valNet = 0m;
            var holdNet = 0m;
            foreach (var pt in ordered)
            {
                var net = NetFor(pt, scenario, direction);
                var entry = pt.Base.EntryTimeUtc;
                if (entry < trainEnd) trainNet += net;
                else if (entry < validationEnd) valNet += net;
                else holdNet += net;
            }

            var fullNet = trainNet + valNet + holdNet;
            costRows.Add(new DiscoveryCostSensitivityRow
            {
                RuleName = ruleName,
                Symbol = symbol.ToString(),
                Interval = interval,
                Direction = direction.ToString(),
                CostScenarioLabel = scenario.Label,
                TradeCount = eval.TotalTrades,
                TrainNet = Math.Round(trainNet, 8),
                ValidationNet = Math.Round(valNet, 8),
                HoldoutNet = Math.Round(holdNet, 8),
                FullHistoryNet = Math.Round(fullNet, 8),
                FullHistoryPositive = fullNet > 0m,
                AllSplitsPositive = trainNet > 0m && valNet > 0m && holdNet > 0m
            });
        }

        var worstMonth = monthly.Count == 0 ? 0m : monthly.Min(m => m.Net);
        var bestMonth = monthly.Count == 0 ? 0m : monthly.Max(m => m.Net);
        var equityFinal = fullTrades.Sum(t => t.Net);
        drawdownRows.Add(new DiscoveryDrawdownRow
        {
            RuleName = ruleName,
            Symbol = symbol.ToString(),
            Interval = interval,
            Direction = direction.ToString(),
            CostScenarioLabel = moderate.Label,
            TotalTrades = eval.TotalTrades,
            MaxDrawdownQuote = fullRisk.MaxDrawdownQuote,
            MaxConsecutiveLosses = fullRisk.MaxConsecutiveLosses,
            WorstTradeNet = fullRisk.WorstTradeNet,
            BestTradeNet = fullRisk.BestTradeNet,
            ProfitFactor = fullRisk.ProfitFactor,
            AverageWin = fullRisk.AverageWin,
            AverageLoss = fullRisk.AverageLoss,
            EquityFinalQuote = Math.Round(equityFinal, 8),
            PositiveMonthsCount = positiveMonths,
            TotalMonthsCount = totalMonths,
            WorstMonthNet = Math.Round(worstMonth, 8),
            BestMonthNet = Math.Round(bestMonth, 8)
        });

        if (emitTrades)
        {
            foreach (var pt in ordered)
            {
                var e = ToEval(pt, pt.ModerateNet, trainEnd, validationEnd);
                tradeRows.Add(new DiscoveryTradeRow
                {
                    RuleName = ruleName,
                    Symbol = symbol.ToString(),
                    Interval = interval,
                    Direction = direction.ToString(),
                    EntryTimeUtc = pt.Sim.EntryTimeUtc,
                    ExitTimeUtc = pt.Sim.ExitTimeUtc,
                    Split = e.Split,
                    MonthKey = e.MonthKey,
                    EntryPrice = pt.Sim.EntryPrice,
                    ExitPrice = pt.Sim.ExitPrice,
                    GrossPnlQuote = Math.Round(direction == LongShortDirection.Long
                        ? pt.Sim.ExitPrice - pt.Sim.EntryPrice
                        : pt.Sim.EntryPrice - pt.Sim.ExitPrice, 8),
                    NetPnlQuote = Math.Round(pt.ModerateNet, 8),
                    ExitReason = pt.Sim.ExitReason,
                    DurationMinutes = pt.Sim.DurationMinutes,
                    AtrPercent = pt.Base.Features.AtrPercent,
                    RangeWidthPercent = pt.Base.Features.RangeWidthPercent,
                    TrendSlopePercent = pt.Base.Features.TrendSlopePercent,
                    DistanceFromRecentHighPercent = pt.Base.Features.DistanceFromRecentHighPercent,
                    DistanceFromRecentLowPercent = pt.Base.Features.DistanceFromRecentLowPercent,
                    BtcReturn30mPercent = pt.Base.Features.BtcReturn30mPercent,
                    VolatilityRegime = pt.Base.Features.VolatilityRegime,
                    SessionBucket = pt.Base.SessionBucket,
                    HourOfDayUtc = pt.Base.HourOfDayUtc
                });
            }
        }
    }

    private readonly record struct EvalTrade(DateTime Entry, decimal Net, string ExitReason, decimal Duration, string Split, string MonthKey);

    private static EvalTrade ToEval(DirectionalPoint pt, decimal net, DateTime trainEnd, DateTime validationEnd)
    {
        var entry = pt.Base.EntryTimeUtc;
        var split = entry < trainEnd ? "Train" : entry < validationEnd ? "Validation" : "Holdout";
        return new EvalTrade(entry, net, pt.Sim.ExitReason, pt.Sim.DurationMinutes, split, $"{entry.Year}-{entry.Month:D2}");
    }

    private static void AddSplitRow(
        List<DiscoverySplitPerformanceRow> rows,
        TradingSymbol symbol,
        string interval,
        LongShortDirection direction,
        string ruleName,
        string split,
        List<EvalTrade> trades)
    {
        var risk = ComputeRisk(trades);
        rows.Add(new DiscoverySplitPerformanceRow
        {
            RuleName = ruleName,
            Symbol = symbol.ToString(),
            Interval = interval,
            Direction = direction.ToString(),
            Split = split,
            CostScenarioLabel = FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryCostScenario,
            TradeCount = risk.TradeCount,
            WinCount = risk.WinCount,
            WinRate = risk.WinRate,
            NetPnlQuote = Math.Round(risk.NetPnlQuote, 8),
            AvgNetPerTrade = risk.TradeCount == 0 ? null : Math.Round(risk.NetPnlQuote / risk.TradeCount, 8),
            MedianNetPerTrade = risk.MedianNetPerTrade,
            ProfitFactor = risk.ProfitFactor,
            MaxDrawdownQuote = risk.MaxDrawdownQuote,
            MaxConsecutiveLosses = risk.MaxConsecutiveLosses,
            WorstTradeNet = risk.WorstTradeNet,
            ProfitTargetRate = risk.ProfitTargetRate,
            StopLossRate = risk.StopLossRate,
            TimeStopRate = risk.TimeStopRate,
            AverageHoldMinutes = risk.AverageHoldMinutes,
            Positive = risk.NetPnlQuote > 0m
        });
    }

    private static DiscoveryRiskMetrics ComputeRisk(List<EvalTrade> trades)
    {
        if (trades.Count == 0)
            return new DiscoveryRiskMetrics();

        var ordered = trades.OrderBy(t => t.Entry).ToList();
        var net = ordered.Sum(t => t.Net);
        var wins = ordered.Where(t => t.Net > 0m).ToList();
        var losses = ordered.Where(t => t.Net <= 0m).ToList();
        var grossWin = wins.Sum(t => t.Net);
        var grossLoss = Math.Abs(losses.Sum(t => t.Net));

        decimal equity = 0m, peak = 0m, maxDd = 0m;
        var consec = 0; var maxConsec = 0;
        foreach (var t in ordered)
        {
            equity += t.Net;
            if (equity > peak) peak = equity;
            var dd = peak - equity;
            if (dd > maxDd) maxDd = dd;
            if (t.Net <= 0m) { consec++; if (consec > maxConsec) maxConsec = consec; }
            else consec = 0;
        }

        var sortedNet = ordered.Select(t => t.Net).OrderBy(v => v).ToArray();
        var mid = sortedNet.Length / 2;
        var median = sortedNet.Length % 2 == 0
            ? (sortedNet[mid - 1] + sortedNet[mid]) / 2m
            : sortedNet[mid];

        return new DiscoveryRiskMetrics
        {
            NetPnlQuote = net,
            TradeCount = ordered.Count,
            WinCount = wins.Count,
            WinRate = Math.Round((decimal)wins.Count / ordered.Count, 6),
            AverageWin = wins.Count == 0 ? null : Math.Round(grossWin / wins.Count, 8),
            AverageLoss = losses.Count == 0 ? null : Math.Round(losses.Sum(t => t.Net) / losses.Count, 8),
            MedianNetPerTrade = Math.Round(median, 8),
            ProfitFactor = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : Math.Round(grossWin / grossLoss, 6),
            MaxDrawdownQuote = Math.Round(maxDd, 8),
            MaxConsecutiveLosses = maxConsec,
            WorstTradeNet = Math.Round(sortedNet[0], 8),
            BestTradeNet = Math.Round(sortedNet[^1], 8),
            AverageHoldMinutes = Math.Round(ordered.Average(t => t.Duration), 4),
            ProfitTargetRate = Math.Round((decimal)ordered.Count(t => t.ExitReason == "ProfitTarget") / ordered.Count, 6),
            StopLossRate = Math.Round((decimal)ordered.Count(t => t.ExitReason == "StopLoss") / ordered.Count, 6),
            TimeStopRate = Math.Round((decimal)ordered.Count(t => t.ExitReason is "TimeStop" or "EndOfData") / ordered.Count, 6)
        };
    }

    private static List<DirectionalPoint> GreedyWalk(
        List<DirectionalPoint> points,
        Func<DiscoveryBasePoint, bool> predicate,
        int cooldown)
    {
        var taken = new List<DirectionalPoint>();
        var nextAllowed = 0;
        foreach (var p in points)
        {
            if (p.Base.SignalIndex < nextAllowed)
                continue;
            if (!predicate(p.Base))
                continue;
            taken.Add(p);
            nextAllowed = p.ResolvedExitBase + cooldown;
        }

        return taken;
    }

    private static (decimal Train, decimal Validation, decimal Holdout, decimal Full) EvaluateConfig(
        Func<DiscoveryBasePoint, bool> predicate,
        IReadOnlyList<DiscoveryBasePoint> basePoints,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        LongShortDirection direction,
        DiscoveryRuleConfig config,
        DirectionalRuleV3CostScenario moderate,
        DateTime trainEnd,
        DateTime validationEnd)
    {
        // Lazy greedy walk reusing precomputed base-point features; only the trade outcome is
        // re-simulated under the supplied execution config. This isolates execution sensitivity.
        var nextAllowed = 0;
        decimal train = 0m, val = 0m, hold = 0m;
        foreach (var bp in basePoints)
        {
            if (bp.SignalIndex < nextAllowed)
                continue;
            if (!predicate(bp))
                continue;
            var entryPrice = config.EntryMode == DirectionalRuleEntryMode.NextOpen
                ? bp.EntryPriceNextOpen
                : bp.EntryPriceNextClose;
            if (entryPrice <= 0m)
                continue;
            var sim = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
                oneMinuteCandles, bp.EntryTimeUtc, entryPrice,
                config.MaxHoldMinutes, config.TargetPercent, config.StopPercent, direction);
            if (sim.ExitReason == "InvalidEntry")
                continue;
            var net = NetForSim(sim, moderate, direction);
            var entry = sim.EntryTimeUtc;
            if (entry < trainEnd) train += net;
            else if (entry < validationEnd) val += net;
            else hold += net;
            nextAllowed = BinaryFirstGreater(intervalCandles, sim.ExitTimeUtc) + 1 + config.CooldownCandles;
        }

        return (train, val, hold, train + val + hold);
    }

    private static decimal SplitNet(
        List<DirectionalPoint> ordered,
        DirectionalRuleV3CostScenario scenario,
        LongShortDirection direction)
    {
        decimal sum = 0m;
        foreach (var pt in ordered)
            sum += NetFor(pt, scenario, direction);
        return sum;
    }

    private static decimal NetFor(DirectionalPoint pt, DirectionalRuleV3CostScenario scenario, LongShortDirection direction)
        => NetForSim(pt.Sim, scenario, direction);

    private static decimal NetForSim(DirectionalTradeSimulationResult sim, DirectionalRuleV3CostScenario scenario, LongShortDirection direction)
        => DirectionalRuleFuturesValidationV3CostModel.ComputeCostBreakdown(sim, direction, scenario).NetPnlQuote;

    public static List<DiscoveryBasePoint> BuildBasePoints(
        string interval,
        IReadOnlyList<KlineCandle> intervalCandles,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext,
        CancellationToken cancellationToken)
    {
        var points = new List<DiscoveryBasePoint>();
        if (intervalCandles.Count <= MarketRegimeForwardEdgeScanner.MinimumWarmupCandles + 2)
            return points;
        var stride = MarketRegimeForwardEdgeScanner.ResolveSamplingStride(interval);
        for (var i = MarketRegimeForwardEdgeScanner.MinimumWarmupCandles; i < intervalCandles.Count - 1; i += stride)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var signalCandle = intervalCandles[i];
            if (signalCandle.Close <= 0m)
                continue;
            var features = MarketRegimeForwardEdgeScanner.ComputeRegimeCandleFeatures(
                intervalCandles, i, btcContext, marketWideContext, signalCandle.OpenTimeUtc);
            var entryCandle = intervalCandles[i + 1];
            points.Add(new DiscoveryBasePoint
            {
                SignalIndex = i,
                SignalTimeUtc = signalCandle.OpenTimeUtc,
                EntryTimeUtc = entryCandle.OpenTimeUtc,
                EntryPriceNextOpen = entryCandle.Open,
                EntryPriceNextClose = entryCandle.Close,
                Features = features,
                HourOfDayUtc = entryCandle.OpenTimeUtc.Hour,
                DayOfWeek = entryCandle.OpenTimeUtc.DayOfWeek.ToString(),
                SessionBucket = MarketRegimeForwardEdgeScanner.ResolveSessionBucket(entryCandle.OpenTimeUtc.Hour)
            });
        }

        return points;
    }

    private static List<DirectionalPoint> BuildDirectionalPoints(
        IReadOnlyList<DiscoveryBasePoint> basePoints,
        IReadOnlyList<KlineCandle> intervalCandles,
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        LongShortDirection direction,
        DiscoveryRuleConfig config,
        DirectionalRuleV3CostScenario moderate)
    {
        var result = new List<DirectionalPoint>(basePoints.Count);
        foreach (var bp in basePoints)
        {
            var entryPrice = config.EntryMode == DirectionalRuleEntryMode.NextOpen
                ? bp.EntryPriceNextOpen
                : bp.EntryPriceNextClose;
            if (entryPrice <= 0m)
            {
                result.Add(new DirectionalPoint { Base = bp, Sim = Invalid(bp.EntryTimeUtc, entryPrice), Valid = false });
                continue;
            }

            var sim = DirectionalRuleFuturesSimulationV1Simulator.SimulateDirectionalTrade(
                oneMinuteCandles, bp.EntryTimeUtc, entryPrice,
                config.MaxHoldMinutes, config.TargetPercent, config.StopPercent, direction);
            var valid = sim.ExitReason != "InvalidEntry";
            result.Add(new DirectionalPoint
            {
                Base = bp,
                Sim = sim,
                Valid = valid,
                ModerateNet = valid ? NetForSim(sim, moderate, direction) : 0m,
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

    private static string ClassifyVerdict(
        bool allSplitsPositive,
        bool validationPositive,
        bool fullPositive,
        bool overfitWarning,
        bool tradeCountSufficient)
    {
        if (!tradeCountSufficient)
            return "InsufficientTrades";
        if (allSplitsPositive && fullPositive)
            return "SurvivesAllSplits";
        if (validationPositive && !allSplitsPositive)
            return "FailsHoldout";
        if (overfitWarning)
            return "OverfitWarning";
        return "FailsValidation";
    }
}
