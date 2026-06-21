using Microsoft.Extensions.Configuration;

namespace TradingBot.Backtest;

public enum RangeExpansionTargetFloorMode
{
    Current,
    Relaxed,
    CostAware
}

public sealed record RangeExpansionCostMetrics
{
    public decimal EstimatedRoundTripCostPercent { get; init; }
    public decimal RequiredNetProfitPercent { get; init; }
    public decimal RequiredGrossProfitPercent { get; init; }
    public decimal? Lock90NetProfitPercent { get; init; }
    public decimal? Lock95NetProfitPercent { get; init; }
    public decimal? Lock98NetProfitPercent { get; init; }
    public bool Lock90ReachableAndNetProfitableWithin60m { get; init; }
    public bool Lock95ReachableAndNetProfitableWithin60m { get; init; }
    public bool Lock98ReachableAndNetProfitableWithin60m { get; init; }
    public bool ForwardMfe60NetTradable { get; init; }
    public decimal? MinExpectedMovePercentForCostAwareFloor { get; init; }
}

public static class RangeExpansionCostModel
{
    public const string TargetTooSmallButNetTradable = RangeExpansionBreakoutV1Model.TargetTooSmallButNetTradable;
    public const string TargetTooSmallAndFeeUntradable = RangeExpansionBreakoutV1Model.TargetTooSmallAndFeeUntradable;

    public static decimal ComputeRoundTripCostPercent(ExecutionCostSettings costs)
        => Math.Max(0m, (costs.FeeRatePercent * 2m) + costs.SpreadPercent + (costs.SlippagePercent * 2m));

    public static decimal ResolveRequiredNetProfitPercent(IConfiguration configuration)
        => Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinNetProfitPercent") ?? 0.05m);

    public static RangeExpansionTargetFloorMode ParseTargetFloorMode(IConfiguration configuration)
    {
        var raw = configuration.GetValue<string?>("Backtest:RangeExpansionBreakoutV1:TargetFloorMode");
        if (string.IsNullOrWhiteSpace(raw))
            return RangeExpansionTargetFloorMode.Current;
        return Enum.TryParse<RangeExpansionTargetFloorMode>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : RangeExpansionTargetFloorMode.Current;
    }

    public static decimal ResolveMinExpectedMovePercent(IConfiguration configuration, RangeExpansionTargetFloorMode mode)
    {
        var configured = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:MinExpectedMovePercent") ?? 0.10m;
        return mode switch
        {
            RangeExpansionTargetFloorMode.Relaxed => Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:RelaxedMinExpectedMovePercent") ?? 0.08m),
            _ => configured
        };
    }

    public static decimal? ComputeCostAwareMinExpectedMovePercent(
        decimal requiredGrossProfitPercent,
        decimal? profitLockThresholdPercent)
    {
        if (!profitLockThresholdPercent.HasValue || profitLockThresholdPercent.Value <= 0m)
            return null;

        return Math.Round(requiredGrossProfitPercent * 100m / profitLockThresholdPercent.Value, 6);
    }

    public static RangeExpansionCostMetrics Compute(
        ExecutionCostSettings costs,
        IConfiguration configuration,
        decimal? expectedMovePercent,
        decimal? lock90DistancePercent,
        decimal? lock95DistancePercent,
        decimal? lock98DistancePercent,
        decimal? forwardMfe60Percent,
        bool lock90ReachableWithin60m,
        bool lock95ReachableWithin60m,
        bool lock98ReachableWithin60m,
        decimal? profitLockThresholdPercent)
    {
        var roundTrip = ComputeRoundTripCostPercent(costs);
        var requiredNet = ResolveRequiredNetProfitPercent(configuration);
        var requiredGross = roundTrip + requiredNet;
        var lock90Net = lock90DistancePercent.HasValue ? lock90DistancePercent.Value - roundTrip : (decimal?)null;
        var lock95Net = lock95DistancePercent.HasValue ? lock95DistancePercent.Value - roundTrip : (decimal?)null;
        var lock98Net = lock98DistancePercent.HasValue ? lock98DistancePercent.Value - roundTrip : (decimal?)null;

        return new RangeExpansionCostMetrics
        {
            EstimatedRoundTripCostPercent = roundTrip,
            RequiredNetProfitPercent = requiredNet,
            RequiredGrossProfitPercent = requiredGross,
            Lock90NetProfitPercent = lock90Net,
            Lock95NetProfitPercent = lock95Net,
            Lock98NetProfitPercent = lock98Net,
            Lock90ReachableAndNetProfitableWithin60m = lock90ReachableWithin60m && lock90Net is not null && lock90Net.Value >= requiredNet,
            Lock95ReachableAndNetProfitableWithin60m = lock95ReachableWithin60m && lock95Net is not null && lock95Net.Value >= requiredNet,
            Lock98ReachableAndNetProfitableWithin60m = lock98ReachableWithin60m && lock98Net is not null && lock98Net.Value >= requiredNet,
            ForwardMfe60NetTradable = forwardMfe60Percent.HasValue && forwardMfe60Percent.Value >= requiredGross,
            MinExpectedMovePercentForCostAwareFloor = ComputeCostAwareMinExpectedMovePercent(requiredGross, profitLockThresholdPercent)
        };
    }

    public static string ClassifyTargetTooSmallRejection(RangeExpansionCostMetrics cost, decimal? lock90DistancePercent)
    {
        if (lock90DistancePercent.HasValue
            && cost.Lock90NetProfitPercent.HasValue
            && cost.Lock90NetProfitPercent.Value >= cost.RequiredNetProfitPercent)
        {
            return TargetTooSmallButNetTradable;
        }

        return TargetTooSmallAndFeeUntradable;
    }

    public static RangeExpansionCandidateRecord Apply(RangeExpansionCandidateRecord candidate, RangeExpansionCostMetrics cost)
        => candidate with
        {
            EstimatedRoundTripCostPercent = cost.EstimatedRoundTripCostPercent,
            RequiredNetProfitPercent = cost.RequiredNetProfitPercent,
            RequiredGrossProfitPercent = cost.RequiredGrossProfitPercent,
            Lock90NetProfitPercent = cost.Lock90NetProfitPercent,
            Lock95NetProfitPercent = cost.Lock95NetProfitPercent,
            Lock98NetProfitPercent = cost.Lock98NetProfitPercent,
            Lock90ReachableAndNetProfitableWithin60m = cost.Lock90ReachableAndNetProfitableWithin60m,
            Lock95ReachableAndNetProfitableWithin60m = cost.Lock95ReachableAndNetProfitableWithin60m,
            Lock98ReachableAndNetProfitableWithin60m = cost.Lock98ReachableAndNetProfitableWithin60m,
            ForwardMfe60NetTradable = cost.ForwardMfe60NetTradable
        };
}
