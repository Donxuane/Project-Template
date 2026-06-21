namespace TradingBot.Backtest;

public static class NoPaidDataAdaptiveActivationV1Aggregator
{
    public static AdaptiveActivationSummaryRow BuildSummaryRow(
        AdaptiveActivationRuleConfig rule,
        IReadOnlyList<RegimeDriftDiagnosticTrade> taken,
        IReadOnlyList<RegimeDriftDiagnosticTrade> baseline,
        IReadOnlyList<AdaptiveActivationPeriodRow> periods,
        string costScenario,
        DateTime dataEndUtc)
    {
        var baselineNet = baseline.Sum(t => t.NetPnlQuote);
        var baselineOlder = baseline.Where(t => t.InOlder).Sum(t => t.NetPnlQuote);
        var baselineRecent90 = baseline.Where(t => t.InRecent90d).Sum(t => t.NetPnlQuote);
        var full365 = taken.Sum(t => t.NetPnlQuote);
        var older = taken.Where(t => t.InOlder).Sum(t => t.NetPnlQuote);
        var recent90 = taken.Where(t => t.InRecent90d).Sum(t => t.NetPnlQuote);

        var activatedPeriods = periods.Where(p => p.Activated).ToArray();
        var positivePeriods = activatedPeriods.Count(p => p.NetPnlDuringActivation > 0m);
        var totalPeriods = activatedPeriods.Length;
        var risk = ComputeRisk(taken);
        var olderTradeCount = taken.Count(t => t.InOlder);
        var baselineOlderTradeCount = baseline.Count(t => t.InOlder);

        var meetsMinTrades = taken.Count >= NoPaidDataAdaptiveActivationV1Catalog.MinimumExecutedTrades;
        var full365Near = full365 >= NoPaidDataAdaptiveActivationV1Catalog.NearBreakevenFull365;
        var olderImprovedWithExposure = olderTradeCount >= NoPaidDataAdaptiveActivationV1Catalog.MinimumOlderTrades
                                        && older >= baselineOlder + NoPaidDataAdaptiveActivationV1Catalog.MinimumOlderImprovementQuote;
        var olderAvoidedOnly = olderTradeCount == 0 && baselineOlder < -100m;
        var olderReduced = olderImprovedWithExposure || (!olderAvoidedOnly && older > baselineOlder + 50m);
        var recentPositive = recent90 > 0m;
        var recentRetained = recent90 >= baselineRecent90 * NoPaidDataAdaptiveActivationV1Catalog.MinimumRecentRetentionRatio;
        var positiveMajority = totalPeriods >= NoPaidDataAdaptiveActivationV1Catalog.MinimumActivationPeriods
                               && positivePeriods > totalPeriods / 2;

        var passes = rule.ConditionType != AdaptiveActivationConditionType.AlwaysOn
                      && meetsMinTrades
                      && full365Near
                      && olderReduced
                      && !olderAvoidedOnly
                      && recentPositive
                      && recentRetained
                      && positiveMajority;

        return new AdaptiveActivationSummaryRow
        {
            ActivationRuleName = rule.ActivationRuleName,
            ConditionType = rule.ConditionType.ToString(),
            Description = rule.Description,
            CheckpointFrequencyDays = rule.CheckpointFrequencyDays,
            LookbackDays = rule.LookbackDays,
            ActivationPeriodDays = rule.ActivationPeriodDays,
            MinLookbackTrades = rule.MinLookbackTrades,
            CostScenario = costScenario,
            TotalTrades = taken.Count,
            BaselineTrades = baseline.Count,
            TradeRetentionRate = baseline.Count == 0 ? 0m : Math.Round((decimal)taken.Count / baseline.Count, 6),
            Full365NetPnl = Math.Round(full365, 8),
            BaselineFull365NetPnl = Math.Round(baselineNet, 8),
            Full365Delta = Math.Round(full365 - baselineNet, 8),
            OlderNetPnl = Math.Round(older, 8),
            BaselineOlderNetPnl = Math.Round(baselineOlder, 8),
            OlderDelta = Math.Round(older - baselineOlder, 8),
            Recent90dNetPnl = Math.Round(recent90, 8),
            BaselineRecent90dNetPnl = Math.Round(baselineRecent90, 8),
            Recent90dDelta = Math.Round(recent90 - baselineRecent90, 8),
            PositivePeriodsCount = positivePeriods,
            TotalPeriodsCount = totalPeriods,
            PositivePeriodRate = totalPeriods == 0 ? 0m : Math.Round((decimal)positivePeriods / totalPeriods, 6),
            MaxDrawdownQuote = Math.Round(risk.MaxDrawdown, 8),
            MaxConsecutiveLosses = risk.MaxConsecutiveLosses,
            WinRate = taken.Count == 0 ? 0m : Math.Round((decimal)taken.Count(t => t.NetPnlQuote > 0m) / taken.Count, 6),
            ProfitFactor = Math.Round(risk.ProfitFactor, 6),
            MeetsMinTrades = meetsMinTrades,
            Full365NearBreakeven = full365Near,
            OlderLossReduced = olderReduced,
            Recent90dPositive = recentPositive && recentRetained,
            PositivePeriodsMajority = positiveMajority,
            PassesSuccessCriteria = passes,
            Verdict = ClassifyVerdict(rule, passes, meetsMinTrades, full365Near, olderReduced, olderAvoidedOnly, recentPositive, recentRetained, positiveMajority)
        };
    }

