namespace TradingBot.Backtest;

public static class RangeExpansionCostSensitivity
{
    public sealed record CostScenario(string Label, decimal FeeRatePercent, decimal SpreadPercent);

    public static readonly CostScenario CurrentDefault = new("current", 0.10m, 0.05m);
    public static readonly CostScenario LowerFee075 = new("fee-0.075-spread-0.05", 0.075m, 0.05m);
    public static readonly CostScenario LowerFee050 = new("fee-0.05-spread-0.05", 0.05m, 0.05m);
    public static readonly CostScenario TighterSpread003 = new("fee-0.10-spread-0.03", 0.10m, 0.03m);
    public static readonly CostScenario Optimistic = new("fee-0.05-spread-0.03", 0.05m, 0.03m);

    public static IReadOnlyList<CostScenario> StandardScenarios { get; } =
    [
        CurrentDefault,
        LowerFee075,
        LowerFee050,
        TighterSpread003,
        Optimistic
    ];

    public static decimal RecalculateNetPnl(SimulatedTrade trade, CostScenario scenario)
        => RecalculateNetPnl(trade, scenario.FeeRatePercent, scenario.SpreadPercent);

    public static decimal RecalculateNetPnl(
        SimulatedTrade trade,
        decimal feeRatePercent,
        decimal spreadPercent)
    {
        var entryNotional = trade.EntryPrice * trade.Quantity;
        var exitNotional = trade.ExitPrice * trade.Quantity;
        var feeEstimate = (entryNotional + exitNotional) * (feeRatePercent / 100m);
        var spreadEstimate = (entryNotional + exitNotional) * (spreadPercent / 100m) / 2m;
        return trade.GrossPnlQuote - (feeEstimate + spreadEstimate);
    }
}
