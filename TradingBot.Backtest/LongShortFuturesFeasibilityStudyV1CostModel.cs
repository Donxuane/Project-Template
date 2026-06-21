namespace TradingBot.Backtest;

public static class LongShortFuturesFeasibilityStudyV1CostModel
{
    public static IReadOnlyList<FeasibilityCostScenario> BuildStudyScenarios()
        =>
        [
            new("spot-conservative", "spot", 0.10m, 0.05m, 0m, 0m),
            new("futures-moderate", "futures-sim", 0.04m, 0.03m, 0.02m, 0.01m),
            new("futures-low", "futures-sim", 0.02m, 0.02m, 0.01m, 0.005m),
            new("futures-stress", "futures-sim", 0.05m, 0.05m, 0.05m, 0.01m)
        ];

    public static decimal EstimateTotalCostPercent(FeasibilityCostScenario scenario, int horizonMinutes)
    {
        var roundTrip = RangeExpansionV2FeasibilityCostModel.EstimateRoundTripCostPercent(scenario);
        var fundingDrag = scenario.FundingRatePercentPerHour * (horizonMinutes / 60m);
        return Math.Round(roundTrip + fundingDrag, 6);
    }

    public static decimal ExpectedNetPercent(
        bool targetBeforeStop,
        bool stopBeforeTarget,
        decimal horizonReturnPercent,
        decimal targetPercent,
        decimal stopPercent,
        decimal totalCostPercent)
    {
        if (targetBeforeStop)
            return Math.Round(targetPercent - totalCostPercent, 6);
        if (stopBeforeTarget)
            return Math.Round(-stopPercent - totalCostPercent, 6);
        return Math.Round(horizonReturnPercent - totalCostPercent, 6);
    }

    public static FeasibilityCostScenario ResolveScenarioForTradeMode(LongShortTradeMode mode)
        => mode switch
        {
            LongShortTradeMode.SpotLongOnly => BuildStudyScenarios().First(s => s.Label == "spot-conservative"),
            _ => BuildStudyScenarios().First(s => s.Label == "futures-moderate")
        };
}
