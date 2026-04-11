using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class MarketConditionResult
{
    public bool IsValid { get; init; }
    public MarketConditionAvailability Availability { get; init; } = MarketConditionAvailability.Unavailable;
    public string Reason { get; init; } = string.Empty;
    public VolatilityRegime Regime { get; init; } = VolatilityRegime.Normal;
    public bool IsBreakout { get; init; }
    public bool HasVolumeSpike { get; init; }
    public bool AllowTrade { get; init; }
    public decimal CurrentVolatility { get; init; }
    public decimal RollingAverageVolatility { get; init; }
    public decimal Atr { get; init; }
    public decimal NormalizedAtr { get; init; }
    public decimal SymbolSensitivity { get; init; } = 1.0m;
}
