using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Models.Decision;

public sealed class TrendAnalysisResult
{
    public bool IsValid { get; init; }
    public string Reason { get; init; } = string.Empty;
    public decimal CurrentShortMa { get; init; }
    public decimal CurrentLongMa { get; init; }
    public decimal PreviousShortMa { get; init; }
    public decimal PreviousLongMa { get; init; }
    public TrendState PreviousTrendState { get; init; } = TrendState.Neutral;
    public TrendState CurrentTrendState { get; init; } = TrendState.Neutral;
    public bool IsBullishCrossover { get; init; }
    public bool IsBearishCrossover { get; init; }
    public decimal MaDistancePercent { get; init; }
    public decimal ShortMaSlopePercent { get; init; }
    public decimal LongMaSlopePercent { get; init; }
    public decimal TrendStrengthPercent { get; init; }
    public bool IsTrendStrong { get; init; }
    public bool IsBullishTrendConfirmed { get; init; }
    public bool IsBearishTrendConfirmed { get; init; }
    public MarketRegime MarketRegime { get; init; } = MarketRegime.Unknown;
    public int ConfidenceScore { get; init; }
}
