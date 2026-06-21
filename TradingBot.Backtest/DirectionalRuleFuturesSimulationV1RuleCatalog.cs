using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum DirectionalRuleMatchKind
{
    QuantilePair,
    VolatilityRegimeOnly
}

public sealed record DirectionalRuleQuantileBound(string Label, decimal Min, decimal Max);

public sealed record DirectionalRuleDefinition
{
    public string RuleName { get; init; } = string.Empty;
    public LongShortDirection Direction { get; init; }
    public string RuleDescription { get; init; } = string.Empty;
    public DirectionalRuleMatchKind MatchKind { get; init; }
    public string? FeatureA { get; init; }
    public DirectionalRuleQuantileBound? FeatureABound { get; init; }
    public string? FeatureB { get; init; }
    public DirectionalRuleQuantileBound? FeatureBBound { get; init; }
    public string? RequiredVolatilityRegime { get; init; }
}

public static class DirectionalRuleFuturesSimulationV1RuleCatalog
{
    public static IReadOnlyList<DirectionalRuleDefinition> BuildDefaultHoldoutRules()
        =>
        [
            QuantileRule("Rule01_Short_DistHighQ1_AtrQ3", LongShortDirection.Short,
                "DistanceFromRecentHighPercent Q1[0.0000,0.2536] AND AtrPercent Q3[0.2694,2.2433]",
                "DistanceFromRecentHighPercent", 0.0000m, 0.2536m, "Q1",
                "AtrPercent", 0.2694m, 2.2433m, "Q3"),
            QuantileRule("Rule02_Long_RangeQ3_DistLowQ1", LongShortDirection.Long,
                "RangeWidthPercent Q3[1.3073,10.5309] AND DistanceFromRecentLowPercent Q1[0.0000,0.2851]",
                "RangeWidthPercent", 1.3073m, 10.5309m, "Q3",
                "DistanceFromRecentLowPercent", 0.0000m, 0.2851m, "Q1"),
            QuantileRule("Rule03_Short_RangeQ3_DistLowQ3", LongShortDirection.Short,
                "RangeWidthPercent Q3[1.3073,10.5309] AND DistanceFromRecentLowPercent Q3[0.6358,8.6512]",
                "RangeWidthPercent", 1.3073m, 10.5309m, "Q3",
                "DistanceFromRecentLowPercent", 0.6358m, 8.6512m, "Q3"),
            QuantileRule("Rule04_Long_DistHighQ3_AtrQ3", LongShortDirection.Long,
                "DistanceFromRecentHighPercent Q3[0.6303,9.1720] AND AtrPercent Q3[0.2694,2.2433]",
                "DistanceFromRecentHighPercent", 0.6303m, 9.1720m, "Q3",
                "AtrPercent", 0.2694m, 2.2433m, "Q3"),
            QuantileRule("Rule05_Short_TrendQ1_AtrQ3", LongShortDirection.Short,
                "TrendSlopePercent Q1[-1.9977,-0.0601] AND AtrPercent Q3[0.2694,2.2433]",
                "TrendSlopePercent", -1.9977m, -0.0601m, "Q1",
                "AtrPercent", 0.2694m, 2.2433m, "Q3"),
            VolatilityRule("Rule06_Long_BtcBuckets_ElevatedVol", LongShortDirection.Long,
                "BtcReturn30mPercent in train buckets AND VolatilityRegime=Elevated", "Elevated"),
            VolatilityRule("Rule07_Short_BtcBuckets_NormalVol", LongShortDirection.Short,
                "BtcReturn30mPercent in train buckets AND VolatilityRegime=Normal", "Normal")
        ];

    public static async Task<IReadOnlyList<DirectionalRuleDefinition>> LoadHoldoutRulesAsync(
        string discoveryJsonPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(discoveryJsonPath))
            return BuildDefaultHoldoutRules();

        await using var stream = File.OpenRead(discoveryJsonPath);
        var rows = await JsonSerializer.DeserializeAsync<LongShortEntryTimeRuleRow[]>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        if (rows is null || rows.Length == 0)
            return BuildDefaultHoldoutRules();

