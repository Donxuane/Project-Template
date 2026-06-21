namespace TradingBot.Backtest;

public sealed record DirectionalRuleV2CostScenario(
    string Label,
    FeasibilityCostScenario BaseScenario,
    decimal ExtraAdverseSlippagePercentPerSide = 0m)
{
    public bool IsStressFamily
        => Label.Contains("stress", StringComparison.OrdinalIgnoreCase);
}

public static class DirectionalRuleFuturesValidationV2CostModel
{
    public static IReadOnlyList<DirectionalRuleV2CostScenario> BuildValidationScenarios()
    {
        var moderate = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios()
            .First(s => s.Label == "futures-moderate");
        var stress = LongShortFuturesFeasibilityStudyV1CostModel.BuildStudyScenarios()
            .First(s => s.Label == "futures-stress");

        return
        [
            new("futures-moderate", moderate, 0m),
            new("futures-stress", stress, 0m),
            new("futures-stress-plus", new FeasibilityCostScenario(
                "futures-stress-plus", "futures-sim", 0.06m, 0.06m, 0.06m, 0.015m), 0m),
            new("futures-moderate-latency-002", moderate, 0.02m),
            new("futures-moderate-latency-005", moderate, 0.05m),
            new("futures-stress-latency-002", stress, 0.02m),
            new("futures-stress-latency-005", stress, 0.05m)
        ];
    }

    public static decimal EstimateRoundTripCostPercent(DirectionalRuleV2CostScenario scenario)
        => RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(scenario.BaseScenario)
           + (scenario.ExtraAdverseSlippagePercentPerSide * 2m);

    public static DirectionalRuleCostBreakdown ComputeCostBreakdown(
        DirectionalTradeSimulationResult simulation,
        LongShortDirection direction,
        DirectionalRuleV2CostScenario scenario,
        decimal quantity = 1m)
    {
        var baseBreakdown = DirectionalRuleFuturesSimulationV1Simulator.ComputeCostBreakdown(
            simulation, direction, scenario.BaseScenario, quantity);
        if (scenario.ExtraAdverseSlippagePercentPerSide <= 0m)
            return baseBreakdown;

        var entryNotional = simulation.EntryPrice * quantity;
        var exitNotional = simulation.ExitPrice * quantity;
        var extraSlippage = (entryNotional + exitNotional)
            * (scenario.ExtraAdverseSlippagePercentPerSide / 100m);
        return baseBreakdown with
        {
            SlippageEstimateQuote = Math.Round(baseBreakdown.SlippageEstimateQuote + extraSlippage, 8),
            NetPnlQuote = Math.Round(baseBreakdown.NetPnlQuote - extraSlippage, 8)
        };
    }
}
