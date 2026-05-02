using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Application.DecisionEngine;

public class TrendStateService(
    ILogger<TrendStateService> logger,
    IOptions<TrendStateSettings> settings) : ITrendStateService
{
    private readonly TrendStateSettings _settings = Normalize(settings.Value);

    public int GetRequiredPeriods(int shortPeriod, int longPeriod)
    {
        var sanitizedShortPeriod = Math.Max(shortPeriod, 2);
        var sanitizedLongPeriod = Math.Max(longPeriod, sanitizedShortPeriod + 1);
        return Math.Max(sanitizedShortPeriod, sanitizedLongPeriod) + 1;
    }

    public TrendAnalysisResult Analyze(MarketSnapshot marketData, int shortPeriod, int longPeriod)
    {
        var sanitizedShortPeriod = Math.Max(shortPeriod, 2);
        var sanitizedLongPeriod = Math.Max(longPeriod, sanitizedShortPeriod + 1);
        var closes = marketData.ClosePrices;
        var minimum = GetRequiredPeriods(sanitizedShortPeriod, sanitizedLongPeriod);
        if (closes.Count < minimum)
        {
            return new TrendAnalysisResult
            {
                IsValid = false,
                Reason = "MarketCondition.Unavailable: trend windows are warming up.",
                MarketRegime = MarketRegime.Unknown
            };
        }

        var currentShort = closes.TakeLast(sanitizedShortPeriod).Average();
        var currentLong = closes.TakeLast(sanitizedLongPeriod).Average();
        var previousShort = closes.Skip(closes.Count - sanitizedShortPeriod - 1).Take(sanitizedShortPeriod).Average();
        var previousLong = closes.Skip(closes.Count - sanitizedLongPeriod - 1).Take(sanitizedLongPeriod).Average();

        if (currentLong <= 0m || previousLong <= 0m || previousShort <= 0m)
        {
            return new TrendAnalysisResult
            {
                IsValid = false,
                Reason = "MarketCondition.Unavailable: invalid moving-average values.",
                MarketRegime = MarketRegime.Unknown
            };
        }

        var previousTrend = previousShort > previousLong
            ? TrendState.Bullish
            : previousShort < previousLong
                ? TrendState.Bearish
                : TrendState.Neutral;

        var bullishCross = previousShort <= previousLong && currentShort > currentLong;
        var bearishCross = previousShort >= previousLong && currentShort < currentLong;
        var distancePct = (currentShort - currentLong) / currentLong;
        var shortSlopePct = (currentShort - previousShort) / previousShort;
        var longSlopePct = (currentLong - previousLong) / previousLong;
        var trendStrengthPct = Math.Abs(currentShort - currentLong) / currentLong;
        var isTrendStrong = trendStrengthPct >= _settings.StrongTrendStrengthPercent;

        var isBullishConfirmed =
            currentShort > currentLong &&
            shortSlopePct > _settings.MinSlopePercent &&
            longSlopePct >= 0m &&
            trendStrengthPct >= _settings.MinTrendStrengthPercent;

        var isBearishConfirmed =
            currentShort < currentLong &&
            shortSlopePct < -_settings.MinSlopePercent &&
            longSlopePct <= 0m &&
            trendStrengthPct >= _settings.MinTrendStrengthPercent;

        var currentTrend = isBullishConfirmed
            ? TrendState.Bullish
            : isBearishConfirmed
                ? TrendState.Bearish
                : TrendState.Neutral;

        var marketRegime = DetermineMarketRegime(marketData, trendStrengthPct, isBullishConfirmed, isBearishConfirmed);
        var confidenceScore = CalculateConfidenceScore(
            currentShort,
            currentLong,
            shortSlopePct,
            longSlopePct,
            trendStrengthPct,
            bullishCross,
            bearishCross);

        logger.LogInformation(
            "TrendState Analyze metrics: currentShort={currentShort}, currentLong={currentLong}, previousShort={previousShort}, previousLong={previousLong}, trendStrengthPct={trendStrengthPct}, shortSlopePct={shortSlopePct}, longSlopePct={longSlopePct}, bullishCross={bullishCross}, bearishCross={bearishCross}, currentTrend={currentTrend}, marketRegime={marketRegime}, confidenceScore={confidenceScore}",
            currentShort,
            currentLong,
            previousShort,
            previousLong,
            trendStrengthPct,
            shortSlopePct,
            longSlopePct,
            bullishCross,
            bearishCross,
            currentTrend,
            marketRegime,
            confidenceScore);

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
            ShortMaSlopePercent = shortSlopePct,
            LongMaSlopePercent = longSlopePct,
            TrendStrengthPercent = trendStrengthPct,
            IsTrendStrong = isTrendStrong,
            IsBullishTrendConfirmed = isBullishConfirmed,
            IsBearishTrendConfirmed = isBearishConfirmed,
            MarketRegime = marketRegime,
            ConfidenceScore = confidenceScore
        };
    }

    private MarketRegime DetermineMarketRegime(
        MarketSnapshot marketData,
        decimal trendStrengthPct,
        bool isBullishConfirmed,
        bool isBearishConfirmed)
    {
        if (HasLowVolatility(marketData))
            return MarketRegime.LowVolatility;

        if (isBullishConfirmed || isBearishConfirmed)
            return MarketRegime.Trending;

        if (trendStrengthPct < _settings.MinTrendStrengthPercent)
            return MarketRegime.Ranging;

        return MarketRegime.Ranging;
    }

    private bool HasLowVolatility(MarketSnapshot marketData)
    {
        var highPrices = marketData.HighPrices;
        var lowPrices = marketData.LowPrices;
        var closePrices = marketData.ClosePrices;

        var hasRangeData = highPrices.Count >= _settings.MinRangeCandlesForVolatilityCheck &&
                           lowPrices.Count >= _settings.MinRangeCandlesForVolatilityCheck &&
                           closePrices.Count >= _settings.MinRangeCandlesForVolatilityCheck;
        if (!hasRangeData)
        {
            // TODO: Use a dedicated volatility metric from MarketSnapshot once available.
            return false;
        }

        var highs = highPrices.TakeLast(_settings.MinRangeCandlesForVolatilityCheck).ToArray();
        var lows = lowPrices.TakeLast(_settings.MinRangeCandlesForVolatilityCheck).ToArray();
        var closes = closePrices.TakeLast(_settings.MinRangeCandlesForVolatilityCheck).ToArray();
        var totalRangePercent = 0m;
        var validCount = 0;

        for (var i = 0; i < highs.Length; i++)
        {
            var close = closes[i];
            if (close <= 0m)
                continue;

            totalRangePercent += Math.Abs(highs[i] - lows[i]) / close;
            validCount++;
        }

        if (validCount == 0)
            return false;

        var averageRangePercent = totalRangePercent / validCount;
        return averageRangePercent < _settings.LowVolatilityRangePercentThreshold;
    }

    private int CalculateConfidenceScore(
        decimal currentShort,
        decimal currentLong,
        decimal shortSlopePct,
        decimal longSlopePct,
        decimal trendStrengthPct,
        bool bullishCross,
        bool bearishCross)
    {
        var score = 0;

        var bullishDirection = currentShort > currentLong;
        var bearishDirection = currentShort < currentLong;
        var hasDirection = bullishDirection || bearishDirection;

        if (hasDirection)
            score += 10;

        if (trendStrengthPct >= _settings.MinTrendStrengthPercent)
            score += 20;

        if (trendStrengthPct >= _settings.StrongTrendStrengthPercent)
            score += 10;

        if (bullishDirection && shortSlopePct > _settings.MinSlopePercent)
            score += 20;
        else if (bearishDirection && shortSlopePct < -_settings.MinSlopePercent)
            score += 20;

        if (bullishDirection && longSlopePct >= 0m)
            score += 20;
        else if (bearishDirection && longSlopePct <= 0m)
            score += 20;

        if ((bullishDirection && bullishCross) || (bearishDirection && bearishCross))
            score += 20;

        return Math.Clamp(score, 0, 100);
    }

    private static TrendStateSettings Normalize(TrendStateSettings settings)
    {
        return new TrendStateSettings
        {
            MinTrendStrengthPercent = Math.Max(settings.MinTrendStrengthPercent, 0m),
            StrongTrendStrengthPercent = Math.Max(settings.StrongTrendStrengthPercent, 0m),
            MinSlopePercent = Math.Max(settings.MinSlopePercent, 0m),
            LowVolatilityRangePercentThreshold = Math.Max(settings.LowVolatilityRangePercentThreshold, 0m),
            MinRangeCandlesForVolatilityCheck = Math.Max(settings.MinRangeCandlesForVolatilityCheck, 2)
        };
    }
}
