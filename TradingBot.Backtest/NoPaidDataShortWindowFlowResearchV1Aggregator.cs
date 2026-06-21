namespace TradingBot.Backtest;

public static class NoPaidDataShortWindowFlowResearchV1Aggregator
{
    public static ShortWindowSummaryRow BuildSummaryRow(
        ShortWindowActivationConfig config,
        IReadOnlyList<RegimeDriftDiagnosticTrade> taken,
        IReadOnlyList<RegimeDriftDiagnosticTrade> baseline,
        IReadOnlyList<ShortWindowPeriodRow> periods,
        int clusterCount,
        int flowUnavailableCount,
        string costScenario)
    {
        var net = taken.Sum(t => t.NetPnlQuote);
        var baselineNet = baseline.Sum(t => t.NetPnlQuote);
        var risk = ComputeRisk(taken);
        var activated = periods.Where(p => p.Activated).ToArray();
        var positive = activated.Count(p => p.NetInActivationWindow > 0m);
        var sparseActivations = activated.Count(p => p.SparseLookback);

        var meetsMinTrades = taken.Count >= NoPaidDataShortWindowFlowResearchV1Catalog.MinimumExecutedTrades;
        var netPositive = net > 0m;
        var multipleClusters = clusterCount >= 2;

        return new ShortWindowSummaryRow
        {
            ActivationRuleName = config.ActivationRuleName,
            PerfCondition = config.PerfCondition.ToString(),
            FlowCondition = config.FlowCondition.ToString(),
            Description = config.Description,
            CheckpointFrequencyHours = config.CheckpointFrequencyHours,
            LookbackDays = config.LookbackDays,
            ActivationPeriodHours = config.ActivationPeriodHours,
            MinLookbackTrades = config.MinLookbackTrades,
            ProfitFactorThreshold = config.ProfitFactorThreshold,
            CostScenario = costScenario,
            TotalTrades = taken.Count,
            BaselineTrades = baseline.Count,
            NetPnlQuote = Math.Round(net, 8),
            BaselineNetPnlQuote = Math.Round(baselineNet, 8),
            Delta = Math.Round(net - baselineNet, 8),
            WinRate = taken.Count == 0 ? 0m : Math.Round((decimal)taken.Count(t => t.NetPnlQuote > 0m) / taken.Count, 6),
            ProfitFactor = Math.Round(risk.ProfitFactor, 6),
            MaxDrawdownQuote = Math.Round(risk.MaxDrawdown, 8),
            MaxConsecutiveLosses = risk.MaxConsecutiveLosses,
            CheckpointCount = periods.Count,
            ActivatedPeriodCount = activated.Length,
            PositivePeriodCount = positive,
            PositivePeriodRate = activated.Length == 0 ? 0m : Math.Round((decimal)positive / activated.Length, 6),
            ActivationClusterCount = clusterCount,
            SparseActivationCount = sparseActivations,
            FlowUnavailableCheckpointCount = flowUnavailableCount,
            MeetsMinExecutedTrades = meetsMinTrades,
            SparseFlagged = !meetsMinTrades,
            NetPositive = netPositive,
            Latency002NetPnl = null,
            SurvivesModerateSlippage002 = false,
            MultipleClusters = multipleClusters,
            // Finalized in the application after the latency-002 rerun.
            PassesSuccessCriteria = false,
            Verdict = "PendingCostCheck"
        };
    }

