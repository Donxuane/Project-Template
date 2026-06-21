namespace TradingBot.Backtest;

public sealed record RangeExpansionV2FeasibilitySummaryRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string Symbol { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal CurrentCostNetPnlQuote { get; init; }
    public decimal CurrentCostNetPerTrade { get; init; }
    public int CurrentCostNetWinnerCount { get; init; }
    public decimal BreakEvenRoundTripCostPercent { get; init; }
    public decimal MaxRealisticScenarioNetPnlQuote { get; init; }
    public string MaxRealisticScenarioLabel { get; init; } = string.Empty;
    public decimal FuturesSimModerateNetPnlQuote { get; init; }
    public bool PositiveUnderRealisticLowerCost { get; init; }
    public bool PositiveOnlyUnderUnrealisticCost { get; init; }
}

public sealed record RangeExpansionV2CostSurfaceRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string ScenarioLabel { get; init; } = string.Empty;
    public string MarketMode { get; init; } = string.Empty;
    public decimal FeeRatePercent { get; init; }
    public decimal SpreadPercent { get; init; }
    public decimal SlippagePercent { get; init; }
    public decimal FundingRatePercentPerHour { get; init; }
    public decimal RoundTripCostPercent { get; init; }
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal NetPerTrade { get; init; }
    public int NetWinnerCount { get; init; }
    public bool IsProfitable { get; init; }
}

public sealed record RangeExpansionV2BreakEvenCostRow
{
    public string VariantLabel { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal CurrentRoundTripCostPercent { get; init; }
    public decimal CurrentCostNetPnlQuote { get; init; }
    public decimal BreakEvenRoundTripCostPercent { get; init; }
    public decimal HeadroomToBreakEvenPercent { get; init; }
    public decimal Slip002NetAtLowFee { get; init; }
    public decimal Slip005NetAtLowFee { get; init; }
}

public sealed record RangeExpansionV2FeasibilityExtendedDiagnostics(
    IReadOnlyList<RangeExpansionV2FeasibilitySummaryRow> Summary,
    IReadOnlyList<RangeExpansionV2CostSurfaceRow> CostSurface,
    IReadOnlyList<RangeExpansionV2BreakEvenCostRow> BreakEvenAnalysis,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers);

public static class RangeExpansionV2FeasibilityDiagnosticsAggregator
{
    public static RangeExpansionV2FeasibilityExtendedDiagnostics Build(
        IReadOnlyList<SimulatedTrade> trades,
        decimal currentFeeRatePercent,
        decimal currentSpreadPercent,
        decimal currentSlippagePercent)
    {
        var scenarios = RangeExpansionV2FeasibilityCostModel.BuildStandardScenarios();
        var summary = BuildSummary(trades, scenarios, currentFeeRatePercent, currentSpreadPercent, currentSlippagePercent);
        var costSurface = BuildCostSurface(trades, scenarios);
        var breakEven = BuildBreakEvenAnalysis(trades, currentFeeRatePercent, currentSpreadPercent, currentSlippagePercent);
        var answers = BuildResearchAnswers(summary, costSurface, breakEven, trades);

        return new RangeExpansionV2FeasibilityExtendedDiagnostics(summary, costSurface, breakEven, answers);
    }

