using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class DirectionalRuleFuturesValidationV31Catalog
{
    public const string BestBnbVariantLabel = "BestBnb_NextClose_4h_cd6_1.75_1.00";
    public const int MinimumMeaningfulTrades = 50;

    public static readonly DirectionalRuleV2OverlapPolicy OverlapPolicy =
        DirectionalRuleV2OverlapPolicy.OneOpenTradePerRuleSymbol;

    public static readonly TradingSymbol[] DefaultCrossSymbols =
    [
        TradingSymbol.BNBUSDT,
        TradingSymbol.ETHUSDT,
        TradingSymbol.SOLUSDT,
        TradingSymbol.BTCUSDT
    ];

    public static readonly string[] CrossSymbolIntervals = ["5m", "15m", "30m"];
    public static readonly (decimal Target, decimal Stop)[] TargetStopPairs = [(1.50m, 1.00m), (1.75m, 1.00m)];
    public static readonly int[] HoldMinutesOptions = [240, 480];
    public static readonly int[] CooldownOptions = [3, 6];

    public static IReadOnlyList<int> LongHistoryWindows() => [30, 60, 90, 120, 180, 270, 365];
    public static IReadOnlyList<int> CrossSymbolWindows() => [30, 60, 90, 120, 180];

    public static IReadOnlyList<TradingSymbol> ResolveCrossSymbols(BacktestSettings settings)
    {
        var available = BroadReachabilitySymbolResolver.ResolveAvailableSymbols(settings);
        return DefaultCrossSymbols.Where(available.Contains).ToArray();
    }

    public static DirectionalRuleV31SimulationProfile BuildBestBnbLongHistoryProfile(DirectionalRuleDefinition rule)
        => new(
            BuildProfileKey(TradingSymbol.BNBUSDT, "5m", DirectionalRuleEntryMode.NextClose, OverlapPolicy, 6, 240, 1.75m, 1.00m),
            BestBnbVariantLabel,
            DirectionalRuleV31ValidationTrack.BestBnbLongHistory,
            true,
            rule,
            TradingSymbol.BNBUSDT,
            "5m",
            1.75m,
            1.00m,
            240,
            DirectionalRuleEntryMode.NextClose,
            OverlapPolicy,
            6);

    public static IReadOnlyList<DirectionalRuleV31SimulationProfile> BuildCrossSymbolProfiles(
        DirectionalRuleDefinition rule,
        IReadOnlyList<TradingSymbol> symbols)
    {
        var profiles = new List<DirectionalRuleV31SimulationProfile>();
        foreach (var symbol in symbols)
        {
            foreach (var interval in CrossSymbolIntervals)
            {
                foreach (var (target, stop) in TargetStopPairs)
                {
                    foreach (var hold in HoldMinutesOptions)
                    {
                        foreach (var cooldown in CooldownOptions)
                        {
                            var variantLabel = BuildVariantLabel(symbol, interval, hold, cooldown, target, stop);
                            profiles.Add(new DirectionalRuleV31SimulationProfile(
                                BuildProfileKey(symbol, interval, DirectionalRuleEntryMode.NextClose, OverlapPolicy, cooldown, hold, target, stop),
                                variantLabel,
                                DirectionalRuleV31ValidationTrack.CrossSymbol,
                                false,
                                rule,
                                symbol,
                                interval,
                                target,
                                stop,
                                hold,
                                DirectionalRuleEntryMode.NextClose,
                                OverlapPolicy,
                                cooldown));
                        }
                    }
                }
            }
        }

        return profiles;
    }

    public static IReadOnlyList<DirectionalRuleV31SimulationProfile> BuildAllProfiles(
        DirectionalRuleDefinition rule,
        IReadOnlyList<TradingSymbol> crossSymbols)
    {
        var profiles = new List<DirectionalRuleV31SimulationProfile> { BuildBestBnbLongHistoryProfile(rule) };
        profiles.AddRange(BuildCrossSymbolProfiles(rule, crossSymbols));
        return profiles;
    }

    public static string BuildVariantLabel(
        TradingSymbol symbol,
        string interval,
        int holdMinutes,
        int cooldown,
        decimal target,
        decimal stop)
        => $"{symbol}_{interval}_NextClose_{holdMinutes / 60}h_cd{cooldown}_t{target:F2}_s{stop:F2}";

    public static string BuildProfileKey(
        TradingSymbol symbol,
        string interval,
        DirectionalRuleEntryMode entryMode,
        DirectionalRuleV2OverlapPolicy overlap,
        int cooldown,
        int holdMinutes,
        decimal target,
        decimal stop)
        => $"Rule01|{symbol}|{interval}|{entryMode}|{overlap}|cd{cooldown}|h{holdMinutes}|t{target:F2}|s{stop:F2}";

    public static DirectionalRuleV2SimulationProfile ToV2Profile(DirectionalRuleV31SimulationProfile profile)
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
