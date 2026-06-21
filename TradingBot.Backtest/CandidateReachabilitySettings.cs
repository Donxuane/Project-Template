using Microsoft.Extensions.Configuration;

namespace TradingBot.Backtest;

public sealed class CandidateReachabilitySettings
{
    public bool Enabled { get; init; }
    public decimal ExpectedMoveInflatedMultiplier { get; init; } = 1.0m;
    public int ForwardHorizonMinutes { get; init; } = 60;
    public decimal MinFavorableMfePercent { get; init; } = 0.05m;
    public decimal MaxAcceptableForwardMae60Percent { get; init; } = 0.30m;

    public static CandidateReachabilitySettings FromConfiguration(IConfiguration configuration)
    {
        return new CandidateReachabilitySettings
        {
            Enabled = configuration.GetValue<bool?>("Backtest:ReachabilityResearch:Enabled") ?? false,
            ExpectedMoveInflatedMultiplier = Math.Max(0.1m, configuration.GetValue<decimal?>("Backtest:ReachabilityResearch:ExpectedMoveInflatedMultiplier") ?? 1.0m),
            ForwardHorizonMinutes = Math.Clamp(configuration.GetValue<int?>("Backtest:ReachabilityResearch:ForwardHorizonMinutes") ?? 60, 15, 240),
            MinFavorableMfePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ReachabilityResearch:MinFavorableMfePercent") ?? 0.05m),
            MaxAcceptableForwardMae60Percent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ReachabilityResearch:MaxAcceptableForwardMae60Percent") ?? 0.30m)
        };
    }
}

public sealed class ReachabilityConfidenceRelaxationSettings
{
    public bool Enabled { get; init; }
    public decimal MaxLock90DistancePercent { get; init; } = 0.40m;
    public decimal MaxDistanceToInvalidationPercent { get; init; } = 0.40m;
    public decimal RelaxedMinConfidence { get; init; } = 0.65m;

    public static ReachabilityConfidenceRelaxationSettings FromConfiguration(IConfiguration configuration)
    {
        return new ReachabilityConfidenceRelaxationSettings
        {
            Enabled = configuration.GetValue<bool?>("Backtest:ReachabilityConfidenceRelaxation:Enabled") ?? false,
            MaxLock90DistancePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ReachabilityConfidenceRelaxation:MaxLock90DistancePercent") ?? 0.40m),
            MaxDistanceToInvalidationPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ReachabilityConfidenceRelaxation:MaxDistanceToInvalidationPercent") ?? 0.40m),
            RelaxedMinConfidence = Math.Clamp(configuration.GetValue<decimal?>("Backtest:ReachabilityConfidenceRelaxation:RelaxedMinConfidence") ?? 0.65m, 0m, 1m)
        };
    }
}
