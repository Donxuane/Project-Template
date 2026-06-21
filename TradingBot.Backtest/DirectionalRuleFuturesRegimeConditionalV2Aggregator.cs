namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesRegimeConditionalV2Aggregator
{
    public static IReadOnlyList<RegimeConditionalSummaryRow> BuildSummary(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        IReadOnlyList<RegimeConditionalFilter> filters,
        string costScenarioLabel)
    {
        var baseline = filters.First(f => string.Equals(f.Name, "Baseline", StringComparison.OrdinalIgnoreCase));
        var baselineTrades = trades.Where(baseline.Predicate).ToArray();
        var baselineMonthly = BuildMonthly(baselineTrades);
        var baselinePositiveRatio = baselineMonthly.Count == 0
            ? 0m
            : (decimal)baselineMonthly.Count(m => m.Positive) / baselineMonthly.Count;

        var rows = new List<RegimeConditionalSummaryRow>();
        foreach (var filter in filters)
        {
            var filtered = trades.Where(filter.Predicate).ToArray();
            rows.Add(BuildSummaryRow(filter, filtered, costScenarioLabel, baselinePositiveRatio));
        }

        return rows
            .OrderByDescending(r => r.PassesAllCriteria)
            .ThenByDescending(r => r.BothPeriodsViable)
            .ThenByDescending(r => r.Full365NetPnl)
            .ToArray();
    }

    public static RegimeConditionalSummaryRow BuildSummaryRow(
        RegimeConditionalFilter filter,
        IReadOnlyList<RegimeDriftDiagnosticTrade> filtered,
        string costScenarioLabel,
        decimal baselinePositiveRatio)
    {
        var older = filtered.Where(t => t.InOlder).ToArray();
        var recent90 = filtered.Where(t => t.InRecent90d).ToArray();
        var monthly = BuildMonthly(filtered);
        var positiveMonths = monthly.Count(m => m.Positive);
        var olderNet = older.Sum(t => t.NetPnlQuote);
        var recent90Net = recent90.Sum(t => t.NetPnlQuote);
        var full365Net = filtered.Sum(t => t.NetPnlQuote);

        var sparse = filtered.Count < DirectionalRuleFuturesRegimeConditionalV2Catalog.MinimumTotalTrades
                     || older.Length < DirectionalRuleFuturesRegimeConditionalV2Catalog.MinimumPeriodTrades
                     || recent90.Length < DirectionalRuleFuturesRegimeConditionalV2Catalog.MinimumPeriodTrades;

        var olderViable = IsViable(olderNet, older.Length);
        var recentViable = IsViable(recent90Net, recent90.Length);
        var bothViable = olderViable && recentViable;
        var full365Positive = full365Net >= 0m;
        var monthlyRatio = monthly.Count == 0 ? 0m : (decimal)positiveMonths / monthly.Count;
        var monthlyImproved = monthlyRatio > baselinePositiveRatio;

        var passes = !sparse && bothViable && full365Positive && monthlyImproved;

        return new RegimeConditionalSummaryRow
        {
            FilterName = filter.Name,
            FilterGroup = filter.FilterGroup,
            FilterDescription = filter.Description,
            CostScenarioLabel = costScenarioLabel,
            TradeCount = filtered.Count,
            TradeCountOlder = older.Length,
            TradeCountRecent = recent90.Length,
            OlderNetPnl = Math.Round(olderNet, 8),
            Recent30dNetPnl = Math.Round(filtered.Where(t => t.InRecent30d).Sum(t => t.NetPnlQuote), 8),
            Recent60dNetPnl = Math.Round(filtered.Where(t => t.InRecent60d).Sum(t => t.NetPnlQuote), 8),
            Recent90dNetPnl = Math.Round(recent90Net, 8),
            Full365NetPnl = Math.Round(full365Net, 8),
            TrainReferenceNetPnl = Math.Round(filtered.Where(t => t.InTrainReference).Sum(t => t.NetPnlQuote), 8),
            Holdout30dNetPnl = Math.Round(filtered.Where(t => t.InHoldout30d).Sum(t => t.NetPnlQuote), 8),
            OlderAvgNetPerTrade = older.Length == 0 ? null : Math.Round(olderNet / older.Length, 8),
            RecentAvgNetPerTrade = recent90.Length == 0 ? null : Math.Round(recent90Net / recent90.Length, 8),
            PositiveMonthsCount = positiveMonths,
            TotalMonthsCount = monthly.Count,
            MonthlyNetPnl = monthly,
            SparseWarning = sparse,
            OlderViable = olderViable,
            RecentViable = recentViable,
            BothPeriodsViable = bothViable,
            Full365Positive = full365Positive,
            MonthlyConsistencyImproved = monthlyImproved,
            PassesAllCriteria = passes,
            Verdict = ClassifyVerdict(filter.Name, sparse, bothViable, full365Positive, monthlyImproved, passes)
        };
    }

    public static IReadOnlyList<RegimeConditionalMonthlyEntry> BuildMonthly(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
        => trades
            .GroupBy(t => t.MonthKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var net = g.Sum(t => t.NetPnlQuote);
                return new RegimeConditionalMonthlyEntry(g.Key, g.Count(), Math.Round(net, 8), net >= 0m);
            })
            .ToArray();

    public static IReadOnlyList<RegimeConditionalCostSensitivityRow> BuildCostSensitivity(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        IReadOnlyList<RegimeConditionalFilter> filters,
        string costScenarioLabel)
    {
        var rows = new List<RegimeConditionalCostSensitivityRow>();
        foreach (var filter in filters)
        {
            var filtered = trades.Where(filter.Predicate).ToArray();
            var older = filtered.Where(t => t.InOlder).ToArray();
            var recent90 = filtered.Where(t => t.InRecent90d).ToArray();
            var olderNet = older.Sum(t => t.NetPnlQuote);
            var recent90Net = recent90.Sum(t => t.NetPnlQuote);
            var full365Net = filtered.Sum(t => t.NetPnlQuote);
            var olderViable = IsViable(olderNet, older.Length);
            var recentViable = IsViable(recent90Net, recent90.Length);
            var full365Positive = full365Net >= 0m;

            rows.Add(new RegimeConditionalCostSensitivityRow
            {
                FilterName = filter.Name,
                FilterGroup = filter.FilterGroup,
                CostScenarioLabel = costScenarioLabel,
                TradeCount = filtered.Length,
                TradeCountOlder = older.Length,
                TradeCountRecent = recent90.Length,
                OlderNetPnl = Math.Round(olderNet, 8),
                Recent90dNetPnl = Math.Round(recent90Net, 8),
                Full365NetPnl = Math.Round(full365Net, 8),
                OlderViable = olderViable,
                RecentViable = recentViable,
                Full365Positive = full365Positive,
                SurvivesScenario = olderViable && recentViable && full365Positive
            });
        }

        return rows;
    }

    public static IReadOnlyList<RegimeConditionalFilterImpactRow> BuildFilterImpact(
        IReadOnlyList<RegimeDriftDiagnosticTrade> trades,
        IReadOnlyList<RegimeConditionalFilter> filters)
    {
        var baseline = filters.First(f => string.Equals(f.Name, "Baseline", StringComparison.OrdinalIgnoreCase));
        var baseTrades = trades.Where(baseline.Predicate).ToArray();
        var baseFull = baseTrades.Sum(t => t.NetPnlQuote);
        var baseOlder = baseTrades.Where(t => t.InOlder).Sum(t => t.NetPnlQuote);
        var baseRecent = baseTrades.Where(t => t.InRecent90d).Sum(t => t.NetPnlQuote);
        var baseMonthly = BuildMonthly(baseTrades);
        var basePositiveMonths = baseMonthly.Count(m => m.Positive);

        var rows = new List<RegimeConditionalFilterImpactRow>();
        foreach (var filter in filters.Where(f => !string.Equals(f.Name, "Baseline", StringComparison.OrdinalIgnoreCase)))
        {
            var filtered = trades.Where(filter.Predicate).ToArray();
            var fFull = filtered.Sum(t => t.NetPnlQuote);
            var fOlder = filtered.Where(t => t.InOlder).Sum(t => t.NetPnlQuote);
            var fRecent = filtered.Where(t => t.InRecent90d).Sum(t => t.NetPnlQuote);
            var fMonthly = BuildMonthly(filtered);

            rows.Add(new RegimeConditionalFilterImpactRow
            {
                FilterName = filter.Name,
                FilterGroup = filter.FilterGroup,
                FilterDescription = filter.Description,
                BaselineTrades = baseTrades.Length,
                FilteredTrades = filtered.Length,
                TradeRetentionRate = baseTrades.Length == 0 ? 0m : Math.Round((decimal)filtered.Length / baseTrades.Length, 6),
                BaselineFull365NetPnl = Math.Round(baseFull, 8),
                FilteredFull365NetPnl = Math.Round(fFull, 8),
                Full365Delta = Math.Round(fFull - baseFull, 8),
                BaselineOlderNetPnl = Math.Round(baseOlder, 8),
                FilteredOlderNetPnl = Math.Round(fOlder, 8),
                OlderDelta = Math.Round(fOlder - baseOlder, 8),
                BaselineRecent90dNetPnl = Math.Round(baseRecent, 8),
                FilteredRecent90dNetPnl = Math.Round(fRecent, 8),
                Recent90dDelta = Math.Round(fRecent - baseRecent, 8),
                BaselinePositiveMonths = basePositiveMonths,
                FilteredPositiveMonths = fMonthly.Count(m => m.Positive),
                TotalMonths = fMonthly.Count
            });
        }

        return rows
            .OrderByDescending(r => r.FilteredFull365NetPnl)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        IReadOnlyList<RegimeConditionalSummaryRow> summary,
        IReadOnlyList<RegimeConditionalCostSensitivityRow> costSensitivity,
        int totalTrades,
        DateTime dataStartUtc,
        DateTime dataEndUtc)
    {
        var baseline = summary.FirstOrDefault(s => string.Equals(s.FilterName, "Baseline", StringComparison.OrdinalIgnoreCase));
        var qualifying = summary.Where(s => s.PassesAllCriteria).ToArray();
        var bestBoth = summary
            .Where(s => !string.Equals(s.FilterName, "Baseline", StringComparison.OrdinalIgnoreCase) && s.BothPeriodsViable && !s.SparseWarning)
            .OrderByDescending(s => s.Full365NetPnl)
            .FirstOrDefault();
        var btcMomentum = summary
            .Where(s => s.FilterGroup == "BtcMomentum" && !s.SparseWarning)
            .OrderByDescending(s => s.Full365NetPnl)
            .FirstOrDefault();
        var answers = new List<ReachabilityResearchAnswer>();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Can BTC momentum activation make Rule01 short positive in both older and recent periods?",
            Answer = btcMomentum is null
                ? "No BTC-momentum filter produced sufficient samples."
                : $"Best BTC-momentum filter {btcMomentum.FilterName}: older={btcMomentum.OlderNetPnl:F2} recent90d={btcMomentum.Recent90dNetPnl:F2} full365={btcMomentum.Full365NetPnl:F2} (older viable={btcMomentum.OlderViable}, recent viable={btcMomentum.RecentViable}).",
            Verdict = btcMomentum?.BothPeriodsViable == true && btcMomentum.Full365Positive ? "BtcMomentumViable" : "BtcMomentumInsufficient",
            Details = new Dictionary<string, object?> { ["bestBtcMomentumFilter"] = btcMomentum }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is the recent edge explained by BTC-up context or only by one recent cluster?",
            Answer = bestBoth is not null
                ? $"Filter {bestBoth.FilterName} keeps both periods viable, suggesting a regime (not single-cluster) explanation."
                : $"No filter keeps older viable; recent edge concentrated in recent window only (baseline older={baseline?.OlderNetPnl:F2} vs recent90d={baseline?.Recent90dNetPnl:F2}).",
            Verdict = bestBoth is not null ? "RegimeExplained" : "RecentClusterOnly",
            Details = null
        });

        var sampleOk = summary.Any(s => !string.Equals(s.FilterName, "Baseline", StringComparison.OrdinalIgnoreCase) && !s.SparseWarning);
        var sampleOkFilters = summary
            .Where(s => !string.Equals(s.FilterName, "Baseline", StringComparison.OrdinalIgnoreCase)
                        && s.TradeCount >= DirectionalRuleFuturesRegimeConditionalV2Catalog.MinimumTotalTrades
                        && s.TradeCountOlder >= DirectionalRuleFuturesRegimeConditionalV2Catalog.MinimumPeriodTrades
                        && s.TradeCountRecent >= DirectionalRuleFuturesRegimeConditionalV2Catalog.MinimumPeriodTrades)
            .Select(s => s.FilterName)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any filter produce at least 50 trades and at least 15 trades in both older and recent periods?",
            Answer = sampleOkFilters.Length > 0
                ? $"{sampleOkFilters.Length} filter(s) meet sample thresholds: {string.Join(", ", sampleOkFilters.Take(8))}."
                : "No filter meets the 50-total / 15-per-period sample thresholds.",
            Verdict = sampleOk ? "SampleSufficient" : "SparseWarning",
            Details = new Dictionary<string, object?> { ["filtersMeetingSampleThreshold"] = sampleOkFilters }
        });

        var baselinePositiveMonths = baseline?.PositiveMonthsCount ?? 0;
        var improved = summary
            .Where(s => !string.Equals(s.FilterName, "Baseline", StringComparison.OrdinalIgnoreCase) && s.MonthlyConsistencyImproved && !s.SparseWarning)
            .OrderByDescending(s => s.PositiveMonthsCount)
            .FirstOrDefault();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any filter improve monthly consistency above the baseline positive-month ratio?",
            Answer = improved is not null
                ? $"Filter {improved.FilterName}: {improved.PositiveMonthsCount}/{improved.TotalMonthsCount} positive months vs baseline {baselinePositiveMonths}/{baseline?.TotalMonthsCount}."
                : $"No filter improved monthly consistency above baseline {baselinePositiveMonths}/{baseline?.TotalMonthsCount}.",
            Verdict = improved is not null ? "ConsistencyImproved" : "NoConsistencyGain",
            Details = null
        });

        var survivesModerate = costSensitivity
            .Where(c => c.CostScenarioLabel is "futures-moderate" or "futures-moderate-latency-002" && c.SurvivesScenario
                        && !string.Equals(c.FilterName, "Baseline", StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.FilterName, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 2)
            .Select(g => g.Key)
            .ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does any filter survive futures-moderate and moderate+0.02 slippage?",
            Answer = survivesModerate.Length > 0
                ? $"{survivesModerate.Length} filter(s) survive both moderate and moderate+0.02: {string.Join(", ", survivesModerate)}."
                : "No filter survives both futures-moderate and moderate+0.02 slippage in both periods.",
            Verdict = survivesModerate.Length > 0 ? "SurvivesModerateAndSlippage" : "FailsCostStress",
            Details = new Dictionary<string, object?> { ["survivingFilters"] = survivesModerate }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If no filter survives both periods, should Rule01 short be parked?",
            Answer = qualifying.Length > 0
                ? $"{qualifying.Length} filter(s) pass all success criteria: {string.Join(", ", qualifying.Select(q => q.FilterName).Take(8))}. Recommend research-only continuation; still no live recommendation."
                : "No filter passes all success criteria (entry-time-only, >=50 trades, >=15/period, both-period viable, full365 positive, improved monthly consistency). Mark Rule01 short as recent-regime-only and park it.",
            Verdict = qualifying.Length > 0 ? "WorthFilteredResearch" : "ParkRecentRegimeOnly",
            Details = new Dictionary<string, object?>
            {
                ["totalTrades"] = totalTrades,
                ["qualifyingFilterCount"] = qualifying.Length,
                ["dataStartUtc"] = dataStartUtc,
                ["dataEndUtc"] = dataEndUtc
            }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Overall verdict: recommend live Futures from regime-conditional activation analysis?",
            Answer = "Do not recommend live Futures. Backtest-only diagnostic; activation filters are research-only even when they pass split validation.",
            Verdict = "DoNotRecommendLiveFutures",
            Details = new Dictionary<string, object?> { ["backtestOnly"] = true, ["liveFuturesRecommended"] = false }
        });

        return answers;
    }

    private static bool IsViable(decimal net, int count)
    {
        if (count == 0)
            return false;
        if (net >= 0m)
            return true;
        return net / count >= DirectionalRuleFuturesRegimeConditionalV2Catalog.NearBreakevenAvgPerTrade;
    }

    private static string ClassifyVerdict(
        string filterName,
        bool sparse,
        bool bothViable,
        bool full365Positive,
        bool monthlyImproved,
        bool passes)
    {
        if (string.Equals(filterName, "Baseline", StringComparison.OrdinalIgnoreCase))
            return "Baseline";
        if (sparse)
            return "SparseWarning";
        if (passes)
            return "PassesAllCriteria";
        if (bothViable && full365Positive && !monthlyImproved)
            return "ViableButNoConsistencyGain";
        if (bothViable && !full365Positive)
            return "ViablePeriodsButFull365Negative";
        if (!bothViable && full365Positive)
            return "Full365PositiveButPeriodSplitFails";
        return "FailsSplitValidation";
    }
}
