using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public static class BtcContextDataBootstrap
{
    public static async Task<BtcContextBootstrapResult> EnsureBtcUsdtDataAsync(
        BacktestSettings settings,
        int bootstrapDays,
        CancellationToken cancellationToken)
    {
        var dataDir = settings.DataDirectory;
        var btcPath = Path.Combine(dataDir, $"{TradingSymbol.BTCUSDT}-1m.json");
        var hadLocalData = File.Exists(btcPath);
        var downloader = new BinanceKlineBootstrapDownloader();
        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc.AddDays(-bootstrapDays);
        var merge = await downloader.DownloadAndMergeToJsonAsync(
            TradingSymbol.BTCUSDT.ToString(),
            btcPath,
            settings.BootstrapLimit,
            startUtc,
            endUtc,
            cancellationToken);

        var loader = new HistoricalKlineDataLoader(settings);
        var validation = await loader.LoadAndValidateAsync(TradingSymbol.BTCUSDT, cancellationToken);
        return new BtcContextBootstrapResult(
            btcPath,
            hadLocalData,
            merge,
            validation);
    }
}

public sealed record BtcContextBootstrapResult(
    string BtcDataPath,
    bool HadLocalDataBeforeBootstrap,
    BootstrapMergeResult MergeResult,
    SymbolValidationResult Validation);