    public static IReadOnlyList<AdaptiveActivationWindowPerformanceRow> BuildWindowPerformance(
        string ruleName,
        IReadOnlyList<RegimeDriftDiagnosticTrade> taken,
        IReadOnlyList<RegimeDriftDiagnosticTrade> baseline,
        string costScenario,
        DateTime dataEndUtc)
    {
        var windows = new[] { 30, 60, 90, 180, 365 };
        var rows = new List<AdaptiveActivationWindowPerformanceRow>();
        foreach (var days in windows)
        {
            var start = dataEndUtc.AddDays(-days);
            var takenWindow = taken.Where(t => t.EntryTimeUtc >= start).ToArray();
            var baselineWindow = baseline.Where(t => t.EntryTimeUtc >= start).ToArray();
            var net = takenWindow.Sum(t => t.NetPnlQuote);
            var baselineNet = baselineWindow.Sum(t => t.NetPnlQuote);
            rows.Add(new AdaptiveActivationWindowPerformanceRow
            {
                ActivationRuleName = ruleName,
                WindowLabel = $"{days}d",
                CostScenario = costScenario,
                TradeCount = takenWindow.Length,
                NetPnlQuote = Math.Round(net, 8),
                BaselineNetPnlQuote = Math.Round(baselineNet, 8),
                Delta = Math.Round(net - baselineNet, 8),
                Positive = net > 0m
            });
        }

        return rows;
    }

    public static AdaptiveActivationDrawdownRow BuildDrawdown(
        string ruleName,
        IReadOnlyList<RegimeDriftDiagnosticTrade> taken,
        string costScenario)
    {
        var risk = ComputeRisk(taken);
        return new AdaptiveActivationDrawdownRow
        {
            ActivationRuleName = ruleName,
            CostScenario = costScenario,
            MaxDrawdownQuote = Math.Round(risk.MaxDrawdown, 8),
            MaxConsecutiveLosses = risk.MaxConsecutiveLosses,
            WorstTradeNet = Math.Round(risk.WorstTrade, 8),
            MaxDrawdownPeakUtc = risk.PeakUtc,
            MaxDrawdownTroughUtc = risk.TroughUtc
        };
    }