    public static ShortWindowSummaryRow FinalizeSummary(ShortWindowSummaryRow row, decimal? latency002Net)
    {
        var survives = latency002Net.HasValue
                       && (latency002Net.Value > 0m
                           || (row.NetPositive && latency002Net.Value >= NoPaidDataShortWindowFlowResearchV1Catalog.NotDestroyedFloorQuote));
        var isBaseline = string.Equals(row.PerfCondition, nameof(ShortWindowPerfCondition.AlwaysOn), StringComparison.OrdinalIgnoreCase);
        var passes = !isBaseline
                     && row.MeetsMinExecutedTrades
                     && row.NetPositive
                     && survives
                     && row.MultipleClusters;

        return row with
        {
            Latency002NetPnl = latency002Net.HasValue ? Math.Round(latency002Net.Value, 8) : null,
            SurvivesModerateSlippage002 = survives,
            PassesSuccessCriteria = passes,
            Verdict = ClassifyVerdict(row, isBaseline, passes, survives, latency002Net.HasValue)
        };
    }

    private static string ClassifyVerdict(
        ShortWindowSummaryRow row, bool isBaseline, bool passes, bool survives, bool costChecked)
    {
        if (isBaseline)
            return "BaselineReference";
        if (passes)
            return "PassesSuccessCriteria";
        if (row.TotalTrades == 0)
            return "NoTradesActivated";
        if (!row.MeetsMinExecutedTrades)
            return row.NetPositive ? "SparseButPositive" : "SparseTrades";
        if (!row.NetPositive)
            return "NetNegative";
        if (!costChecked)
            return "NotCostChecked";
        if (!survives)
            return "DestroyedByModerateSlippage";
        if (!row.MultipleClusters)
            return "SingleClusterOnly";
        return "FailsOther";
    }

    public static IReadOnlyList<ShortWindowCostSensitivityRow> BuildCostSensitivity(
        ShortWindowActivationConfig config,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        BtcContextIndex btcContext,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        DateTime dataEndUtc,
        ShortWindowFlowFeatureIndex flowIndex)
    {
        var rows = new List<ShortWindowCostSensitivityRow>();
        foreach (var scenario in NoPaidDataShortWindowFlowResearchV1Catalog.CostStressScenarios)
        {
            var costed = MapCostScenario(baseTrades, scenario, btcContext, dataEndUtc);
            var sim = NoPaidDataShortWindowFlowResearchV1Engine.Simulate(
                config, costed, studyStartUtc, studyEndUtc, flowIndex, scenario);
            var summary = sim.Summary;
            rows.Add(new ShortWindowCostSensitivityRow
            {
                ActivationRuleName = config.ActivationRuleName,
                CostScenario = scenario,
                TradeCount = summary.TotalTrades,
                NetPnlQuote = summary.NetPnlQuote,
                WinRate = summary.WinRate,
                ProfitFactor = summary.ProfitFactor,
                NetPositive = summary.NetPnlQuote > 0m,
                SurvivesModerateSlippage002 =
                    string.Equals(scenario, NoPaidDataShortWindowFlowResearchV1Catalog.ModerateSlippageScenario, StringComparison.OrdinalIgnoreCase)
                    && summary.NetPnlQuote >= NoPaidDataShortWindowFlowResearchV1Catalog.NotDestroyedFloorQuote,
                SurvivesStress = scenario.Contains("stress", StringComparison.OrdinalIgnoreCase) && summary.NetPnlQuote >= 0m
            });
        }

        return rows;
    }

