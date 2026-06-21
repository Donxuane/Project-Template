using TradingBot.Backtest;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.Application.Tests;

public class FuturesDirectionalRuleDiscoveryV2Tests
{
    [Fact]
    public void ConfigMatrix_Has48ControlledVariants()
    {
        var matrix = FuturesDirectionalRuleDiscoveryV2Catalog.BuildConfigMatrix();
        Assert.Equal(48, matrix.Count);
        Assert.Contains(matrix, c => c.Label == FuturesDirectionalRuleDiscoveryV2Catalog.PrimaryConfig.Label);
        Assert.Equal(matrix.Count, matrix.Select(c => c.Label).Distinct().Count());
    }

    [Fact]
    public void ComputeTertiles_ProducesValidBoundsForSufficientData()
    {
        var points = Enumerable.Range(0, 120)
            .Select(i => MakePoint(DateTime.UtcNow, atr: i * 0.01m))
            .ToList();

        var bounds = FuturesDirectionalRuleDiscoveryV2Catalog.ComputeTertiles(
            points, nameof(MarketRegimeForwardEdgeScanner.RegimeCandleFeatures.AtrPercent));

        Assert.True(bounds.Valid);
        Assert.True(bounds.B66 > bounds.B33);
    }

    [Fact]
    public void BuildSingleFeatureRules_GeneratesNumericAndCategoricalRules()
    {
        var points = Enumerable.Range(0, 120)
            .Select(i => MakePoint(DateTime.UtcNow, atr: i * 0.01m, regime: i % 2 == 0 ? "Normal" : "Elevated"))
            .ToList();
        var tertiles = new Dictionary<string, FuturesDirectionalRuleDiscoveryV2Catalog.TertileBounds>(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in FuturesDirectionalRuleDiscoveryV2Catalog.NumericRuleFeatures)
            tertiles[feature] = FuturesDirectionalRuleDiscoveryV2Catalog.ComputeTertiles(points, feature);

        var rules = FuturesDirectionalRuleDiscoveryV2Catalog.BuildSingleFeatureRules(points, tertiles);

        Assert.Contains(rules, r => r.Description.StartsWith("AtrPercent Q"));
        Assert.Contains(rules, r => r.Description.Contains("VolatilityRegime="));
    }

    [Fact]
    public void ScanCombo_RunsEndToEndWithoutThrowing()
    {
        var oneMinute = BuildSyntheticOneMinute(4000);
        var intervalCandles = CandleAggregator.Aggregate(TradingSymbol.BNBUSDT, oneMinute, "1m", "5m").Candles;
        var basePoints = FuturesDirectionalRuleDiscoveryV2Engine.BuildBasePoints(
            "5m", intervalCandles, null, null, CancellationToken.None);
        Assert.NotEmpty(basePoints);

        var dataEnd = oneMinute[^1].OpenTimeUtc;
        var result = FuturesDirectionalRuleDiscoveryV2Engine.ScanCombo(
            TradingSymbol.BNBUSDT, "5m", LongShortDirection.Short,
            basePoints, intervalCandles, oneMinute, dataEnd, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result.CandidateCount >= 0);
        Assert.All(result.Candidates, c => Assert.False(c.UsesFutureInformation));
    }

    private static DiscoveryBasePoint MakePoint(DateTime entry, decimal atr, string regime = "Normal")
        => new()
        {
            SignalIndex = 0,
            SignalTimeUtc = entry,
            EntryTimeUtc = entry,
            EntryPriceNextOpen = 100m,
            EntryPriceNextClose = 100m,
            Features = new MarketRegimeForwardEdgeScanner.RegimeCandleFeatures
            {
                AtrPercent = atr,
                VolatilityRegime = regime,
                DistanceFromRecentHighPercent = atr * 0.5m,
                DistanceFromRecentLowPercent = atr * 0.3m,
                RangeWidthPercent = atr,
                TrendSlopePercent = atr - 0.5m
            },
            HourOfDayUtc = entry.Hour,
            DayOfWeek = entry.DayOfWeek.ToString(),
            SessionBucket = "US"
        };

    private static IReadOnlyList<KlineCandle> BuildSyntheticOneMinute(int count)
    {
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var candles = new List<KlineCandle>(count);
        var price = 100m;
        for (var i = 0; i < count; i++)
        {
            var wave = (decimal)Math.Sin(i / 50.0) * 0.5m;
            var open = price;
            var close = price + wave + (i % 7 - 3) * 0.02m;
            var high = Math.Max(open, close) + 0.1m;
            var low = Math.Min(open, close) - 0.1m;
            candles.Add(new KlineCandle(TradingSymbol.BNBUSDT, start.AddMinutes(i), open, high, low, close, 10m + i % 5));
            price = close <= 0m ? 100m : close;
        }

        return candles;
    }
}
