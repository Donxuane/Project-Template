using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

/// <summary>
/// Frozen incubation profiles eligible for futures testnet shadow evaluation.
/// Catalog/threshold/strategy logic is referenced read-only; never modified here.
/// </summary>
public static class FuturesTestnetShadowCatalog
{
    public const string ModeName = "futures-testnet-shadow-runner";
    public const string DefaultOutputSubdir = "futures-testnet-shadow-run";
    public const string PrimaryCostScenario = "futures-moderate";

    public static readonly string[] FrozenProfileNames =
    [
        NoPaidDataShortWindowForwardIncubationV1Catalog.FrozenProfileName,
        NoPaidDataShortWindowSolForwardIncubationV1Catalog.FrozenProfileName,
        NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.FrozenProfileName,
        NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenProfileName
    ];

    public sealed record ProfileRef(
        string ProfileName,
        bool IsBnbRule01,
        TradingSymbol Symbol,
        string Interval,
        CrossSymbolComboKey? ComboKey,
        Func<string, string> FrozenStatePath,
        Func<string, string> ForwardHistoryPath,
        Func<string, string> ForwardIncubationSummaryPath,
        Func<CrossSymbolActivationConfig> BuildCrossSymbolActivationConfig,
        Func<ShortWindowActivationConfig> BuildShortWindowActivationConfig,
        string PrimaryCostScenario);

    public static IReadOnlyList<ProfileRef> Profiles { get; } =
    [
        new ProfileRef(
            NoPaidDataShortWindowForwardIncubationV1Catalog.FrozenProfileName,
            IsBnbRule01: true,
            TradingSymbol.BNBUSDT,
            "5m",
            ComboKey: null,
            NoPaidDataShortWindowForwardIncubationV1Catalog.FrozenStatePath,
            NoPaidDataShortWindowForwardIncubationV1Catalog.ForwardHistoryPath,
            outputRoot => Path.Combine(outputRoot, "no-paid-short-window-forward-incubation-v1-run", "frozen-candidate-summary.json"),
            () => throw new InvalidOperationException("BNB Rule01 uses short-window activation config."),
            NoPaidDataShortWindowForwardIncubationV1Catalog.BuildFrozenActivationConfig,
            NoPaidDataShortWindowForwardIncubationV1Catalog.PrimaryCostScenario),
        new ProfileRef(
            NoPaidDataShortWindowSolForwardIncubationV1Catalog.FrozenProfileName,
            IsBnbRule01: false,
            TradingSymbol.SOLUSDT,
            "5m",
            NoPaidDataShortWindowSolForwardIncubationV1Catalog.FrozenComboKey,
            NoPaidDataShortWindowSolForwardIncubationV1Catalog.FrozenStatePath,
            NoPaidDataShortWindowSolForwardIncubationV1Catalog.ForwardHistoryPath,
            outputRoot => Path.Combine(outputRoot, "no-paid-short-window-sol-forward-incubation-v1", "frozen-sol-candidate-summary.json"),
            NoPaidDataShortWindowSolForwardIncubationV1Catalog.BuildFrozenActivationConfig,
            () => throw new InvalidOperationException("Cross-symbol profile uses cross-symbol activation config."),
            NoPaidDataShortWindowSolForwardIncubationV1Catalog.PrimaryCostScenario),
        new ProfileRef(
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.FrozenProfileName,
            IsBnbRule01: false,
            TradingSymbol.BNBUSDT,
            "15m",
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.FrozenComboKey,
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.FrozenStatePath,
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.ForwardHistoryPath,
            outputRoot => Path.Combine(outputRoot, "no-paid-short-window-bnb-15m-forward-incubation-v1", "frozen-bnb-15m-candidate-summary.json"),
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.BuildFrozenActivationConfig,
            () => throw new InvalidOperationException("Cross-symbol profile uses cross-symbol activation config."),
            NoPaidDataShortWindowBnb15mForwardIncubationV1Catalog.PrimaryCostScenario),
        new ProfileRef(
            NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenProfileName,
            IsBnbRule01: false,
            TradingSymbol.SOLUSDT,
            "15m",
            NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenComboKey,
            NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.FrozenStatePath,
            NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.ForwardHistoryPath,
            outputRoot => Path.Combine(outputRoot, "no-paid-short-window-sol-15m-forward-incubation-v1", "frozen-sol-15m-candidate-summary.json"),
            NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.BuildFrozenActivationConfig,
            () => throw new InvalidOperationException("Cross-symbol profile uses cross-symbol activation config."),
            NoPaidDataShortWindowSol15mForwardIncubationV1Catalog.PrimaryCostScenario)
    ];
}
