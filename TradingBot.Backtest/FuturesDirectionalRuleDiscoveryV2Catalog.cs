using TradingBot.Domain.Enums;
using Features = TradingBot.Backtest.MarketRegimeForwardEdgeScanner.RegimeCandleFeatures;

namespace TradingBot.Backtest;

public sealed class DiscoveryBasePoint
{
    public int SignalIndex { get; init; }
    public DateTime SignalTimeUtc { get; init; }
    public DateTime EntryTimeUtc { get; init; }
    public decimal EntryPriceNextOpen { get; init; }
    public decimal EntryPriceNextClose { get; init; }
    public Features Features { get; init; } = new();
    public int HourOfDayUtc { get; init; }
    public string DayOfWeek { get; init; } = string.Empty;
    public string SessionBucket { get; init; } = string.Empty;
}

public sealed record DiscoveryGeneratedRule(
    string Description,
    IReadOnlyList<string> FeaturesUsed,
    Func<DiscoveryBasePoint, bool> Predicate);

public static class FuturesDirectionalRuleDiscoveryV2Catalog
{
    public const int MinimumTotalTrades = 50;
    public const int MinimumTrainTrades = 20;
    public const int MinimumValidationTrades = 15;
    public const int MinimumHoldoutTrades = 15;
    public const int MinimumActiveMonths = 6;

    public const string PrimaryCostScenario = "futures-moderate";

    // Bounds on the controlled search to avoid combinatorial explosion.
    public const int TopSinglesForPairs = 8;
    public const int TopPairsForTriples = 4;
    public const int MaxTripleRules = 8;
    public const int MaxReportedCandidatesPerCombo = 30;

    public static readonly TradingSymbol[] Symbols =
    [
        TradingSymbol.BTCUSDT,
        TradingSymbol.ETHUSDT,
        TradingSymbol.BNBUSDT,
        TradingSymbol.SOLUSDT
    ];

    public static readonly string[] Intervals = ["5m", "15m", "30m"];

    public static readonly DiscoveryRuleConfig PrimaryConfig =
        new(DirectionalRuleEntryMode.NextClose, 1.00m, 0.75m, 480, 6);

    public static IReadOnlyList<DiscoveryRuleConfig> BuildConfigMatrix()
    {
        var entryModes = new[] { DirectionalRuleEntryMode.NextOpen, DirectionalRuleEntryMode.NextClose };
        var targetStops = new[] { (1.00m, 0.75m), (0.75m, 0.50m), (1.50m, 1.00m), (2.00m, 1.25m) };
        var holds = new[] { 240, 480, 720 };
        var cooldowns = new[] { 3, 6 };
        var configs = new List<DiscoveryRuleConfig>();
        foreach (var entryMode in entryModes)
            foreach (var (target, stop) in targetStops)
                foreach (var hold in holds)
                    foreach (var cooldown in cooldowns)
                        configs.Add(new DiscoveryRuleConfig(entryMode, target, stop, hold, cooldown));
        return configs;
    }

    public static readonly string[] NumericRuleFeatures =
    [
        nameof(Features.DistanceFromRecentHighPercent),
        nameof(Features.DistanceFromRecentLowPercent),
        nameof(Features.AtrPercent),
        nameof(Features.RangeWidthPercent),
        nameof(Features.TrendSlopePercent),
        nameof(Features.TrendStrengthPercent),
        nameof(Features.RecentReturn15),
        nameof(Features.RecentReturn30),
        nameof(Features.RecentReturn60),
        nameof(Features.BtcReturn15mPercent),
        nameof(Features.BtcReturn30mPercent),
        nameof(Features.BtcReturn60mPercent),
        nameof(Features.BtcTrendSlopePercent),
        nameof(Features.VolumeExpansionRatio),
        nameof(Features.CandleBodyStrengthPercent),
        nameof(Features.ClosePositionInRange),
        nameof(Features.SymbolReturnRelativeToBtc60mPercent),
        nameof(Features.MarketWideReturnProxyPercent)
    ];

