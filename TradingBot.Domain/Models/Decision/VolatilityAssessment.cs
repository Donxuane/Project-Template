namespace TradingBot.Domain.Models.Decision;

public sealed class VolatilityAssessment
{
    public bool IsValid { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal CurrentVolatility { get; init; }
    public decimal RollingAverageVolatility { get; init; }
    public int ReturnsUsed { get; init; }
}
