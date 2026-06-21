using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV31Aggregator
{
    private static readonly string[] LongHistoryWindowLabels = ["30d", "60d", "90d", "120d", "180d", "270d", "365d"];
    private static readonly string[] CrossSymbolWindowLabels = ["30d", "60d", "90d", "120d", "180d"];

    public static IReadOnlyDictionary<string, string> LabelDefinitions { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SymbolPositive"] = "True when NetPnlQuote for the profile/window/cost row is >= 0.",
            ["AllWindowsPositive"] = "True when every configured rolling window present in the run has net >= 0.",
            ["HoldoutPositive"] = "True when holdout30d net >= 0, or 30d net when holdout30d is absent.",
            ["StressPositive"] = "True for stress/latency scenarios when aggregate rolling-window net is >= 0.",
            ["StressAllWindowsPositive"] = "True for stress/latency scenarios when all present rolling windows are >= 0.",
            ["TradeCountSufficient"] = "True when executed trades >= 50.",
            ["LongHistoryPositive"] = "True when longest available window net (180d/270d/365d) is >= 0 under futures-moderate.",
            ["OverfitWarning"] = "True when holdout/trainReference is negative while shorter windows are positive, or long window turns negative.",
            ["SameRuleGeneralizesAcrossSymbols"] = "True when >=2 non-BNB symbols pass moderate aggregate with TradeCountSufficient and AllWindowsPositive."
        };

    public static IReadOnlyList<DirectionalRuleV31SummaryRow> ApplyCrossWindowLabels(
        IReadOnlyList<DirectionalRuleV31SummaryRow> summaries,
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness)
    {
        var robustByKey = windowRobustness.ToDictionary(
            r => $"{r.ProfileKey}|{r.CostScenarioLabel}",
            StringComparer.OrdinalIgnoreCase);

        return summaries.Select(row =>
        {
            robustByKey.TryGetValue($"{row.ProfileKey}|{row.CostScenarioLabel}", out var robust);
            return row with
            {
                AllWindowsPositive = robust?.AllWindowsPositive ?? false,
                HoldoutPositive = robust?.HoldoutPositive ?? false,
                StressPositive = robust?.StressPositive ?? false,
                StressAllWindowsPositive = robust?.StressAllWindowsPositive ?? false,
                LongHistoryPositive = robust?.LongHistoryPositive ?? false,
                OverfitWarning = robust?.OverfitWarning ?? false,
                SymbolPositive = row.NetPnlQuote >= 0m,
                TradeCountSufficient = row.ExecutedTrades >= DirectionalRuleFuturesValidationV31Catalog.MinimumMeaningfulTrades
            };
        }).ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> BuildWindowRobustness(
        IReadOnlyList<DirectionalRuleV31SummaryRow> summaries)
    {
        return summaries
            .GroupBy(s => $"{s.ProfileKey}|{s.CostScenarioLabel}", StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                var labels = first.ValidationTrack == DirectionalRuleV31ValidationTrack.BestBnbLongHistory
                    ? LongHistoryWindowLabels
                    : CrossSymbolWindowLabels;
                decimal Net(string label) => g.FirstOrDefault(x => x.WindowLabel == label)?.NetPnlQuote ?? 0m;
                int Trades(string label) => g.FirstOrDefault(x => x.WindowLabel == label)?.ExecutedTrades ?? 0;
                var rollingNets = labels.Where(l => g.Any(x => x.WindowLabel == l)).Select(Net).ToArray();
                var allRollingPositive = rollingNets.Length > 0 && rollingNets.All(n => n >= 0m);
                var holdoutNet = Net("holdout30d");
                if (holdoutNet == 0m && g.Any(x => x.WindowLabel == "30d"))
                    holdoutNet = Net("30d");
                var trainNet = Net("trainReference");
                var isStress = IsStressScenario(first.CostScenarioLabel);
                var aggregateRolling = rollingNets.Sum();
                var referenceTrades = ResolveReferenceTradeCount(BuildTradeRow(Trades));
                var longHistoryNet = Net("365d") != 0m ? Net("365d")
                    : Net("270d") != 0m ? Net("270d")
                    : Net("180d");
                var overfit = (holdoutNet < 0m && aggregateRolling > 0m)
                              || (trainNet < 0m && holdoutNet > 0m && aggregateRolling > 0m)
                              || (longHistoryNet < 0m && Net("90d") > 0m);

                var row = new DirectionalRuleV31WindowRobustnessRow
                {
                    ProfileKey = first.ProfileKey,
                    VariantLabel = first.VariantLabel,
                    ValidationTrack = first.ValidationTrack,
                    IsBestBnbCandidate = first.IsBestBnbCandidate,
                    Symbol = first.Symbol,
                    Interval = first.Interval,
                    EntryMode = first.EntryMode,
                    CooldownCandlesAfterExit = first.CooldownCandlesAfterExit,
                    TargetPercent = first.TargetPercent,
                    StopPercent = first.StopPercent,
                    MaxHoldMinutes = first.MaxHoldMinutes,
                    CostScenarioLabel = first.CostScenarioLabel,
                    Window30dTrades = Trades("30d"),
                    Window60dTrades = Trades("60d"),
                    Window90dTrades = Trades("90d"),
                    Window120dTrades = Trades("120d"),
                    Window180dTrades = Trades("180d"),
                    Window270dTrades = Trades("270d"),
                    Window365dTrades = Trades("365d"),
                    Holdout30dTrades = Trades("holdout30d") > 0 ? Trades("holdout30d") : Trades("30d"),
                    TrainReferenceTrades = Trades("trainReference"),
                    Window30dNetPnl = Net("30d"),
                    Window60dNetPnl = Net("60d"),
                    Window90dNetPnl = Net("90d"),
                    Window120dNetPnl = Net("120d"),
                    Window180dNetPnl = Net("180d"),
                    Window270dNetPnl = Net("270d"),
                    Window365dNetPnl = Net("365d"),
                    Holdout30dNetPnl = holdoutNet,
                    TrainReferenceNetPnl = trainNet,
                    AggregateNetPnl = aggregateRolling,
                    SymbolPositive = aggregateRolling >= 0m,
                    AllWindowsPositive = allRollingPositive,
                    HoldoutPositive = holdoutNet >= 0m,
                    StressPositive = isStress && aggregateRolling >= 0m,
                    StressAllWindowsPositive = isStress && allRollingPositive,
                    TradeCountSufficient = referenceTrades >= DirectionalRuleFuturesValidationV31Catalog.MinimumMeaningfulTrades,
                    LongHistoryPositive = longHistoryNet >= 0m,
                    OverfitWarning = overfit
                };
                return row with
                {
                    RobustnessVerdict = ClassifyRobustnessVerdict(row, referenceTrades)
                };
            })
            .ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV31CostSensitivityRow> BuildCostSensitivity(
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness)
    {
        var scenarios = DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios()
            .ToDictionary(s => s.Label, StringComparer.OrdinalIgnoreCase);

        return windowRobustness.Select(row =>
        {
            scenarios.TryGetValue(row.CostScenarioLabel, out var scenario);
            scenario ??= DirectionalRuleFuturesValidationV3CostModel.BuildValidationScenarios().First();
            var trades = ResolveReferenceTradeCount(row);
            var avg = trades == 0 ? (decimal?)null : Math.Round(row.AggregateNetPnl / trades, 8);
            return new DirectionalRuleV31CostSensitivityRow
            {
                ProfileKey = row.ProfileKey,
                VariantLabel = row.VariantLabel,
                ValidationTrack = row.ValidationTrack,
                IsBestBnbCandidate = row.IsBestBnbCandidate,
                Symbol = row.Symbol,
                Interval = row.Interval,
                CostScenarioLabel = row.CostScenarioLabel,
                RoundTripCostPercent = DirectionalRuleFuturesValidationV3CostModel.EstimateRoundTripCostPercent(scenario),
                ExtraAdverseSlippagePercentPerSide = scenario.ExtraAdverseSlippagePercentPerSide,
                TradeCount = trades,
                NetPnlQuote = row.AggregateNetPnl,
                AvgNetPnlPerTrade = avg,
                SymbolPositive = row.SymbolPositive,
                StressPositive = row.StressPositive,
                Verdict = ClassifySummaryVerdict(trades, row.AggregateNetPnl, avg ?? 0m)
            };
        }).ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV31DrawdownRow> ApplyWorstWindowNet(
        IReadOnlyList<DirectionalRuleV31DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness)
    {
        var worstByProfile = windowRobustness
            .Where(r => string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                r => r.ProfileKey,
                r =>
                {
                    var nets = new[]
                        {
                            r.Window30dNetPnl, r.Window60dNetPnl, r.Window90dNetPnl,
                            r.Window120dNetPnl, r.Window180dNetPnl, r.Window270dNetPnl, r.Window365dNetPnl
                        }
                        .Where(n => n != 0m)
                        .ToArray();
                    return nets.Length == 0 ? 0m : nets.Min();
                },
                StringComparer.OrdinalIgnoreCase);

        return drawdown.Select(row =>
        {
            worstByProfile.TryGetValue(row.ProfileKey, out var worst);
            return row with { WorstWindowNet = worst };
        }).ToArray();
    }

    public static bool SameRuleGeneralizesAcrossSymbols(IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness)
    {
        var passing = windowRobustness
            .Where(r => r.ValidationTrack == DirectionalRuleV31ValidationTrack.CrossSymbol
                        && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)
                        && r.TradeCountSufficient
                        && r.AllWindowsPositive
                        && r.SymbolPositive
                        && r.Symbol != TradingSymbol.BNBUSDT)
            .Select(r => r.Symbol)
            .Distinct()
            .Count();
        return passing >= 2;
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildGeneralizationAnswers(
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness,
        IReadOnlyList<DirectionalRuleV31DrawdownRow> drawdown,
        IReadOnlyList<DirectionalRuleV31MonthlyWeeklyPnlRow> monthlyWeekly,
        int bootstrapDays,
        int historyDaysAvailable,
        long expandedTradeRowCount,
        long skippedSignalCount)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var bestBnbModerate = windowRobustness.FirstOrDefault(r =>
            r.IsBestBnbCandidate
            && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase));
        var bestBnbDd = drawdown.FirstOrDefault(d =>
            d.IsBestBnbCandidate
            && string.Equals(d.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)
            && d.WindowLabel is "365d" or "270d" or "180d" or "90d");
        var generalizes = SameRuleGeneralizesAcrossSymbols(windowRobustness);

        answers.Add(Answer(
            "Does the best BNB 5m Rule01 short survive longer history?",
            bestBnbModerate is { TradeCountSufficient: true, AllWindowsPositive: true, LongHistoryPositive: true }
                ? $"Yes — moderate aggregate={bestBnbModerate.AggregateNetPnl:F2}, 180d={bestBnbModerate.Window180dNetPnl:F2}, 270d={bestBnbModerate.Window270dNetPnl:F2}, 365d={bestBnbModerate.Window365dNetPnl:F2}, trades={ResolveReferenceTradeCount(bestBnbModerate)}."
                : bestBnbModerate is null
                    ? "Best BNB moderate row missing."
                    : $"Mixed/fail — aggregate={bestBnbModerate.AggregateNetPnl:F2}, longHistory={bestBnbModerate.LongHistoryPositive}, overfit={bestBnbModerate.OverfitWarning}.",
            bestBnbModerate is { TradeCountSufficient: true, AllWindowsPositive: true, LongHistoryPositive: true, HoldoutPositive: true }
                ? "LongHistorySurvives"
                : bestBnbModerate is { OverfitWarning: true }
                    ? "OverfitWarning"
                    : "LongHistoryFails",
            new Dictionary<string, object?> { ["bestBnbModerate"] = bestBnbModerate, ["bootstrapDays"] = bootstrapDays }));

        var monthly = monthlyWeekly.Where(r => r.IsBestBnbCandidate && r.PeriodType == "Month"
            && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)).ToArray();
        var positiveMonths = monthly.Count(m => m.NetPnlQuote >= 0m);
        answers.Add(Answer(
            "Is the edge stable by month/week or concentrated in a few clusters?",
            monthly.Length == 0
                ? "No monthly buckets."
                : $"Months positive={positiveMonths}/{monthly.Length}; sample: {string.Join(", ", monthly.Take(6).Select(m => $"{m.PeriodKey}={m.NetPnlQuote:F1}({m.TradeCount})"))}.",
            positiveMonths >= monthly.Length * 2 / 3 ? "StableByMonth" : "ConcentratedClusters",
            new Dictionary<string, object?> { ["monthly"] = monthly }));

        var crossModerate = windowRobustness
            .Where(r => r.ValidationTrack == DirectionalRuleV31ValidationTrack.CrossSymbol
                        && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.Symbol)
            .Select(g => new
            {
                Symbol = g.Key,
                Best = g.OrderByDescending(x => x.AggregateNetPnl).First()
            })
            .OrderByDescending(x => x.Best.AggregateNetPnl)
            .ToArray();
        answers.Add(Answer(
            "Does Rule01 short generalize to ETH/SOL/BTC, or is it BNB-specific?",
            string.Join("; ", crossModerate.Select(x =>
                $"{x.Symbol}: best={x.Best.VariantLabel} net={x.Best.AggregateNetPnl:F2} allWin={x.Best.AllWindowsPositive} trades={ResolveReferenceTradeCount(x.Best)}")),
            generalizes ? "SameRuleGeneralizesAcrossSymbols" : "BnbSpecificResearchCandidate",
            new Dictionary<string, object?> { ["crossModerate"] = crossModerate, ["generalizes"] = generalizes }));

        AddVariantComparisonAnswers(answers, windowRobustness, bestBnbModerate);
        AddStressAnswers(answers, windowRobustness, bestBnbModerate);

        answers.Add(Answer(
            "Is drawdown acceptable in simulation?",
            bestBnbDd is null
                ? "No drawdown row."
                : $"maxDD={bestBnbDd.MaxDrawdownQuote:F2}, maxLossStreak={bestBnbDd.MaxConsecutiveLosses}, worstTrade={bestBnbDd.WorstTradeNet:F2}, avgHold={bestBnbDd.AverageHoldMinutes:F1}m, targetRate={bestBnbDd.ProfitTargetRate:P0}.",
            bestBnbDd is { MaxDrawdownQuote: < 90m, MaxConsecutiveLosses: < 12 }
                ? "DrawdownAcceptable"
                : "DrawdownElevated",
            new Dictionary<string, object?> { ["drawdown"] = bestBnbDd }));

        answers.Add(Answer(
            "Is this strong enough for future paper/sandbox planning, while still not live?",
            bestBnbModerate is { TradeCountSufficient: true, AllWindowsPositive: true, HoldoutPositive: true, LongHistoryPositive: true }
                ? "Candidate is strong enough for future paper/sandbox planning only. Not live-ready."
                : "Insufficient for paper/sandbox planning until longer-history/generalization concerns are resolved.",
            bestBnbModerate is { TradeCountSufficient: true, AllWindowsPositive: true, HoldoutPositive: true }
                ? "PaperSandboxCandidate"
                : "ResearchOnly",
            new Dictionary<string, object?>
            {
                ["expandedTradeRowCount"] = expandedTradeRowCount,
                ["skippedSignals"] = skippedSignalCount,
                ["historyDaysAvailable"] = historyDaysAvailable,
                ["liveFuturesRecommended"] = false,
                ["labelDefinitions"] = LabelDefinitions
            }));

        answers.Add(Answer(
            "Overall verdict: recommend live Futures from this validation?",
            "Do not recommend live Futures from DirectionalRuleFuturesValidationV31. Backtest-only research branch.",
            "DoNotRecommendLiveFutures",
            new Dictionary<string, object?> { ["backtestOnly"] = true }));

        return answers;
    }

    internal static string TradeBucketKey(DirectionalRuleV31TradeRecord t)
        => $"{t.ProfileKey}|{t.WindowLabel}|{t.CostScenarioLabel}";

    public static int ResolveReferenceTradeCount(DirectionalRuleV31WindowRobustnessRow row)
    {
        if (row.Window365dTrades > 0) return row.Window365dTrades;
        if (row.Window270dTrades > 0) return row.Window270dTrades;
        if (row.Window180dTrades > 0) return row.Window180dTrades;
        if (row.Window120dTrades > 0) return row.Window120dTrades;
        if (row.Window90dTrades > 0) return row.Window90dTrades;
        if (row.Window60dTrades > 0) return row.Window60dTrades;
        if (row.Window30dTrades > 0) return row.Window30dTrades;
        if (row.Holdout30dTrades > 0) return row.Holdout30dTrades;
        return row.TrainReferenceTrades;
    }

    internal static string ClassifySummaryVerdict(int tradeCount, decimal netPnl, decimal avgNet)
    {
        if (tradeCount == 0)
            return "InsufficientSamples";
        if (tradeCount < DirectionalRuleFuturesValidationV31Catalog.MinimumMeaningfulTrades)
            return netPnl >= 0m ? "TradeCountBelowThreshold" : "NegativeLowSample";
        if (netPnl >= 0m)
            return "NonNegative";
        if (avgNet >= -0.0005m)
            return "NearBreakeven";
        return "Negative";
    }

    private static void AddVariantComparisonAnswers(
        List<ReachabilityResearchAnswer> answers,
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness,
        DirectionalRuleV31WindowRobustnessRow? bestBnb)
    {
        var bnbCross = windowRobustness.Where(r =>
            r.ValidationTrack == DirectionalRuleV31ValidationTrack.CrossSymbol
            && r.Symbol == TradingSymbol.BNBUSDT
            && r.Interval == "5m"
            && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase)).ToArray();

        decimal BestNet(Func<DirectionalRuleV31WindowRobustnessRow, bool> pred)
            => bnbCross.Where(pred).Select(r => r.AggregateNetPnl).DefaultIfEmpty(decimal.MinValue).Max();

        var t175 = BestNet(r => r.TargetPercent == 1.75m);
        var t150 = BestNet(r => r.TargetPercent == 1.50m);
        answers.Add(Answer(
            "Does 1.75/1.00 remain better than 1.50/1.00?",
            $"Best 1.75/1.00 net={t175:F2}, best 1.50/1.00 net={t150:F2}.",
            t175 >= t150 ? "Target175Better" : "Target150Better",
            null));

        var h4 = BestNet(r => r.MaxHoldMinutes == 240);
        var h8 = BestNet(r => r.MaxHoldMinutes == 480);
        answers.Add(Answer(
            "Does 4h remain better than 8h?",
            $"Best 4h net={h4:F2}, best 8h net={h8:F2}.",
            h4 >= h8 ? "FourHourHoldBetter" : "EightHourHoldBetter",
            null));

        var cd6 = BestNet(r => r.CooldownCandlesAfterExit == 6);
        var cd3 = BestNet(r => r.CooldownCandlesAfterExit == 3);
        answers.Add(Answer(
            "Does cooldown 6 remain better than cooldown 3?",
            $"Best cd6 net={cd6:F2}, best cd3 net={cd3:F2}.",
            cd6 >= cd3 ? "Cooldown6Better" : "Cooldown3Better",
            null));
    }

    private static void AddStressAnswers(
        List<ReachabilityResearchAnswer> answers,
        IReadOnlyList<DirectionalRuleV31WindowRobustnessRow> windowRobustness,
        DirectionalRuleV31WindowRobustnessRow? bestBnb)
    {
        foreach (var (label, question) in new[]
                 {
                     ("futures-stress", "Does the strategy survive futures-stress?"),
                     ("futures-stress-plus", "Does it survive stress-plus?")
                 })
        {
            var row = windowRobustness.FirstOrDefault(r =>
                r.IsBestBnbCandidate
                && string.Equals(r.CostScenarioLabel, label, StringComparison.OrdinalIgnoreCase));
            answers.Add(Answer(
                question,
                row is null ? $"No {label} row." : $"aggregate={row.AggregateNetPnl:F2}, allWin={row.AllWindowsPositive}.",
                row is { SymbolPositive: true } ? $"{label}Survives" : $"{label}Fails",
                new Dictionary<string, object?> { ["row"] = row }));
        }

        var latency = windowRobustness.Where(r =>
            r.IsBestBnbCandidate
            && r.CostScenarioLabel.Contains("latency", StringComparison.OrdinalIgnoreCase)).ToArray();
        answers.Add(Answer(
            "Does it survive high latency/slippage?",
            string.Join("; ", latency.Select(r => $"{r.CostScenarioLabel}={r.AggregateNetPnl:F2}")),
            latency.Any(r => r.CostScenarioLabel.Contains("moderate-latency-002", StringComparison.OrdinalIgnoreCase) && r.SymbolPositive)
                ? "ModerateLatency002Survives"
                : "LatencyStressFails",
            new Dictionary<string, object?> { ["latencyRows"] = latency }));
    }

    private static ReachabilityResearchAnswer Answer(
        string question,
        string answer,
        string verdict,
        Dictionary<string, object?>? details)
        => new() { Question = question, Answer = answer, Verdict = verdict, Details = details };

    private static string ClassifyRobustnessVerdict(DirectionalRuleV31WindowRobustnessRow row, int tradeCount)
    {
        if (tradeCount == 0)
            return "InsufficientSamples";
        if (tradeCount < DirectionalRuleFuturesValidationV31Catalog.MinimumMeaningfulTrades)
            return row.SymbolPositive ? "TradeCountBelowThreshold" : "NegativeLowSample";
        if (row.OverfitWarning)
            return "OverfitWarning";
        if (row.AllWindowsPositive && row.HoldoutPositive)
            return "AllWindowsAndHoldoutPositive";
        if (row.AllWindowsPositive)
            return "AllWindowsPositive";
        if (row.HoldoutPositive && row.SymbolPositive)
            return "HoldoutPositive";
        if (row.SymbolPositive)
            return "SymbolPositive";
        return "Negative";
    }

    private static bool IsStressScenario(string label)
        => label.Contains("stress", StringComparison.OrdinalIgnoreCase)
           || label.Contains("latency", StringComparison.OrdinalIgnoreCase);

    private static DirectionalRuleV31WindowRobustnessRow BuildTradeRow(Func<string, int> trades)
        => new()
        {
            Window365dTrades = trades("365d"),
            Window270dTrades = trades("270d"),
            Window180dTrades = trades("180d"),
            Window120dTrades = trades("120d"),
            Window90dTrades = trades("90d"),
            Window60dTrades = trades("60d"),
            Window30dTrades = trades("30d"),
            Holdout30dTrades = trades("holdout30d") > 0 ? trades("holdout30d") : trades("30d"),
            TrainReferenceTrades = trades("trainReference")
        };
}
