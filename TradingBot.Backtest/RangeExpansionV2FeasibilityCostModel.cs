namespace TradingBot.Backtest;

public sealed record FeasibilityCostScenario(
    string Label,
    string MarketMode,
    decimal FeeRatePercent,
    decimal SpreadPercent,
    decimal SlippagePercent,
    decimal FundingRatePercentPerHour);

public static class RangeExpansionV2FeasibilityCostModel
{
    public const decimal SpotConservativeFee = 0.10m;
    public const decimal SpotConservativeSpread = 0.05m;

    public static IReadOnlyList<FeasibilityCostScenario> BuildStandardScenarios()
    {
        var scenarios = new List<FeasibilityCostScenario>
        {
            new("spot-conservative", "spot", 0.10m, 0.05m, 0m, 0m),
            new("spot-lower-fee", "spot", 0.075m, 0.05m, 0m, 0m),
            new("spot-low-fee", "spot", 0.05m, 0.03m, 0m, 0m),
            new("spot-maker-like", "spot", 0.02m, 0.02m, 0m, 0m)
        };

        foreach (var slippage in new[] { 0m, 0.02m, 0.05m })
        {
            scenarios.Add(new(
                $"spot-low-fee-slip-{slippage:0.00}".Replace('.', '-'),
                "spot",
                0.05m,
                0.03m,
                slippage,
                0m));
        }

        scenarios.Add(new("futures-sim-low", "futures-sim", 0.02m, 0.02m, 0.02m, 0.005m));
        scenarios.Add(new("futures-sim-moderate", "futures-sim", 0.05m, 0.03m, 0.05m, 0.01m));
        scenarios.Add(new("futures-sim-taker", "futures-sim", 0.05m, 0.03m, 0.05m, 0.015m));

        return scenarios;
    }

    public static decimal EstimateRoundTripCostPercent(FeasibilityCostScenario scenario)
        => (scenario.FeeRatePercent * 2m) + scenario.SpreadPercent + (scenario.SlippagePercent * 2m);

    public static decimal RecalculateNetPnl(SimulatedTrade trade, FeasibilityCostScenario scenario)
    {
        var entryNotional = trade.EntryPrice * trade.Quantity;
        var exitNotional = trade.ExitPrice * trade.Quantity;
        var feeEstimate = (entryNotional + exitNotional) * (scenario.FeeRatePercent / 100m);
        var spreadEstimate = (entryNotional + exitNotional) * (scenario.SpreadPercent / 100m) / 2m;
        var slippageEstimate = (entryNotional + exitNotional) * (scenario.SlippagePercent / 100m);
        var fundingEstimate = entryNotional * (scenario.FundingRatePercentPerHour / 100m)
            * (trade.DurationMinutes / 60m);
        return trade.GrossPnlQuote - feeEstimate - spreadEstimate - slippageEstimate - fundingEstimate;
    }

    public static decimal SumNetPnl(IReadOnlyList<SimulatedTrade> trades, FeasibilityCostScenario scenario)
        => trades.Sum(t => RecalculateNetPnl(t, scenario));

    public static decimal FindBreakEvenRoundTripCostPercent(
        IReadOnlyList<SimulatedTrade> trades,
        decimal baselineFeeRatePercent,
        decimal baselineSpreadPercent,
        decimal baselineSlippagePercent = 0m)
    {
        if (trades.Count == 0)
            return 0m;

        var grossNet = SumNetPnl(trades, new FeasibilityCostScenario(
            "zero-cost", "spot", 0m, 0m, 0m, 0m));
        if (grossNet <= 0m)
            return 0m;

        var baselineRoundTrip = (baselineFeeRatePercent * 2m) + baselineSpreadPercent + (baselineSlippagePercent * 2m);
        if (baselineRoundTrip <= 0m)
            return grossNet > 0m ? decimal.MaxValue : 0m;

        decimal low = 0m;
        decimal high = Math.Max(baselineRoundTrip * 3m, 0.50m);
        while (SumNetAtRoundTrip(trades, high, baselineFeeRatePercent, baselineSpreadPercent, baselineSlippagePercent) > 0m)
        {
            high *= 2m;
            if (high > 5m)
                break;
        }

        for (var i = 0; i < 40; i++)
        {
            var mid = (low + high) / 2m;
            if (SumNetAtRoundTrip(trades, mid, baselineFeeRatePercent, baselineSpreadPercent, baselineSlippagePercent) >= 0m)
                low = mid;
            else
                high = mid;
        }

        return Math.Round(low, 6);
    }

    private static decimal SumNetAtRoundTrip(
        IReadOnlyList<SimulatedTrade> trades,
        decimal roundTripCostPercent,
        decimal baselineFeeRatePercent,
        decimal baselineSpreadPercent,
        decimal baselineSlippagePercent)
    {
        var baselineRoundTrip = (baselineFeeRatePercent * 2m) + baselineSpreadPercent + (baselineSlippagePercent * 2m);
        if (baselineRoundTrip <= 0m)
            return trades.Sum(t => t.GrossPnlQuote);

        var scale = roundTripCostPercent / baselineRoundTrip;
        var scenario = new FeasibilityCostScenario(
            "scaled",
            "spot",
            baselineFeeRatePercent * scale,
            baselineSpreadPercent * scale,
            baselineSlippagePercent * scale,
            0m);
        return SumNetPnl(trades, scenario);
    }

    public static bool IsUnrealisticallyLowCost(FeasibilityCostScenario scenario)
        => scenario.FeeRatePercent <= 0.02m
           && scenario.SpreadPercent <= 0.02m
           && scenario.SlippagePercent <= 0.01m
           && scenario.FundingRatePercentPerHour <= 0.005m;

    public static bool IsRealisticLowerCost(FeasibilityCostScenario scenario)
    {
        if (string.Equals(scenario.MarketMode, "futures-sim", StringComparison.OrdinalIgnoreCase))
            return scenario.FeeRatePercent <= 0.05m
                   && scenario.SpreadPercent <= 0.03m
                   && scenario.SlippagePercent <= 0.05m;

        return scenario.FeeRatePercent <= 0.075m
               && scenario.SpreadPercent <= 0.05m
               && scenario.SlippagePercent <= 0.03m
               && scenario.FundingRatePercentPerHour == 0m;
    }
}
