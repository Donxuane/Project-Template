using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV3Catalog
{
    public const string PrimaryVariantLabel = "Primary_NextClose_4h_cd3_1.50_1.00";
    public const string SmokeBestVariantLabel = "SmokeBest_NextClose_8h_cd6_1.75_1.00";
    public static readonly DirectionalRuleV2OverlapPolicy FocusedOverlapPolicy =
        DirectionalRuleV2OverlapPolicy.OneOpenTradePerRuleSymbol;
    public static readonly (decimal Target, decimal Stop)[] TargetStopMatrix =
    [
        (1.25m, 1.00m),
        (1.50m, 1.00m),
        (1.75m, 1.00m),
        (1.50m, 1.25m)
    ];
    public static readonly int[] HoldMinutesOptions = [120, 240, 480];
    public static readonly int[] CooldownCandleOptions = [1, 3, 6];
    public static readonly DirectionalRuleEntryMode[] EntryModes =
        [DirectionalRuleEntryMode.NextOpen, DirectionalRuleEntryMode.NextClose];
    public static readonly DirectionalRuleV2OverlapPolicy[] OverlapPolicies =
    [
        DirectionalRuleV2OverlapPolicy.OneOpenTradePerRuleSymbol,
        DirectionalRuleV2OverlapPolicy.OneOpenTradePerSymbol
    ];

    public static IReadOnlyList<int> DefaultRobustnessWindows() => [30, 60, 90, 120, 180];
    public static IReadOnlyList<int> FocusedRobustnessWindows() => [30, 60, 90];

    public static IReadOnlyList<DirectionalRuleV3SimulationProfile> BuildFocusedProfiles(
        IReadOnlyList<DirectionalRuleDefinition> rules)
    {
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase));
        if (rule is null)
            return [];

        var specs = new (string LabelPrefix, bool IsPrimary, bool IsSmokeBest, DirectionalRuleEntryMode Entry, int Hold, int Cd, decimal Target, decimal Stop)[]
        {
            ("Primary", true, false, DirectionalRuleEntryMode.NextClose, 240, 3, 1.50m, 1.00m),
            ("SmokeBest", false, true, DirectionalRuleEntryMode.NextClose, 480, 6, 1.75m, 1.00m),
            ("Primary_NextOpen", false, false, DirectionalRuleEntryMode.NextOpen, 240, 3, 1.50m, 1.00m),
            ("SmokeBest_NextOpen", false, false, DirectionalRuleEntryMode.NextOpen, 480, 6, 1.75m, 1.00m),
            ("HoldCompare_Primary8h", false, false, DirectionalRuleEntryMode.NextClose, 480, 3, 1.50m, 1.00m),
            ("HoldCompare_Smoke4h", false, false, DirectionalRuleEntryMode.NextClose, 240, 6, 1.75m, 1.00m),
            ("CooldownCompare_PrimaryCd6", false, false, DirectionalRuleEntryMode.NextClose, 240, 6, 1.50m, 1.00m),
            ("CooldownCompare_SmokeCd3", false, false, DirectionalRuleEntryMode.NextClose, 480, 3, 1.75m, 1.00m)
        };

        return specs.Select(spec =>
        {
            var variantLabel = spec.LabelPrefix == "Primary"
                ? PrimaryVariantLabel
                : spec.LabelPrefix == "SmokeBest"
                    ? SmokeBestVariantLabel
                    : BuildVariantLabel(spec.Entry, spec.Hold, spec.Cd, spec.Target, spec.Stop, FocusedOverlapPolicy);
            var profileKey = BuildProfileKey(spec.Entry, FocusedOverlapPolicy, spec.Cd, spec.Hold, spec.Target, spec.Stop);
            return new DirectionalRuleV3SimulationProfile(
                profileKey,
                variantLabel,
                spec.IsPrimary,
                spec.IsSmokeBest,
                rule,
                TradingSymbol.BNBUSDT,
                "5m",
                spec.Target,
                spec.Stop,
                spec.Hold,
                spec.Entry,
                FocusedOverlapPolicy,
                spec.Cd);
        }).ToArray();
    }

    public static IReadOnlyList<DirectionalRuleV3SimulationProfile> BuildProfiles(
        IReadOnlyList<DirectionalRuleDefinition> rules)
    {
        var rule = rules.FirstOrDefault(r => r.RuleName.StartsWith("Rule01", StringComparison.OrdinalIgnoreCase));
        if (rule is null)
            return [];

        var profiles = new List<DirectionalRuleV3SimulationProfile>();
        foreach (var entryMode in EntryModes)
        {
            foreach (var maxHold in HoldMinutesOptions)
            {
                foreach (var cooldown in CooldownCandleOptions)
                {
                    foreach (var (target, stop) in TargetStopMatrix)
                    {
                        foreach (var overlap in OverlapPolicies)
                        {
                            var variantLabel = BuildVariantLabel(entryMode, maxHold, cooldown, target, stop, overlap);
                            var isPrimary = variantLabel == PrimaryVariantLabel
                                            || IsPrimaryProfile(entryMode, maxHold, cooldown, target, stop, overlap);
                            var profileKey = BuildProfileKey(entryMode, overlap, cooldown, maxHold, target, stop);
                            profiles.Add(new DirectionalRuleV3SimulationProfile(
                                profileKey,
                                variantLabel,
                                isPrimary,
                                IsSmokeBestProfile(entryMode, maxHold, cooldown, target, stop, overlap),
                                rule,
                                TradingSymbol.BNBUSDT,
                                "5m",
                                target,
                                stop,
                                maxHold,
                                entryMode,
                                overlap,
                                cooldown));
                        }
                    }
                }
            }
        }

        return profiles;
    }

    public static bool IsPrimaryProfile(
        DirectionalRuleEntryMode entryMode,
        int maxHoldMinutes,
        int cooldown,
        decimal target,
        decimal stop,
        DirectionalRuleV2OverlapPolicy overlap)
        => entryMode == DirectionalRuleEntryMode.NextClose
           && maxHoldMinutes == 240
           && cooldown == 3
           && target == 1.50m
           && stop == 1.00m;

    public static bool IsSmokeBestProfile(
        DirectionalRuleEntryMode entryMode,
        int maxHoldMinutes,
        int cooldown,
        decimal target,
        decimal stop,
        DirectionalRuleV2OverlapPolicy overlap)
        => entryMode == DirectionalRuleEntryMode.NextClose
           && maxHoldMinutes == 480
           && cooldown == 6
           && target == 1.75m
           && stop == 1.00m;

    public static string BuildVariantLabel(
        DirectionalRuleEntryMode entryMode,
        int maxHoldMinutes,
        int cooldown,
        decimal target,
        decimal stop,
        DirectionalRuleV2OverlapPolicy overlap)
        => $"{entryMode}_{maxHoldMinutes / 60}h_cd{cooldown}_t{target:F2}_s{stop:F2}_{overlap}";

    public static string BuildProfileKey(
        DirectionalRuleEntryMode entryMode,
        DirectionalRuleV2OverlapPolicy overlap,
        int cooldown,
        int maxHoldMinutes,
        decimal target,
        decimal stop)
        => $"Rule01|BNBUSDT|5m|{entryMode}|{overlap}|cd{cooldown}|h{maxHoldMinutes}|t{target:F2}|s{stop:F2}";

    public static DirectionalRuleV2SimulationProfile ToV2Profile(DirectionalRuleV3SimulationProfile profile)
        => new(
            profile.ProfileKey,
            profile.Rule,
            profile.Symbol,
            profile.Interval,
            profile.TargetPercent,
            profile.StopPercent,
            profile.MaxHoldMinutes,
            profile.EntryMode,
            profile.OverlapPolicy,
            profile.CooldownCandlesAfterExit);
}
