using Xunit;

namespace TradingBot.Application.Tests;

public class ExpectedTargetExpectancyAuditTests
{
    private const decimal MinExpectedMovePercent = 0.35m;
    private const decimal MinNetProfitPercent = 0.20m;
    private const decimal SpreadPercent = 0.05m;

    [Fact]
    public void NormalTrendExpectedTarget_EthLikeTightRange_IsBelowMinExpectedMoveThreshold()
    {
        var entryPrice = 2060.42m;
        var result = ExpectedTargetFormulaAudit.CalculateNormalTrendExpectedTarget(
            entryPrice,
            recentRangeHigh: 2064.00m,
            recentRangeLow: 2056.00m,
            atr: 2.50m,
            lookbackCandles: 8);

        Assert.True(result.ExpectedMovePercent < MinExpectedMovePercent);
        Assert.InRange(result.ExpectedMovePercent, 0.15m, 0.30m);
    }

    [Fact]
    public void NormalTrendExpectedTarget_AtSwingHigh_ProducesVerySmallExpectedMove()
    {
        var entryPrice = 2064.00m;
        var result = ExpectedTargetFormulaAudit.CalculateNormalTrendExpectedTarget(
            entryPrice,
            recentRangeHigh: 2064.00m,
            recentRangeLow: 2056.00m,
            atr: 2.50m,
            lookbackCandles: 8);

        Assert.True(result.ExpectedMovePercent < 0.10m);
        Assert.True(result.ExpectedMovePercent < MinExpectedMovePercent);
    }

    [Fact]
    public void NormalTrendExpectedTarget_DefaultProjection_PreservesLegacyConservativeBehavior()
    {
        var entryPrice = 2060.42m;
        var baseline = ExpectedTargetFormulaAudit.CalculateNormalTrendExpectedTarget(
            entryPrice,
            recentRangeHigh: 2064.00m,
            recentRangeLow: 2056.00m,
            atr: 2.50m,
            lookbackCandles: 8);
        var explicitLegacy = ExpectedTargetFormulaAudit.CalculateNormalTrendExpectedTarget(
            entryPrice,
            recentRangeHigh: 2064.00m,
            recentRangeLow: 2056.00m,
            atr: 2.50m,
            lookbackCandles: 8,
            atrExtensionMultiplier: 0.35m,
            structureExtensionMultiplier: 0.35m,
            useMinAtrStructureExtension: true);

        Assert.Equal(baseline.ExpectedTargetPrice, explicitLegacy.ExpectedTargetPrice);
        Assert.Equal(baseline.ExpectedMovePercent, explicitLegacy.ExpectedMovePercent);
        Assert.Equal(baseline.StructureExtensionUsed, explicitLegacy.StructureExtensionUsed);
    }

    [Fact]
    public void NormalTrendExpectedTarget_CustomMultipliersWithMaxMode_ProducesLargerProjection()
    {
        var entryPrice = 631.00m;
        var conservative = ExpectedTargetFormulaAudit.CalculateNormalTrendExpectedTarget(
            entryPrice,
            recentRangeHigh: 632.00m,
            recentRangeLow: 624.00m,
            atr: 0.40m,
            lookbackCandles: 8);
        var structureAware = ExpectedTargetFormulaAudit.CalculateNormalTrendExpectedTarget(
            entryPrice,
            recentRangeHigh: 632.00m,
            recentRangeLow: 624.00m,
            atr: 0.40m,
            lookbackCandles: 8,
            atrExtensionMultiplier: 0.35m,
            structureExtensionMultiplier: 0.60m,
            useMinAtrStructureExtension: false);

        Assert.True(structureAware.ExpectedTargetPrice > conservative.ExpectedTargetPrice);
        Assert.True(structureAware.ExpectedMovePercent > conservative.ExpectedMovePercent);
    }

    [Fact]
    public void NormalTrendExpectedTarget_InvalidCurrentPrice_ReturnsSafeZeroValues()
    {
        var result = ExpectedTargetFormulaAudit.CalculateNormalTrendExpectedTarget(
            currentPrice: 0m,
            recentRangeHigh: 0m,
            recentRangeLow: 0m,
            atr: 0m,
            lookbackCandles: 8);

        Assert.Equal(0m, result.ExpectedTargetPrice);
        Assert.Equal(0m, result.ExpectedMovePercent);
        Assert.Equal(0m, result.StructureExtensionUsed);
        Assert.Null(result.AtrUsed);
    }

