using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public enum FuturesDataAvailabilityClass
{
    FullHistory,
    Limited30d,
    NotPublicFree
}

public sealed record FuturesDataSourceDescriptor(
    string SourceKey,
    string DisplayName,
    string Endpoint,
    string Granularity,
    FuturesDataAvailabilityClass Availability,
    bool BootstrapSupported,
    string Notes);

public static class FuturesMarketDataCatalog
{
    public const string FuturesBaseUrl = "https://fapi.binance.com";
    public const string FuturesDataSubdir = "futures";

    // The futures/data/* endpoints only expose ~30 days of history on the free API.
    public const string LimitedPeriod = "30m";

    public static readonly TradingSymbol[] Symbols =
    [
        TradingSymbol.BTCUSDT,
        TradingSymbol.ETHUSDT,
        TradingSymbol.BNBUSDT,
        TradingSymbol.SOLUSDT
    ];

    public static IReadOnlyList<FuturesDataSourceDescriptor> Sources()
        =>
        [
            new("funding", "Funding Rate", "/fapi/v1/fundingRate", "8h",
                FuturesDataAvailabilityClass.FullHistory, true,
                "Full history via fundingRate (8h cadence). 365d available."),
            new("markPriceKlines", "Mark Price Klines", "/fapi/v1/markPriceKlines", LimitedPeriod,
                FuturesDataAvailabilityClass.FullHistory, true,
                "Full history. Used for mark/index divergence."),
            new("indexPriceKlines", "Index Price Klines", "/fapi/v1/indexPriceKlines", LimitedPeriod,
                FuturesDataAvailabilityClass.FullHistory, true,
                "Full history (by pair). Used for mark/index divergence."),
            new("openInterestHist", "Open Interest History", "/futures/data/openInterestHist", LimitedPeriod,
                FuturesDataAvailabilityClass.Limited30d, true,
                "Free API exposes only ~30d. Insufficient for 365d train/validation/holdout."),
            new("takerLongShortRatio", "Taker Buy/Sell Volume", "/futures/data/takerlongshortRatio", LimitedPeriod,
                FuturesDataAvailabilityClass.Limited30d, true,
                "Free API exposes only ~30d. Recent-window diagnostic only."),
            new("globalLongShortAccountRatio", "Global Long/Short Account Ratio", "/futures/data/globalLongShortAccountRatio", LimitedPeriod,
                FuturesDataAvailabilityClass.Limited30d, true,
                "Free API exposes only ~30d. Recent-window diagnostic only."),
            new("topLongShortPositionRatio", "Top Trader Long/Short Position Ratio", "/futures/data/topLongShortPositionRatio", LimitedPeriod,
                FuturesDataAvailabilityClass.Limited30d, true,
                "Free API exposes only ~30d. Recent-window diagnostic only."),
            new("liquidations", "Liquidations", "(websocket forceOrder only)", "n/a",
                FuturesDataAvailabilityClass.NotPublicFree, false,
                "No free historical REST. Requires paid dataset (Coinalyze/Coinglass/Tardis) or live capture."),
            new("depthSnapshots", "Order Book Depth Snapshots", "(no historical REST)", "n/a",
                FuturesDataAvailabilityClass.NotPublicFree, false,
                "No free historical depth. Requires data.binance.vision dumps or paid feed (Tardis/Kaiko).")
        ];

    public static string FuturesDataDirectory(string dataDirectory)
        => Path.Combine(dataDirectory, FuturesDataSubdir);

    public static string FilePath(string dataDirectory, TradingSymbol symbol, string sourceKey)
        => Path.Combine(FuturesDataDirectory(dataDirectory), $"{symbol}-{sourceKey}.json");

    public static bool Is365dCapable(string sourceKey)
        => Sources().Any(s => s.SourceKey == sourceKey && s.Availability == FuturesDataAvailabilityClass.FullHistory);
}
