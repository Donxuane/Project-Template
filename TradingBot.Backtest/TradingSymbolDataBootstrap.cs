using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed record SymbolBootstrapOutcome(
    TradingSymbol Symbol,
    SymbolValidationResult Validation,
    bool DownloadAttempted,
    bool DownloadSucceeded,
    string? Note);

public sealed record SymbolBootstrapResult(
    IReadOnlyDictionary<TradingSymbol, SymbolValidationResult> Validated,
    IReadOnlyList<SymbolBootstrapOutcome> Outcomes);

public static class TradingSymbolDataBootstrap
{
    // Per-symbol wall-clock budget for the Binance download so a slow/rate-limited
    // symbol can never hang the whole validation run.
    private static readonly TimeSpan DefaultPerSymbolDownloadBudget = TimeSpan.FromMinutes(4);

    public static async Task<SymbolBootstrapResult> EnsureSymbolsDataAsync(
        BacktestSettings settings,
        IReadOnlyList<TradingSymbol> symbols,
        int bootstrapDays,
        bool attemptDownload,
        CancellationToken cancellationToken,
        TimeSpan? perSymbolDownloadBudget = null)
    {
        var loader = new HistoricalKlineDataLoader(settings);
        var downloader = new BinanceKlineBootstrapDownloader();
        var budget = perSymbolDownloadBudget ?? DefaultPerSymbolDownloadBudget;
        var endUtc = DateTime.UtcNow;
        var startUtc = endUtc.AddDays(-bootstrapDays);

        var validated = new Dictionary<TradingSymbol, SymbolValidationResult>();
        var outcomes = new List<SymbolBootstrapOutcome>();

        foreach (var symbol in symbols.Distinct())
        {
            // Always establish a local baseline first; this is fast and guarantees the
            // run can proceed even when the network download fails or times out.
            var local = await loader.LoadAndValidateAsync(symbol, cancellationToken);

            if (!attemptDownload)
            {
                validated[symbol] = local;
                outcomes.Add(new SymbolBootstrapOutcome(symbol, local, false, false,
                    local.Candles.Count == 0 ? "No local data and download disabled." : "Used local data."));
                continue;
            }

            var path = Path.Combine(settings.DataDirectory, $"{symbol}-1m.json");
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(budget);
                await downloader.DownloadAndMergeToJsonAsync(
                    symbol.ToString(),
                    path,
                    settings.BootstrapLimit,
                    startUtc,
                    endUtc,
                    timeout.Token);

                var refreshed = await loader.LoadAndValidateAsync(symbol, cancellationToken);
                // Keep whichever has more candles (download should only ever add history).
                var best = refreshed.Candles.Count >= local.Candles.Count ? refreshed : local;
                validated[symbol] = best;
                outcomes.Add(new SymbolBootstrapOutcome(symbol, best, true, true,
                    $"Downloaded; candles={best.Candles.Count}."));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                validated[symbol] = local;
                outcomes.Add(new SymbolBootstrapOutcome(symbol, local, true, false,
                    $"Download exceeded {budget.TotalMinutes:F0}m budget; fell back to local ({local.Candles.Count} candles)."));
            }
            catch (Exception ex)
            {
                validated[symbol] = local;
                outcomes.Add(new SymbolBootstrapOutcome(symbol, local, true, false,
                    $"Download failed ({ex.GetType().Name}: {ex.Message}); fell back to local ({local.Candles.Count} candles)."));
            }
        }

        return new SymbolBootstrapResult(validated, outcomes);
    }

    public static int ResolvePracticalBootstrapDays(
        IReadOnlyDictionary<TradingSymbol, SymbolValidationResult> validated,
        int preferredDays)
    {
        if (validated.Count == 0)
            return preferredDays;

        var maxSpan = validated.Values
            .Select(v => v.Candles.Count == 0
                ? 0
                : (int)Math.Max(1, (v.Candles[^1].OpenTimeUtc - v.Candles[0].OpenTimeUtc).TotalDays))
            .DefaultIfEmpty(0)
            .Max();

        if (maxSpan >= preferredDays)
            return preferredDays;
        if (maxSpan >= 270)
            return 270;
        if (maxSpan >= 180)
            return 180;
        return Math.Max(90, maxSpan);
    }
}