    public static readonly string[] CategoricalRuleFeatures =
    [
        nameof(Features.VolatilityRegime),
        nameof(Features.BtcTrendRegime),
        "SessionBucket"
    ];

    public static decimal? GetNumeric(DiscoveryBasePoint p, string feature)
        => feature switch
        {
            nameof(Features.DistanceFromRecentHighPercent) => p.Features.DistanceFromRecentHighPercent,
            nameof(Features.DistanceFromRecentLowPercent) => p.Features.DistanceFromRecentLowPercent,
            nameof(Features.AtrPercent) => p.Features.AtrPercent,
            nameof(Features.RangeWidthPercent) => p.Features.RangeWidthPercent,
            nameof(Features.TrendSlopePercent) => p.Features.TrendSlopePercent,
            nameof(Features.TrendStrengthPercent) => p.Features.TrendStrengthPercent,
            nameof(Features.RecentReturn15) => p.Features.RecentReturn15,
            nameof(Features.RecentReturn30) => p.Features.RecentReturn30,
            nameof(Features.RecentReturn60) => p.Features.RecentReturn60,
            nameof(Features.BtcReturn15mPercent) => p.Features.BtcReturn15mPercent,
            nameof(Features.BtcReturn30mPercent) => p.Features.BtcReturn30mPercent,
            nameof(Features.BtcReturn60mPercent) => p.Features.BtcReturn60mPercent,
            nameof(Features.BtcTrendSlopePercent) => p.Features.BtcTrendSlopePercent,
            nameof(Features.VolumeExpansionRatio) => p.Features.VolumeExpansionRatio,
            nameof(Features.CandleBodyStrengthPercent) => p.Features.CandleBodyStrengthPercent,
            nameof(Features.ClosePositionInRange) => p.Features.ClosePositionInRange,
            nameof(Features.SymbolReturnRelativeToBtc60mPercent) => p.Features.SymbolReturnRelativeToBtc60mPercent,
            nameof(Features.MarketWideReturnProxyPercent) => p.Features.MarketWideReturnProxyPercent,
            _ => null
        };

    public static string GetCategorical(DiscoveryBasePoint p, string feature)
        => feature switch
        {
            nameof(Features.VolatilityRegime) => p.Features.VolatilityRegime,
            nameof(Features.BtcTrendRegime) => p.Features.BtcTrendRegime ?? "Unknown",
            nameof(Features.BtcVolatilityRegime) => p.Features.BtcVolatilityRegime ?? "Unknown",
            "SessionBucket" => p.SessionBucket,
            "DayOfWeek" => p.DayOfWeek,
            _ => "Unknown"
        };

    public readonly record struct TertileBounds(decimal B33, decimal B66, bool Valid);

    public static TertileBounds ComputeTertiles(IReadOnlyList<DiscoveryBasePoint> trainPoints, string feature)
    {
        var values = new List<decimal>(trainPoints.Count);
        foreach (var p in trainPoints)
        {
            var v = GetNumeric(p, feature);
            if (v.HasValue)
                values.Add(v.Value);
        }

        if (values.Count < 30)
            return new TertileBounds(0m, 0m, false);
        values.Sort();
        var b33 = values[(int)(values.Count / 3.0)];
        var b66 = values[(int)(values.Count * 2.0 / 3.0)];
        return new TertileBounds(b33, b66, b66 > b33);
    }

    public static DiscoveryGeneratedRule? BuildNumericBucketRule(string feature, string bucket, TertileBounds bounds)
    {
        if (!bounds.Valid)
            return null;

        Func<DiscoveryBasePoint, bool> predicate = bucket switch
        {
            "Q1" => p => GetNumeric(p, feature) is { } v && v <= bounds.B33,
            "Q2" => p => GetNumeric(p, feature) is { } v && v > bounds.B33 && v <= bounds.B66,
            "Q3" => p => GetNumeric(p, feature) is { } v && v > bounds.B66,
            _ => _ => false
        };
        var descr = bucket switch
        {
            "Q1" => $"{feature} Q1(<={bounds.B33:0.0000})",
            "Q2" => $"{feature} Q2({bounds.B33:0.0000}..{bounds.B66:0.0000}]",
            "Q3" => $"{feature} Q3(>{bounds.B66:0.0000})",
            _ => feature
        };
        return new DiscoveryGeneratedRule(descr, [feature], predicate);
    }

