namespace TradingBot.Application.DecisionEngine;

public class TrendStateSettings
{
    public const string SectionName = "DecisionEngine:TrendState";

    public decimal MinTrendStrengthPercent { get; set; } = 0.001m;
    public decimal StrongTrendStrengthPercent { get; set; } = 0.003m;
    public decimal MinSlopePercent { get; set; } = 0.0005m;
    public decimal LowVolatilityRangePercentThreshold { get; set; } = 0.0008m;
    public int MinRangeCandlesForVolatilityCheck { get; set; } = 10;
}