    public static IReadOnlyList<RangeExpansionV2FeasibilitySummaryRow> BuildSummary(
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<FeasibilityCostScenario> scenarios,
        decimal currentFeeRatePercent,
        decimal currentSpreadPercent,
        decimal currentSlippagePercent)
    {
        var currentScenario = new FeasibilityCostScenario(
            "current-run", "spot", currentFeeRatePercent, currentSpreadPercent, currentSlippagePercent, 0m);

        return trades
            .GroupBy(t => t.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var profileTrades = group.ToList();
                var variant = ExtractVariantLabel(group.Key);
                var currentNets = profileTrades.Select(t => RangeExpansionV2FeasibilityCostModel.RecalculateNetPnl(t, currentScenario)).ToList();
                var currentNet = currentNets.Sum();
                var breakEven = RangeExpansionV2FeasibilityCostModel.FindBreakEvenRoundTripCostPercent(
                    profileTrades, currentFeeRatePercent, currentSpreadPercent, currentSlippagePercent);

                var realisticScenarios = scenarios.Where(RangeExpansionV2FeasibilityCostModel.IsRealisticLowerCost).ToList();
                var bestRealistic = realisticScenarios
                    .Select(s => new { Scenario = s, Net = RangeExpansionV2FeasibilityCostModel.SumNetPnl(profileTrades, s) })
                    .MaxBy(x => x.Net);

                var profitableScenarios = scenarios
                    .Select(s => new { Scenario = s, Net = RangeExpansionV2FeasibilityCostModel.SumNetPnl(profileTrades, s) })
                    .Where(x => x.Net > 0m)
                    .ToList();
                var profitableUnrealisticOnly = profitableScenarios.Count > 0
                    && profitableScenarios.All(x => RangeExpansionV2FeasibilityCostModel.IsUnrealisticallyLowCost(x.Scenario));

                var futuresModerate = scenarios.First(s => s.Label == "futures-sim-moderate");
                var futuresModerateNet = RangeExpansionV2FeasibilityCostModel.SumNetPnl(profileTrades, futuresModerate);

                return new RangeExpansionV2FeasibilitySummaryRow
                {
                    VariantLabel = variant,
                    ProfileName = group.Key,
                    Symbol = profileTrades.First().Symbol.ToString(),
                    TradeCount = profileTrades.Count,
                    GrossPnlQuote = profileTrades.Sum(t => t.GrossPnlQuote),
                    CurrentCostNetPnlQuote = currentNet,
                    CurrentCostNetPerTrade = profileTrades.Count == 0 ? 0m : Math.Round(currentNet / profileTrades.Count, 8),
                    CurrentCostNetWinnerCount = currentNets.Count(n => n > 0m),
                    BreakEvenRoundTripCostPercent = breakEven,
                    MaxRealisticScenarioNetPnlQuote = bestRealistic?.Net ?? 0m,
                    MaxRealisticScenarioLabel = bestRealistic?.Scenario.Label ?? string.Empty,
                    FuturesSimModerateNetPnlQuote = futuresModerateNet,
                    PositiveUnderRealisticLowerCost = bestRealistic?.Net > 0m,
                    PositiveOnlyUnderUnrealisticCost = profitableUnrealisticOnly
                };
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV2CostSurfaceRow> BuildCostSurface(
        IReadOnlyList<SimulatedTrade> trades,
        IReadOnlyList<FeasibilityCostScenario> scenarios)
    {
        return trades
            .GroupBy(t => t.ProfileName, StringComparer.OrdinalIgnoreCase)
            .SelectMany(group =>
            {
                var profileTrades = group.ToList();
                var variant = ExtractVariantLabel(group.Key);
                return scenarios.Select(scenario =>
                {
                    var nets = profileTrades.Select(t => RangeExpansionV2FeasibilityCostModel.RecalculateNetPnl(t, scenario)).ToList();
                    var net = nets.Sum();
                    return new RangeExpansionV2CostSurfaceRow
                    {
                        VariantLabel = variant,
                        ProfileName = group.Key,
                        ScenarioLabel = scenario.Label,
                        MarketMode = scenario.MarketMode,
                        FeeRatePercent = scenario.FeeRatePercent,
                        SpreadPercent = scenario.SpreadPercent,
                        SlippagePercent = scenario.SlippagePercent,
                        FundingRatePercentPerHour = scenario.FundingRatePercentPerHour,
                        RoundTripCostPercent = RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(scenario),
                        TradeCount = profileTrades.Count,
                        NetPnlQuote = net,
                        NetPerTrade = profileTrades.Count == 0 ? 0m : Math.Round(net / profileTrades.Count, 8),
                        NetWinnerCount = nets.Count(n => n > 0m),
                        IsProfitable = net > 0m
                    };
                });
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ScenarioLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RangeExpansionV2BreakEvenCostRow> BuildBreakEvenAnalysis(
        IReadOnlyList<SimulatedTrade> trades,
        decimal currentFeeRatePercent,
        decimal currentSpreadPercent,
        decimal currentSlippagePercent)
    {
        var currentRoundTrip = (currentFeeRatePercent * 2m) + currentSpreadPercent + (currentSlippagePercent * 2m);
        var currentScenario = new FeasibilityCostScenario("current-run", "spot", currentFeeRatePercent, currentSpreadPercent, currentSlippagePercent, 0m);
        var slip002 = new FeasibilityCostScenario("slip-002", "spot", 0.05m, 0.03m, 0.02m, 0m);
        var slip005 = new FeasibilityCostScenario("slip-005", "spot", 0.05m, 0.03m, 0.05m, 0m);

        return trades
            .GroupBy(t => t.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var profileTrades = group.ToList();
                var currentNet = RangeExpansionV2FeasibilityCostModel.SumNetPnl(profileTrades, currentScenario);
                var breakEven = RangeExpansionV2FeasibilityCostModel.FindBreakEvenRoundTripCostPercent(
                    profileTrades, currentFeeRatePercent, currentSpreadPercent, currentSlippagePercent);

                return new RangeExpansionV2BreakEvenCostRow
                {
                    VariantLabel = ExtractVariantLabel(group.Key),
                    ProfileName = group.Key,
                    TradeCount = profileTrades.Count,
                    GrossPnlQuote = profileTrades.Sum(t => t.GrossPnlQuote),
                    CurrentRoundTripCostPercent = currentRoundTrip,
                    CurrentCostNetPnlQuote = currentNet,
                    BreakEvenRoundTripCostPercent = breakEven,
                    HeadroomToBreakEvenPercent = Math.Round(breakEven - currentRoundTrip, 6),
                    Slip002NetAtLowFee = RangeExpansionV2FeasibilityCostModel.SumNetPnl(profileTrades, slip002),
                    Slip005NetAtLowFee = RangeExpansionV2FeasibilityCostModel.SumNetPnl(profileTrades, slip005)
                };
            })
            .OrderBy(r => r.VariantLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ReachabilityResearchAnswer> BuildResearchAnswers(
        IReadOnlyList<RangeExpansionV2FeasibilitySummaryRow> summary,
        IReadOnlyList<RangeExpansionV2CostSurfaceRow> costSurface,
        IReadOnlyList<RangeExpansionV2BreakEvenCostRow> breakEven,
        IReadOnlyList<SimulatedTrade> trades)
    {
        var answers = new List<ReachabilityResearchAnswer>();
        var body80Current = summary.FirstOrDefault(r => r.VariantLabel.Contains("body80-halflock-current", StringComparison.OrdinalIgnoreCase))
            ?? summary.MaxBy(r => r.BreakEvenRoundTripCostPercent);
        var body80CostCover = summary.FirstOrDefault(r => r.VariantLabel.Contains("body80-halflock-costcover", StringComparison.OrdinalIgnoreCase));
        var bestBreakEven = breakEven.MaxBy(r => r.BreakEvenRoundTripCostPercent);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "What maximum round-trip cost makes body80 profitable?",
            Answer = bestBreakEven is null
                ? "No trades to analyze."
                : $"{bestBreakEven.VariantLabel} break-even round-trip cost ≈ {bestBreakEven.BreakEvenRoundTripCostPercent:F4}% (current {bestBreakEven.CurrentRoundTripCostPercent:F4}%, headroom {bestBreakEven.HeadroomToBreakEvenPercent:F4}%).",
            Verdict = bestBreakEven?.BreakEvenRoundTripCostPercent > bestBreakEven?.CurrentRoundTripCostPercent
                ? "HasCostHeadroom"
                : "NoCostHeadroom",
            Details = new Dictionary<string, object?> { ["breakEvenRows"] = breakEven.ToArray(), ["body80Current"] = body80Current }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Is profitability only possible under unrealistically low costs?",
            Answer = summary.All(r => !r.PositiveUnderRealisticLowerCost)
                ? "All profiles remain negative under realistic lower Spot/Futures-sim assumptions."
                : string.Join("; ", summary.Where(r => r.PositiveUnderRealisticLowerCost).Select(r =>
                    $"{r.VariantLabel} positive under {r.MaxRealisticScenarioLabel} net={r.MaxRealisticScenarioNetPnlQuote:F8}.")),
            Verdict = summary.Any(r => r.PositiveUnderRealisticLowerCost && !r.PositiveOnlyUnderUnrealisticCost)
                ? "RealisticLowerCostCanWork"
                : summary.Any(r => r.PositiveOnlyUnderUnrealisticCost)
                    ? "OnlyUnrealisticCosts"
                    : "NeverProfitableEvenAtZeroCost",
            Details = new Dictionary<string, object?> { ["summary"] = summary.ToArray() }
        });

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does slippage destroy the tiny edge?",
            Answer = body80Current is null
                ? "No body80 reference profile."
                : $"body80-current slip0.02 net={breakEven.FirstOrDefault(r => r.VariantLabel == body80Current.VariantLabel)?.Slip002NetAtLowFee:F8}, slip0.05 net={breakEven.FirstOrDefault(r => r.VariantLabel == body80Current.VariantLabel)?.Slip005NetAtLowFee:F8}.",
            Verdict = body80Current is not null
                      && breakEven.First(r => r.VariantLabel == body80Current.VariantLabel).Slip005NetAtLowFee < 0m
                      && breakEven.First(r => r.VariantLabel == body80Current.VariantLabel).Slip002NetAtLowFee < body80Current.CurrentCostNetPnlQuote
                ? "SlippageDestroysEdge"
                : "SlippageManageable",
            Details = new Dictionary<string, object?> { ["breakEven"] = breakEven.ToArray() }
        });

        var sensitivity = costSurface
            .GroupBy(r => r.VariantLabel)
            .Select(g => new
            {
                Variant = g.Key,
                Spread = g.Max(r => r.NetPnlQuote) - g.Min(r => r.NetPnlQuote),
                Worst = g.MinBy(r => r.NetPnlQuote),
                Best = g.MaxBy(r => r.NetPnlQuote)
            })
            .OrderByDescending(x => x.Spread)
            .ToArray();

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Which exit policy is most cost-sensitive?",
            Answer = sensitivity.Length == 0
                ? "No cost surface rows."
                : string.Join("; ", sensitivity.Select(s =>
                    $"{s.Variant} range={s.Spread:F8} (best {s.Best?.ScenarioLabel}={s.Best?.NetPnlQuote:F8}, worst {s.Worst?.ScenarioLabel}={s.Worst?.NetPnlQuote:F8})")),
            Verdict = sensitivity.FirstOrDefault()?.Variant ?? "Unknown",
            Details = new Dictionary<string, object?> { ["sensitivity"] = sensitivity }
        });

        var bnbBody80Gross = body80Current?.GrossPnlQuote ?? trades.Sum(t => t.GrossPnlQuote);
        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Does BNB 1m have enough gross edge to justify future Futures research?",
            Answer = body80Current is null
                ? "Missing body80 profile."
                : $"body80 gross={bnbBody80Gross:F8}, current net={body80Current.CurrentCostNetPnlQuote:F8}, futures-sim-moderate net={body80Current.FuturesSimModerateNetPnlQuote:F8}, break-even RT cost={body80Current.BreakEvenRoundTripCostPercent:F4}%.",
            Verdict = body80Current?.FuturesSimModerateNetPnlQuote > 0m
                ? "FuturesResearchCandidate"
                : body80Current?.BreakEvenRoundTripCostPercent > RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(
                    new FeasibilityCostScenario("futures-sim-moderate", "futures-sim", 0.05m, 0.03m, 0.05m, 0.01m))
                    ? "ThinGrossEdgeNeedsLowerCosts"
                    : "InsufficientGrossEdge",
            Details = new Dictionary<string, object?> { ["body80Current"] = body80Current, ["body80CostCover"] = body80CostCover }
        });

        var keepResearch = summary.Any(r => r.PositiveUnderRealisticLowerCost)
            || summary.Any(r => r.BreakEvenRoundTripCostPercent > 0.18m && r.GrossPnlQuote > 0m);

        answers.Add(new ReachabilityResearchAnswer
        {
            Question = "Should we continue with this family for Futures/lower-fee research, or park it completely?",
            Answer = keepResearch
                ? "Keep as backtest-only Futures/lower-fee research candidate; do not promote to live Spot or live Futures."
                : "Park completely — gross edge too thin even under generous simulated costs.",
            Verdict = keepResearch ? "KeepFeasibilityResearchOnly" : "ParkCompletely",
            Details = new Dictionary<string, object?>
            {
                ["summary"] = summary.ToArray(),
                ["recommendation"] = "Backtest-only. No live trading recommendation from this branch."
            }
        });

        return answers;
    }

    private static string ExtractVariantLabel(string profileName)
    {
        const string prefix = "range-expansion-v2-feasibility-";
        if (profileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = profileName[prefix.Length..];
            var markerIndex = remainder.IndexOf("-1m-", StringComparison.OrdinalIgnoreCase);
            return markerIndex >= 0 ? remainder[..markerIndex] : remainder;
        }

        return profileName;
    }
}