    [Fact]
    public void LowVolBreakoutExpectedTarget_WhenAlreadyExtended_ProducesSmallExpectedMove()
    {
        var entryPrice = 2068.50m;
        var result = ExpectedTargetFormulaAudit.CalculateLowVolBreakoutExpectedTarget(
            entryPrice,
            breakoutThresholdPrice: 2064.41m,
            recentRangeHigh: 2064.00m,
            recentRangeLow: 2056.00m);

        Assert.True(result.ExpectedMovePercent < MinExpectedMovePercent);
        Assert.InRange(result.ExpectedMovePercent, 0.05m, 0.15m);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(15)]
    public void HistoricalForwardMoveAudit_MostPredictionsAndForwardExcursionsStayBelowThreshold(int forwardCandles)
    {
        var closes = ExpectedTargetFormulaAudit.BuildEthLikeOneMinuteCloses();
        var highs = ExpectedTargetFormulaAudit.BuildEthLikeOneMinuteHighs(closes);
        var summary = ExpectedTargetFormulaAudit.SummarizeForwardExcursion(
            closes,
            highs,
            forwardCandles,
            atr: 2.5m,
            lookbackCandles: 8);

        Assert.True(summary.Samples > 0);
        Assert.True(summary.PredictedBelowThresholdRate >= 0.70m);
        Assert.True(summary.ForwardMfeBelowThresholdRate >= 0.55m);
        Assert.True(summary.MedianPredictedNormalTrendMovePercent < MinExpectedMovePercent);
        Assert.True(summary.MedianForwardMfePercent < MinExpectedMovePercent);
    }

    [Fact]
    public void AuditRecommendation_CurrentThresholdBlocksMostConservativePredictions()
    {
        var closes = ExpectedTargetFormulaAudit.BuildEthLikeOneMinuteCloses();
        var highs = ExpectedTargetFormulaAudit.BuildEthLikeOneMinuteHighs(closes);
        var summary = ExpectedTargetFormulaAudit.SummarizeForwardExcursion(
            closes,
            highs,
            forwardCandles: 10,
            atr: 2.5m,
            lookbackCandles: 8);

        var netAfterSpread = summary.MedianPredictedNormalTrendMovePercent - SpreadPercent;
        Assert.True(summary.PredictedBelowThresholdRate >= 0.70m);
        Assert.True(summary.ForwardMfeBelowThresholdRate >= 0.55m);
        Assert.True(netAfterSpread < MinNetProfitPercent || summary.MedianPredictedNormalTrendMovePercent < MinExpectedMovePercent);
        Assert.True(summary.MedianForwardMfePercent < MinExpectedMovePercent);
    }
}

internal static class ExpectedTargetFormulaAudit
{
    public sealed record ExpectedTargetAuditResult(
        decimal ExpectedTargetPrice,
        decimal ExpectedMovePercent,
        decimal StructureExtensionUsed,
        decimal? AtrUsed);

    public sealed record ForwardExcursionSummary(
        int Samples,
        decimal MedianPredictedNormalTrendMovePercent,
        decimal MedianForwardMfePercent,
        decimal PredictedBelowThresholdRate,
        decimal ForwardMfeBelowThresholdRate);

    public static ExpectedTargetAuditResult CalculateNormalTrendExpectedTarget(
        decimal currentPrice,
        decimal recentRangeHigh,
        decimal recentRangeLow,
        decimal atr,
        int lookbackCandles,
        decimal atrExtensionMultiplier = 0.35m,
        decimal structureExtensionMultiplier = 0.35m,
        bool useMinAtrStructureExtension = true)
    {
        if (currentPrice <= 0m)
            return new ExpectedTargetAuditResult(0m, 0m, 0m, null);

        _ = lookbackCandles;
        var structureRange = Math.Max(0m, recentRangeHigh - recentRangeLow);
        var atrExtension = atr > 0m ? atr * Math.Max(0m, atrExtensionMultiplier) : 0m;
        var structureExtension = structureRange > 0m
            ? structureRange * Math.Max(0m, structureExtensionMultiplier)
            : currentPrice * 0.0015m;
        var projectedExtension = useMinAtrStructureExtension
            ? (atr > 0m
                ? Math.Min(atrExtension, structureRange > 0m ? structureRange * 0.5m : atrExtension)
                : structureExtension)
            : (atr > 0m ? Math.Max(atrExtension, structureExtension) : structureExtension);

        var swingReference = Math.Max(recentRangeHigh, currentPrice);
        var expectedTargetPrice = Math.Max(swingReference + projectedExtension, currentPrice);
        var expectedMovePercent = expectedTargetPrice > currentPrice
            ? ((expectedTargetPrice - currentPrice) / currentPrice) * 100m
            : 0m;

        return new ExpectedTargetAuditResult(
            expectedTargetPrice,
            expectedMovePercent,
            projectedExtension,
            atr > 0m ? atr : null);
    }