    public static DiscoveryGeneratedRule BuildCategoricalRule(string feature, string category)
        => new(
            $"{feature}={category}",
            [feature],
            p => string.Equals(GetCategorical(p, feature), category, StringComparison.OrdinalIgnoreCase));

    public static DiscoveryGeneratedRule Combine(DiscoveryGeneratedRule a, DiscoveryGeneratedRule b)
    {
        var features = a.FeaturesUsed.Concat(b.FeaturesUsed).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new DiscoveryGeneratedRule(
            $"{a.Description} AND {b.Description}",
            features,
            p => a.Predicate(p) && b.Predicate(p));
    }

    public static IReadOnlyList<DiscoveryGeneratedRule> BuildSingleFeatureRules(
        IReadOnlyList<DiscoveryBasePoint> trainPoints,
        IReadOnlyDictionary<string, TertileBounds> tertiles)
    {
        var rules = new List<DiscoveryGeneratedRule>();
        foreach (var feature in NumericRuleFeatures)
        {
            if (!tertiles.TryGetValue(feature, out var bounds) || !bounds.Valid)
                continue;
            foreach (var bucket in new[] { "Q1", "Q2", "Q3" })
            {
                var rule = BuildNumericBucketRule(feature, bucket, bounds);
                if (rule is not null)
                    rules.Add(rule);
            }
        }

        foreach (var feature in CategoricalRuleFeatures)
        {
            var categories = trainPoints
                .Select(p => GetCategorical(p, feature))
                .Where(c => !string.IsNullOrWhiteSpace(c) && !string.Equals(c, "Unknown", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var category in categories)
                rules.Add(BuildCategoricalRule(feature, category));
        }

        return rules;
    }

    public static IReadOnlyList<DiscoveryGeneratedRule> BuildCuratedPairRules(
        LongShortDirection direction,
        IReadOnlyDictionary<string, TertileBounds> tertiles)
    {
        DiscoveryGeneratedRule? Bucket(string feature, string bucket)
            => tertiles.TryGetValue(feature, out var b) ? BuildNumericBucketRule(feature, bucket, b) : null;

        var thesis = direction == LongShortDirection.Short
            ? new[]
            {
                (Bucket(nameof(Features.DistanceFromRecentHighPercent), "Q1"), Bucket(nameof(Features.AtrPercent), "Q3")),
                (Bucket(nameof(Features.TrendSlopePercent), "Q1"), Bucket(nameof(Features.BtcReturn30mPercent), "Q1")),
                (Bucket(nameof(Features.TrendSlopePercent), "Q1"), Bucket(nameof(Features.DistanceFromRecentLowPercent), "Q3")),
                (Bucket(nameof(Features.DistanceFromRecentHighPercent), "Q1"), Bucket(nameof(Features.BtcReturn30mPercent), "Q1"))
            }
            : new[]
            {
                (Bucket(nameof(Features.DistanceFromRecentLowPercent), "Q1"), Bucket(nameof(Features.BtcReturn30mPercent), "Q3")),
                (Bucket(nameof(Features.DistanceFromRecentLowPercent), "Q1"), Bucket(nameof(Features.TrendSlopePercent), "Q3")),
                (Bucket(nameof(Features.DistanceFromRecentHighPercent), "Q3"), Bucket(nameof(Features.AtrPercent), "Q3")),
                (Bucket(nameof(Features.DistanceFromRecentLowPercent), "Q1"), Bucket(nameof(Features.SymbolReturnRelativeToBtc60mPercent), "Q3"))
            };

        var rules = new List<DiscoveryGeneratedRule>();
        foreach (var (a, b) in thesis)
        {
            if (a is not null && b is not null)
                rules.Add(Combine(a, b));
        }

        return rules;
    }
}
