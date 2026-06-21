using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV2Catalog
{
    public const decimal PrimaryTargetPercent = 1.50m;
    public const decimal PrimaryStopPercent = 1.00m;
    public static readonly int[] HoldMinutesOptions = [240, 480, 720];
    public static readonly DirectionalRuleEntryMode[] EntryModes =
        [DirectionalRuleEntryMode.NextOpen, DirectionalRuleEntryMode.NextClose];
    public static readonly DirectionalRuleV2OverlapPolicy[] OverlapPolicies =
    [
        DirectionalRuleV2OverlapPolicy.AllowOverlap,
        DirectionalRuleV2OverlapPolicy.OneOpenTradePerRuleSymbol,
        DirectionalRuleV2OverlapPolicy.OneOpenTradePerSymbol
    ];
    public static readonly int[] CooldownCandleOptions = [0, 1, 3, 6];

    public static IReadOnlyList<DirectionalRuleV2Candidate> BuildCandidates()
        =>
        [
            new("Rule01", TradingSymbol.ETHUSDT, "30m"),
            new("Rule01", TradingSymbol.BNBUSDT, "5m"),
            new("Rule05", TradingSymbol.ETHUSDT, "15m"),
            new("Rule05", TradingSymbol.BNBUSDT, "15m"),
            new("Rule05", TradingSymbol.BNBUSDT, "5m")
        ];

    public static DirectionalRuleDefinition? ResolveRule(
        IReadOnlyList<DirectionalRuleDefinition> rules,
        string ruleKey)
        => rules.FirstOrDefault(r => r.RuleName.StartsWith(ruleKey, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<DirectionalRuleV2SimulationProfile> BuildProfiles(
        IReadOnlyList<DirectionalRuleDefinition> rules)
    {
        var profiles = new List<DirectionalRuleV2SimulationProfile>();
        foreach (var candidate in BuildCandidates())
        {
            var rule = ResolveRule(rules, candidate.RuleKey);
            if (rule is null)
                continue;

            foreach (var entryMode in EntryModes)
            {
                foreach (var maxHold in HoldMinutesOptions)
                {
                    foreach (var overlap in OverlapPolicies)
                    {
                        foreach (var cooldown in CooldownCandleOptions)
                        {
                            var profileKey = BuildProfileKey(
                                candidate.RuleKey, candidate.Symbol, candidate.Interval,
                                entryMode, overlap, cooldown, maxHold);
                            profiles.Add(new DirectionalRuleV2SimulationProfile(
                                profileKey,
                                rule,
                                candidate.Symbol,
                                candidate.Interval,
                                PrimaryTargetPercent,
                                PrimaryStopPercent,
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

    public static IReadOnlyList<DirectionalRuleV2SimulationProfile> BuildPriorityProfiles(
        IReadOnlyList<DirectionalRuleDefinition> rules,
        TradingSymbol symbol)
    {
        var rule01 = ResolveRule(rules, "Rule01");
        var rule05 = ResolveRule(rules, "Rule05");
        if (rule01 is null || rule05 is null)
            return [];

        var candidates = BuildCandidates()
            .Where(c => c.Symbol == symbol)
            .ToArray();
        var rule01Candidate = candidates.FirstOrDefault(c => c.RuleKey == "Rule01");
        var rule05Candidate = candidates.FirstOrDefault(c => c.RuleKey == "Rule05");
        if (rule01Candidate is null || rule05Candidate is null)
            return [];

        var profiles = new List<DirectionalRuleV2SimulationProfile>();
        foreach (var priority in new[]
                 {
                     DirectionalRuleV2RulePriority.Rule01First,
                     DirectionalRuleV2RulePriority.Rule05First,
                     DirectionalRuleV2RulePriority.StrongerEdgeFirst
                 })
        {
            foreach (var entryMode in EntryModes)
            {
                foreach (var maxHold in HoldMinutesOptions)
                {
                    var profileKey = $"Priority_{priority}_{symbol}_{entryMode}_{maxHold}m";
                    profiles.Add(new DirectionalRuleV2SimulationProfile(
                        profileKey,
                        rule01,
                        symbol,
                        rule01Candidate.Interval,
                        PrimaryTargetPercent,
                        PrimaryStopPercent,
                        maxHold,
                        entryMode,
                        DirectionalRuleV2OverlapPolicy.OneOpenTradePerSymbol,
                        0,
                        priority));
                    _ = rule05;
                    _ = rule05Candidate;
                }
            }
        }

        return profiles;
    }

    public static string BuildProfileKey(
        string ruleKey,
        TradingSymbol symbol,
        string interval,
        DirectionalRuleEntryMode entryMode,
        DirectionalRuleV2OverlapPolicy overlap,
        int cooldown,
        int maxHoldMinutes)
        => $"{ruleKey}|{symbol}|{interval}|{entryMode}|{overlap}|cd{cooldown}|h{maxHoldMinutes}|t{PrimaryTargetPercent:F2}|s{PrimaryStopPercent:F2}";
}