    public static IReadOnlyList<AdaptiveActivationCostSensitivityRow> BuildCostSensitivity(
        AdaptiveActivationRuleConfig rule,
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        BtcContextIndex btcContext,
        DateTime dataStartUtc,
        DateTime dataEndUtc,
        IReadOnlyList<KlineCandle> bnbIntervalCandles,
        decimal btc30Q3Lower)
    {
        var rows = new List<AdaptiveActivationCostSensitivityRow>();
        foreach (var scenario in NoPaidDataAdaptiveActivationV1Catalog.CostStressScenarios)
        {
            var costed = MapCostScenario(baseTrades, scenario, btcContext, dataEndUtc);
            var sim = NoPaidDataAdaptiveActivationV1Engine.Simulate(
                rule, costed, costed, dataStartUtc, dataEndUtc, btcContext, bnbIntervalCandles, btc30Q3Lower, scenario);
            rows.Add(new AdaptiveActivationCostSensitivityRow
            {
                ActivationRuleName = rule.ActivationRuleName,
                CostScenario = scenario,
                TradeCount = sim.Summary.TotalTrades,
                Full365NetPnl = sim.Summary.Full365NetPnl,
                OlderNetPnl = sim.Summary.OlderNetPnl,
                Recent90dNetPnl = sim.Summary.Recent90dNetPnl,
                Full365Positive = sim.Summary.Full365NetPnl >= 0m,
                SurvivesModerateSlippage = string.Equals(scenario, "futures-moderate-latency-002", StringComparison.OrdinalIgnoreCase)
                                           && sim.Summary.Full365NetPnl >= NoPaidDataAdaptiveActivationV1Catalog.NearBreakevenFull365,
                SurvivesStress = scenario.Contains("stress", StringComparison.OrdinalIgnoreCase) && sim.Summary.Full365NetPnl >= 0m
            });
        }

        return rows;
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildAnswers(
        IReadOnlyList<AdaptiveActivationSummaryRow> summary,
        IReadOnlyList<AdaptiveActivationCostSensitivityRow> costSensitivity,
        int baselineTrades,
        decimal baselineFull365,
        decimal baselineOlder,
        decimal baselineRecent90,
        DateTime dataStartUtc,
        DateTime dataEndUtc)
    {
        var baseline = summary.FirstOrDefault(s => s.ConditionType == nameof(AdaptiveActivationConditionType.AlwaysOn));
        var qualifying = summary.Where(s => s.PassesSuccessCriteria).ToArray();
        var avoidedOlderOnly = summary.Count(s => s.Verdict == "AvoidedOlderPeriodOnly");
        var best = summary
            .Where(s => s.ConditionType != nameof(AdaptiveActivationConditionType.AlwaysOn))
            .OrderByDescending(s => s.Full365NetPnl)
            .FirstOrDefault();
        var bestHonest = summary
            .Where(s => s.ConditionType != nameof(AdaptiveActivationConditionType.AlwaysOn) && s.Verdict != "AvoidedOlderPeriodOnly")
            .OrderByDescending(s => s.Full365NetPnl)
            .FirstOrDefault();
        var bestModerateSlippage = costSensitivity
            .Where(c => string.Equals(c.CostScenario, "futures-moderate-latency-002", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(c => c.Full365NetPnl)
            .FirstOrDefault();

        return
        [
            new ReachabilityResearchAnswer
            {
                Question = "Can walk-forward activation turn the recent-regime artifact into a usable adaptive rule?",
                Answer = qualifying.Length > 0
                    ? $"{qualifying.Length} activation rule(s) passed all success criteria."
                    : best is null
                        ? "No adaptive activation rules evaluated."
                        : $"No rule passed all criteria. Best by full365 net: {best.ActivationRuleName} ({best.Full365NetPnl:F2} vs baseline {baselineFull365:F2}). {avoidedOlderOnly} rule(s) only avoided the older period without material in-period improvement.",
                Verdict = qualifying.Length > 0 ? "AdaptiveRuleUsable" : "RecentRegimeArtifactRemains"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Does activating after recent profitability improve full365 net?",
                Answer = best is null
                    ? "No evaluated rules."
                    : $"Baseline full365={baselineFull365:F2}. Best adaptive full365={best.Full365NetPnl:F2} (delta {best.Full365Delta:F2}).",
                Verdict = best?.Full365Delta > 0m ? "Full365Improved" : "Full365NotImproved"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Does it reduce older-period losses?",
                Answer = best is null
                    ? "No evaluated rules."
                    : $"Baseline older={baselineOlder:F2}. Best adaptive older={best.OlderNetPnl:F2} (delta {best.OlderDelta:F2}).",
                Verdict = best?.OlderLossReduced == true ? "OlderLossReduced" : "OlderLossNotReduced"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Does it keep enough of the recent profitable period?",
                Answer = best is null
                    ? "No evaluated rules."
                    : $"Baseline recent90d={baselineRecent90:F2}. Best adaptive recent90d={best.Recent90dNetPnl:F2} (delta {best.Recent90dDelta:F2}).",
                Verdict = best?.Recent90dPositive == true ? "Recent90dRetained" : "Recent90dNotRetained"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Is performance spread across activation periods or only one cluster?",
                Answer = best is null
                    ? "No evaluated rules."
                    : $"Best rule positive periods: {best.PositivePeriodsCount}/{best.TotalPeriodsCount} ({best.PositivePeriodRate:P1}). Honest-best (excl. older-avoid-only): {(bestHonest?.ActivationRuleName ?? "none")} full365={(bestHonest?.Full365NetPnl.ToString("F2") ?? "n/a")}.",
                Verdict = best?.PositivePeriodsMajority == true && best.TotalPeriodsCount >= NoPaidDataAdaptiveActivationV1Catalog.MinimumActivationPeriods
                    ? "SpreadAcrossPeriods" : "ConcentratedCluster"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Does it survive futures-moderate and moderate+0.02 slippage?",
                Answer = bestModerateSlippage is null
                    ? "No cost sensitivity rows."
                    : $"Best under moderate+0.02: {bestModerateSlippage.ActivationRuleName} full365={bestModerateSlippage.Full365NetPnl:F2}, survives={bestModerateSlippage.SurvivesModerateSlippage}.",
                Verdict = bestModerateSlippage?.SurvivesModerateSlippage == true ? "SurvivesModerateSlippage" : "FailsModerateSlippage"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Does it still fail under stress?",
                Answer = costSensitivity.Any(c => c.CostScenario.Contains("stress", StringComparison.OrdinalIgnoreCase) && c.Full365Positive)
                    ? "At least one rule is full365 positive under a stress scenario (review trade count before trusting)."
                    : "All evaluated adaptive rules remain negative under stress scenarios.",
                Verdict = costSensitivity.Any(c => c.CostScenario.Contains("stress", StringComparison.OrdinalIgnoreCase) && c.Full365Positive)
                    ? "SomeStressSurvival" : "FailsStress"
            },
            new ReachabilityResearchAnswer
            {
                Question = "Is there enough evidence for paper/sandbox later, or should Rule01 short still be parked?",
                Answer = qualifying.Length > 0
                    ? "Some adaptive rules meet success criteria under walk-forward logic; still backtest-only — no live/paper recommendation from this run alone."
                    : "Insufficient evidence. Park Rule01 short fully until richer free data or a different rule family is found.",
                Verdict = qualifying.Length > 0 ? "ReviewForPaperLater" : "ParkRule01Short",
                Details = new Dictionary<string, object?>
                {
                    ["backtestOnly"] = true,
                    ["liveFuturesRecommended"] = false,
                    ["baselineTrades"] = baselineTrades,
                    ["dataStartUtc"] = dataStartUtc,
                    ["dataEndUtc"] = dataEndUtc
                }
            }
        ];
    }

    private static RegimeDriftDiagnosticTrade[] MapCostScenario(
        IReadOnlyList<DirectionalRuleV2TradeRecord> baseTrades,
        string scenarioLabel,
        BtcContextIndex btcContext,
        DateTime dataEndUtc)
        => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog
            .ApplyCostScenario(baseTrades, scenarioLabel)
            .Select(t => DirectionalRuleFuturesRegimeDriftAnalysisV1Catalog.MapTrade(t, btcContext, dataEndUtc))
            .Where(t => !string.Equals(t.ExitReason, "InvalidEntry", StringComparison.OrdinalIgnoreCase))
            .ToArray();

    private sealed record RiskResult(
        decimal MaxDrawdown,
        int MaxConsecutiveLosses,
        decimal ProfitFactor,
        decimal WorstTrade,
        DateTime? PeakUtc,
        DateTime? TroughUtc);

    private static RiskResult ComputeRisk(IReadOnlyList<RegimeDriftDiagnosticTrade> trades)
    {
        if (trades.Count == 0)
            return new RiskResult(0m, 0, 0m, 0m, null, null);

        decimal peak = 0m, equity = 0m, maxDd = 0m;
        var maxConsec = 0;
        var consec = 0;
        DateTime? peakUtc = null, troughUtc = null;
        DateTime? currentPeakUtc = null;
        var grossWin = 0m;
        var grossLoss = 0m;
        var worst = trades.Min(t => t.NetPnlQuote);

        foreach (var trade in trades.OrderBy(t => t.ExitTimeUtc))
        {
            equity += trade.NetPnlQuote;
            if (trade.NetPnlQuote > 0m)
                grossWin += trade.NetPnlQuote;
            else
                grossLoss += Math.Abs(trade.NetPnlQuote);

            if (equity > peak)
            {
                peak = equity;
                currentPeakUtc = trade.ExitTimeUtc;
            }

            var dd = peak - equity;
            if (dd > maxDd)
            {
                maxDd = dd;
                peakUtc = currentPeakUtc;
                troughUtc = trade.ExitTimeUtc;
            }

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
        return new RiskResult(maxDd, maxConsec, pf, worst, peakUtc, troughUtc);
    }

    private static string ClassifyVerdict(
        AdaptiveActivationRuleConfig rule,
        bool passes,
        bool meetsMinTrades,
        bool full365Near,
        bool olderReduced,
        bool olderAvoidedOnly,
        bool recentPositive,
        bool recentRetained,
        bool positiveMajority)
    {
        if (rule.ConditionType == AdaptiveActivationConditionType.AlwaysOn)
            return "BaselineReference";
        if (passes)
            return "PassesSuccessCriteria";
        if (!meetsMinTrades)
            return "SparseTrades";
        if (!full365Near)
            return "FailsFull365";
        if (olderAvoidedOnly)
            return "AvoidedOlderPeriodOnly";
        if (!olderReduced)
            return "FailsOlderReduction";
        if (!recentPositive)
            return "FailsRecent90d";
        if (!recentRetained)
            return "FailsRecentRetention";
        if (!positiveMajority)
            return "FailsPeriodConsistency";
        return "FailsOther";
    }
}