    public static ExpectedTargetAuditResult CalculateLowVolBreakoutExpectedTarget(
        decimal currentPrice,
        decimal breakoutThresholdPrice,
        decimal recentRangeHigh,
        decimal recentRangeLow)
    {
        var structureRange = Math.Max(0m, recentRangeHigh - recentRangeLow);
        var conservativeExtension = structureRange * 0.5m;
        var baselineTarget = breakoutThresholdPrice + conservativeExtension;
        var expectedTargetPrice = baselineTarget;

        if (currentPrice > baselineTarget && structureRange > 0m)
            expectedTargetPrice = currentPrice + (structureRange * 0.25m);

        expectedTargetPrice = Math.Max(expectedTargetPrice, currentPrice);
        var expectedMovePercent = expectedTargetPrice > currentPrice
            ? ((expectedTargetPrice - currentPrice) / currentPrice) * 100m
            : 0m;

        return new ExpectedTargetAuditResult(
            expectedTargetPrice,
            expectedMovePercent,
            conservativeExtension,
            null);
    }

    public static decimal CalculateMaxForwardMfePercent(
        IReadOnlyList<decimal> highs,
        int entryIndex,
        int forwardCandles,
        decimal entryPrice)
    {
        if (entryPrice <= 0m || entryIndex < 0 || entryIndex >= highs.Count)
            return 0m;

        var end = Math.Min(highs.Count - 1, entryIndex + forwardCandles);
        var maxHigh = entryPrice;
        for (var i = entryIndex + 1; i <= end; i++)
            maxHigh = Math.Max(maxHigh, highs[i]);

        return ((maxHigh - entryPrice) / entryPrice) * 100m;
    }

    public static ForwardExcursionSummary SummarizeForwardExcursion(
        IReadOnlyList<decimal> closes,
        IReadOnlyList<decimal> highs,
        int forwardCandles,
        decimal atr,
        int lookbackCandles)
    {
        var predictedMoves = new List<decimal>();
        var forwardMfes = new List<decimal>();
        var belowThreshold = 0;
        var forwardBelowThreshold = 0;

        for (var i = lookbackCandles; i < closes.Count - forwardCandles; i++)
        {
            var windowHighs = highs.Skip(i - lookbackCandles).Take(lookbackCandles).ToArray();
            var windowLows = closes.Skip(i - lookbackCandles).Take(lookbackCandles).Select(x => x - 2m).ToArray();
            var entryPrice = closes[i];
            var predicted = CalculateNormalTrendExpectedTarget(
                entryPrice,
                windowHighs.Max(),
                windowLows.Min(),
                atr,
                lookbackCandles);
            var forwardMfe = CalculateMaxForwardMfePercent(highs, i, forwardCandles, entryPrice);

            predictedMoves.Add(predicted.ExpectedMovePercent);
            forwardMfes.Add(forwardMfe);
            if (predicted.ExpectedMovePercent < 0.35m)
                belowThreshold++;
            if (forwardMfe < 0.35m)
                forwardBelowThreshold++;
        }

        predictedMoves.Sort();
        forwardMfes.Sort();
        var samples = predictedMoves.Count;
        return new ForwardExcursionSummary(
            samples,
            predictedMoves[samples / 2],
            forwardMfes[samples / 2],
            samples == 0 ? 0m : (decimal)belowThreshold / samples,
            samples == 0 ? 0m : (decimal)forwardBelowThreshold / samples);
    }

    public static decimal[] BuildEthLikeOneMinuteCloses()
    {
        return
        [
            2054.2m, 2055.1m, 2056.0m, 2055.4m, 2057.2m, 2058.0m, 2057.5m, 2059.1m,
            2060.0m, 2059.4m, 2061.2m, 2060.8m, 2062.0m, 2061.5m, 2063.0m, 2062.4m,
            2064.0m, 2063.2m, 2062.8m, 2063.6m, 2064.2m, 2063.9m, 2062.5m, 2061.8m,
            2060.9m, 2061.4m, 2062.1m, 2061.0m, 2060.4m, 2059.8m, 2060.2m, 2061.1m,
            2062.0m, 2061.6m, 2062.8m, 2063.4m, 2062.9m, 2064.1m, 2063.7m, 2062.2m
        ];
    }

    public static decimal[] BuildEthLikeOneMinuteHighs(IReadOnlyList<decimal> closes)
    {
        return closes.Select(c => c + 1.2m).ToArray();
    }
}
