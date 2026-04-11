using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class TrendStateService : ITrendStateService
{
    public int RequiredPeriods => 2;

    public TrendAnalysisResult Analyze(MarketSnapshot marketData, int shortPeriod, int longPeriod)
    {
        var closes = marketData.ClosePrices;
        var minimum = Math.Max(shortPeriod, longPeriod) + 1;
        if (closes.Count < minimum)
        {
            return new TrendAnalysisResult
            {
                IsValid = false,
                Reason = "MarketCondition.Unavailable: trend windows are warming up."
            };
        }

        var currentShort = closes.TakeLast(shortPeriod).Average();
        var currentLong = closes.TakeLast(longPeriod).Average();
        var previousShort = closes.Skip(closes.Count - shortPeriod - 1).Take(shortPeriod).Average();
        var previousLong = closes.Skip(closes.Count - longPeriod - 1).Take(longPeriod).Average();

        if (currentLong <= 0m || previousLong <= 0m || previousShort <= 0m)
        {
            return new TrendAnalysisResult
            {
                IsValid = false,
                Reason = "MarketCondition.Unavailable: invalid moving-average values."
            };
        }

        var currentTrend = currentShort > currentLong
            ? TrendState.Bullish
            : currentShort < currentLong
                ? TrendState.Bearish
                : TrendState.Neutral;

        var previousTrend = previousShort > previousLong
            ? TrendState.Bullish
            : previousShort < previousLong
                ? TrendState.Bearish
                : TrendState.Neutral;

        var bullishCross = previousShort <= previousLong && currentShort > currentLong;
        var bearishCross = previousShort >= previousLong && currentShort < currentLong;
        var distancePct = (currentShort - currentLong) / currentLong;
        var shortSlopePct = (currentShort - previousShort) / previousShort;

        return new TrendAnalysisResult
        {
            IsValid = true,
            CurrentShortMa = currentShort,
            CurrentLongMa = currentLong,
            PreviousShortMa = previousShort,
            PreviousLongMa = previousLong,
            PreviousTrendState = previousTrend,
            CurrentTrendState = currentTrend,
            IsBullishCrossover = bullishCross,
            IsBearishCrossover = bearishCross,
            MaDistancePercent = distancePct,
            ShortMaSlopePercent = shortSlopePct
        };
    }
}