    public static RegimeDriftDiagnosticTrade[] MapCostScenario(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        string scenarioLabel,
        BtcContextIndex btcContext,
        DateTime dataEndUtc)
        => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog
            .ApplyCostScenario(baseTrades, scenarioLabel)
            .Select(t => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MapTrade(t, btcContext, dataEndUtc))
            .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        IReadOnlyList<ShortWindowDataAvailabilityRow> availability,
        IReadOnlyList<ShortWindowSummaryRow> summary,
        IReadOnlyDictionary<string, ShortWindowSimResult> simByRule,
        IReadOnlyList<ShortWindowCostSensitivityRow> costSensitivity,
        ShortWindowSummaryRow baseline,
        DateTime studyStartUtc,
        DateTime studyEndUtc,
        DateTime? flowCoverageStartUtc)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var nonBaseline = summary.Where(s => s.PerfCondition != nameof(ShortWindowPerfCondition.AlwaysOn)).ToArray();
        var qualifying = nonBaseline.Where(s => s.PassesSuccessCriteria).ToArray();
        var best = nonBaseline.OrderByDescending(s => s.NetPnlQuote).FirstOrDefault();
        var bestWithTrades = nonBaseline
            .Where(s => s.MeetsMinExecutedTrades)
            .OrderByDescending(s => s.NetPnlQuote)
            .FirstOrDefault();

        // Q1: free flow endpoints usable for short windows.
        var binanceUseful = availability
            .Where(a => a.Provider.StartsWith("binance", StringComparison.OrdinalIgnoreCase) && a.UsefulFor30d && a.LocalFilePresent)
            .Select(a => a.SourceKey).Distinct().ToArray();
        var coinalyzeProbed = availability
            .Where(a => a.Provider.StartsWith("coinalyze", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var coinalyzeVerified = coinalyzeProbed.Any(a => a.ProbeStatus.StartsWith("OK", StringComparison.OrdinalIgnoreCase));
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which useful free flow endpoints are available for short windows?",
            Answer = $"Binance free (7d/14d/30d usable, locally cached): {string.Join(", ", binanceUseful)}. " +
                     $"Funding, mark and index klines support 365d. Liquidations/depth are not on the free REST API. " +
                     $"Coinalyze free API: {(coinalyzeVerified ? "verified live with key" : "requires free registered key (env " + CoinalyzeFeasibilityProbe.ApiKeyEnvVar + "), probed without key")}; " +
                     "documented retention limits intraday granularities to ~1500-2000 points (5min~7d, 1hour~83d, daily=long history), incl. liquidations and long/short ratio.",
            Verdict = binanceUseful.Length > 0 ? "FreeShortWindowFlowDataAvailable" : "InsufficientFreeFlowData",
            Details = new Dictionary<string, object?>
            {
                ["binance30dSources"] = binanceUseful,
                ["coinalyzeProbeStatuses"] = coinalyzeProbed.Select(a => $"{a.SourceKey}: {a.ProbeStatus}").ToArray()
            }
        });

        // Q2: do flow features separate good from bad next periods (flow-only rules, non-selective period stats)?
        var flowSeparation = new List<object>();
        var anyFlowSeparates = false;
        foreach (var flow in NoPaidDataShortWindowFlowResearchV1Catalog.FlowConditions)
        {
            var refRuleName = $"Flow_{flow}_Chk4h_LB7d_Fwd24h";
            if (!simByRule.TryGetValue(refRuleName, out var sim))
                continue;
            var available = sim.Periods.Where(p => p.FlowDataAvailable).ToArray();
            var confirmed = available.Where(p => p.FlowConditionPass).ToArray();
            var rejected = available.Where(p => !p.FlowConditionPass).ToArray();
            if (confirmed.Length == 0 || rejected.Length == 0)
                continue;
            var confirmedAvg = confirmed.Average(p => p.NetInActivationWindow);
            var rejectedAvg = rejected.Average(p => p.NetInActivationWindow);
            if (confirmedAvg > rejectedAvg && confirmedAvg > 0m)
                anyFlowSeparates = true;
            flowSeparation.Add(new
            {
                flowCondition = flow.ToString(),
                confirmedCheckpoints = confirmed.Length,
                rejectedCheckpoints = rejected.Length,
                avgNext24hNetWhenConfirmed = Math.Round(confirmedAvg, 4),
                avgNext24hNetWhenRejected = Math.Round(rejectedAvg, 4)
            });
        }

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Can free 30d flow data explain recent positive Rule01 windows?",
            Answer = flowSeparation.Count == 0
                ? "Not measurable: flow coverage or checkpoint count too small for the reference grid (Chk4h/LB7d/Fwd24h)."
                : $"Per-flow-condition split of next-24h baseline net at every checkpoint (no selection): {flowSeparation.Count} conditions measured; " +
                  (anyFlowSeparates
                      ? "at least one flow condition shows higher and positive average next-period net when confirmed."
                      : "no flow condition shows a positive, higher next-period net when confirmed — flow data does not clearly explain the positive windows."),
            Verdict = flowSeparation.Count == 0 ? "NotMeasurable" : anyFlowSeparates ? "FlowPartiallyExplains" : "FlowDoesNotExplain",
            Details = new Dictionary<string, object?> { ["flowSeparation"] = flowSeparation }
        });

        // Q3: does short-window activation improve next-period PnL without leakage?
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does short-window activation improve next-period PnL without future leakage?",
            Answer = best is null
                ? "No activation rules evaluated."
                : $"Baseline study-window net={baseline.NetPnlQuote:F2} ({baseline.TotalTrades} trades). " +
                  $"Best activation rule: {best.ActivationRuleName} net={best.NetPnlQuote:F2} (delta {best.Delta:F2}, trades={best.TotalTrades}). " +
                  $"Best with >= {NoPaidDataShortWindowFlowResearchV1Catalog.MinimumExecutedTrades} trades: " +
                  $"{(bestWithTrades is null ? "none" : $"{bestWithTrades.ActivationRuleName} net={bestWithTrades.NetPnlQuote:F2} (delta {bestWithTrades.Delta:F2})")}.",
            Verdict = bestWithTrades?.NetPnlQuote > 0m && bestWithTrades.Delta > 0m
                ? "ImprovesNextPeriodPnl"
                : best?.NetPnlQuote > 0m ? "PositiveButSparseOrNoImprovement" : "NoImprovement"
        });

        // Q4: does flow confirmation reduce false activation periods?
        var perfOnly = nonBaseline.Where(s =>
            s.PerfCondition == nameof(ShortWindowPerfCondition.RecentNetPositive)
            && s.FlowCondition == nameof(ShortWindowFlowCondition.None)
            && s.ActivatedPeriodCount > 0).ToArray();
        var combined = nonBaseline.Where(s =>
            s.PerfCondition == nameof(ShortWindowPerfCondition.RecentNetPositive)
            && s.FlowCondition != nameof(ShortWindowFlowCondition.None)
            && s.ActivatedPeriodCount > 0).ToArray();
        decimal? perfFalseRate = perfOnly.Length == 0
            ? null
            : 1m - perfOnly.Average(s => s.PositivePeriodRate);
        decimal? combinedFalseRate = combined.Length == 0
            ? null
            : 1m - combined.Average(s => s.PositivePeriodRate);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does flow confirmation reduce false activation periods?",
            Answer = perfFalseRate is null || combinedFalseRate is null
                ? "Not measurable: too few activated periods in perf-only or combined rules."
                : $"Average non-positive activated-period rate: perf-only={perfFalseRate:P1} ({perfOnly.Length} rules), " +
                  $"perf+flow combined={combinedFalseRate:P1} ({combined.Length} rules).",
            Verdict = perfFalseRate is null || combinedFalseRate is null
                ? "NotMeasurable"
                : combinedFalseRate < perfFalseRate ? "FlowReducesFalseActivations" : "FlowDoesNotReduceFalseActivations"
        });

        // Q5: positive net over the available short window?
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Can the method produce positive net over the available short-window data?",
            Answer = qualifying.Length > 0
                ? $"{qualifying.Length} rule(s) pass all success criteria (>= {NoPaidDataShortWindowFlowResearchV1Catalog.MinimumExecutedTrades} trades, positive net, survives moderate+0.02 slippage, >1 activation cluster)."
                : bestWithTrades?.NetPnlQuote > 0m
                    ? $"Positive net with sufficient trades exists ({bestWithTrades.ActivationRuleName} net={bestWithTrades.NetPnlQuote:F2}) but full success criteria not met (verdict: {bestWithTrades.Verdict})."
                    : best?.NetPnlQuote > 0m
                        ? $"Only sparse-positive rules found (best {best.ActivationRuleName} net={best.NetPnlQuote:F2}, trades={best.TotalTrades} < {NoPaidDataShortWindowFlowResearchV1Catalog.MinimumExecutedTrades}); mark as SPARSE."
                        : "No activation rule produced positive net in the study window.",
            Verdict = qualifying.Length > 0 ? "PositiveNetAchieved"
                : bestWithTrades?.NetPnlQuote > 0m ? "PositiveButFailsCriteria"
                : best?.NetPnlQuote > 0m ? "SparsePositiveOnly" : "NoPositiveNet"
        });

        // Q6: stability for future paper/sandbox planning.
        var stable = qualifying.Where(s => s.PositivePeriodRate > 0.5m && s.ActivationClusterCount >= 2).ToArray();
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is it stable enough for future paper/sandbox planning?",
            Answer = stable.Length > 0
                ? $"{stable.Length} qualifying rule(s) also show >50% positive activated periods across >=2 clusters. Still a single ~30d window — treat as preliminary, re-verify after more free data is collected. No paper/sandbox action from this run."
                : "No. Either no rule qualifies, or qualifying rules are concentrated in one cluster / minority-positive periods. The window is a single ~30d sample.",
            Verdict = stable.Length > 0 ? "PreliminarilyStableButShortSample" : "NotStableEnough",
            Details = new Dictionary<string, object?>
            {
                ["studyWindowDays"] = Math.Round((decimal)(studyEndUtc - studyStartUtc).TotalDays, 2),
                ["flowCoverageStartUtc"] = flowCoverageStartUtc
            }
        });

        // Q7: pause until more free data is collected?
        var shouldPause = qualifying.Length == 0;
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "If not, should we pause until we have more free collected data?",
            Answer = shouldPause
                ? "Yes. Keep the merge-collecting downloader running periodically (each run extends the local flow history beyond Binance's ~30d API limit) and optionally register a free Coinalyze key for liquidation/long-short history. Re-run this branch when 60-90d of collected flow data exists."
                : "Qualifying rules exist, but the sample is one short window. Continue collecting free flow data and re-validate on a longer collected window before any paper/sandbox planning.",
            Verdict = shouldPause ? "PauseAndCollectFreeData" : "ContinueCollectingAndRevalidate",
            Details = new Dictionary<string, object?>
            {
                ["backtestOnly"] = true,
                ["liveFuturesRecommended"] = false,
                ["paidDataUsed"] = false
            }
        });

        return answers;
    }

    private sealed record RiskResult(decimal MaxDrawdown, int MaxConsecutiveLosses, decimal ProfitFactor);

    private static RiskResult ComputeRisk(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        if (trades.Count == 0)
            return new RiskResult(0m, 0, 0m);

        decimal peak = 0m, equity = 0m, maxDd = 0m, grossWin = 0m, grossLoss = 0m;
        var maxConsec = 0;
        var consec = 0;
        foreach (var trade in trades.OrderBy(t => t.ExitTimeUtc))
        {
            equity += trade.NetPnlQuote;
            if (trade.NetPnlQuote > 0m)
                grossWin += trade.NetPnlQuote;
            else
                grossLoss += Math.Abs(trade.NetPnlQuote);
            if (equity > peak)
                peak = equity;
            var dd = peak - equity;
            if (dd > maxDd)
                maxDd = dd;
            if (trade.NetPnlQuote <= 0m)
            {
                consec++;
                if (consec > maxConsec)
                    maxConsec = consec;
            }
            else
            {
                consec = 0;
            }
        }

        var pf = grossLoss == 0m ? (grossWin > 0m ? 999m : 0m) : grossWin / grossLoss;
        return new RiskResult(maxDd, maxConsec, pf);
    }
}