        var filtered = rows
            .Where(r => string.Equals(r.Verdict, "HoldoutNonNegative", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.CostScenarioLabel, "futures-moderate", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (filtered.Length == 0)
            return BuildDefaultHoldoutRules();

        var parsed = filtered
            .Select((row, index) => ParseFromDiscoveryRow(row, index + 1))
            .Where(r => r is not null)
            .Cast<DirectionalRuleDefinition>()
            .ToArray();

        return parsed.Length > 0 ? parsed : BuildDefaultHoldoutRules();
    }

    public static bool MatchesRule(
        MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features,
        DirectionalRuleDefinition rule)
        => rule.MatchKind switch
        {
            DirectionalRuleMatchKind.VolatilityRegimeOnly =>
                string.Equals(features.VolatilityRegime, rule.RequiredVolatilityRegime, StringComparison.OrdinalIgnoreCase),
            DirectionalRuleMatchKind.QuantilePair =>
                InBound(GetNumericFeature(features, rule.FeatureA!), rule.FeatureABound!)
                && InBound(GetNumericFeature(features, rule.FeatureB!), rule.FeatureBBound!),
            _ => false
        };

    private static DirectionalRuleDefinition? ParseFromDiscoveryRow(LongShortEntryTimeRuleRow row, int index)
    {
        if (row.RuleDescription.Contains("VolatilityRegime=", StringComparison.OrdinalIgnoreCase))
        {
            var regime = row.RuleDescription.Split("VolatilityRegime=", StringSplitOptions.TrimEntries)[^1];
            var prefix = row.Direction == LongShortDirection.Long ? "Long" : "Short";
            return new DirectionalRuleDefinition
            {
                RuleName = $"Rule{index:D2}_{prefix}_Vol_{regime}",
                Direction = row.Direction,
                RuleDescription = row.RuleDescription,
                MatchKind = DirectionalRuleMatchKind.VolatilityRegimeOnly,
                FeatureA = "BtcReturn30mPercent",
                FeatureB = "VolatilityRegime",
                RequiredVolatilityRegime = regime
            };
        }

        var parts = row.RuleDescription.Split(" AND ", StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return null;

        if (!TryParseQuantileClause(parts[0], out var featureA, out var boundA)
            || !TryParseQuantileClause(parts[1], out var featureB, out var boundB))
        {
            return null;
        }

        var dir = row.Direction == LongShortDirection.Long ? "Long" : "Short";
        return new DirectionalRuleDefinition
        {
            RuleName = $"Rule{index:D2}_{dir}_{featureA}_{boundA.Label}_{featureB}_{boundB.Label}",
            Direction = row.Direction,
            RuleDescription = row.RuleDescription,
            MatchKind = DirectionalRuleMatchKind.QuantilePair,
            FeatureA = featureA,
            FeatureABound = boundA,
            FeatureB = featureB,
            FeatureBBound = boundB
        };
    }

    private static bool TryParseQuantileClause(
        string clause,
        out string featureName,
        out DirectionalRuleQuantileBound bound)
    {
        featureName = string.Empty;
        bound = new DirectionalRuleQuantileBound(string.Empty, 0m, 0m);
        var openBracket = clause.IndexOf('[');
        var closeBracket = clause.IndexOf(']');
        if (openBracket <= 0 || closeBracket <= openBracket)
            return false;

        featureName = clause[..openBracket].Trim();
        var labelEnd = featureName.LastIndexOf(' ');
        var label = labelEnd > 0 ? featureName[(labelEnd + 1)..] : "Q1";
        if (labelEnd > 0)
            featureName = featureName[..labelEnd];

        var range = clause[(openBracket + 1)..closeBracket];
        var comma = range.IndexOf(',');
        if (comma <= 0)
            return false;

        if (!decimal.TryParse(range[..comma], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var min)
            || !decimal.TryParse(range[(comma + 1)..], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var max))
        {
            return false;
        }

        bound = new DirectionalRuleQuantileBound(label, min, max);
        return true;
    }

    private static DirectionalRuleDefinition QuantileRule(
        string ruleName,
        LongShortDirection direction,
        string description,
        string featureA,
        decimal minA,
        decimal maxA,
        string labelA,
        string featureB,
        decimal minB,
        decimal maxB,
        string labelB)
        => new()
        {
            RuleName = ruleName,
            Direction = direction,
            RuleDescription = description,
            MatchKind = DirectionalRuleMatchKind.QuantilePair,
            FeatureA = featureA,
            FeatureABound = new DirectionalRuleQuantileBound(labelA, minA, maxA),
            FeatureB = featureB,
            FeatureBBound = new DirectionalRuleQuantileBound(labelB, minB, maxB)
        };

    private static DirectionalRuleDefinition VolatilityRule(
        string ruleName,
        LongShortDirection direction,
        string description,
        string regime)
        => new()
        {
            RuleName = ruleName,
            Direction = direction,
            RuleDescription = description,
            MatchKind = DirectionalRuleMatchKind.VolatilityRegimeOnly,
            FeatureA = "BtcReturn30mPercent",
            FeatureB = "VolatilityRegime",
            RequiredVolatilityRegime = regime
        };

    private static decimal? GetNumericFeature(
        MarketRegimeForwardEdgeScanner.RegimeCandleFeatures features,
        string featureName)
        => featureName switch
        {
            "TrendSlopePercent" => features.TrendSlopePercent,
            "AtrPercent" => features.AtrPercent,
            "RangeWidthPercent" => features.RangeWidthPercent,
            "DistanceFromRecentHighPercent" => features.DistanceFromRecentHighPercent,
            "DistanceFromRecentLowPercent" => features.DistanceFromRecentLowPercent,
            "BtcReturn30mPercent" => features.BtcReturn30mPercent,
            _ => null
        };

    private static bool InBound(decimal? value, DirectionalRuleQuantileBound bound)
        => value.HasValue && value.Value >= bound.Min && value.Value <= bound.Max;
}
