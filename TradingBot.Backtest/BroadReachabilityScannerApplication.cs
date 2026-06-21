using System.Text.Json;
using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class BroadReachabilityScannerApplication(BacktestSettings settings)
{
    public async Task<BroadReachabilityScanRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var symbols = BroadReachabilitySymbolResolver.ResolveAvailableSymbols(settings);
        if (symbols.Count == 0)
            throw new InvalidOperationException($"No local candle data found under '{settings.DataDirectory}'.");

        var intervals = settings.Intervals;
        var dataLoader = new HistoricalKlineDataLoader(settings);
        var candlesBySymbol = new Dictionary<TradingSymbol, IReadOnlyList<KlineCandle>>();
        foreach (var symbol in symbols)
        {
            var validation = await dataLoader.LoadAndValidateAsync(symbol, cancellationToken);
            var candles = validation.Candles;
            if (settings.BootstrapDays.HasValue && candles.Count > 0)
            {
                var endUtc = candles[^1].OpenTimeUtc;
                var startUtc = endUtc.AddDays(-settings.BootstrapDays.Value);
                candles = CandleWindowSlicer.Slice(candles, startUtc, endUtc);
            }

            candlesBySymbol[symbol] = candles;
        }

        var allCandidates = new List<CandidateReachabilityRecord>();
        foreach (var interval in intervals)
        {
            foreach (var symbol in symbols)
            {
                if (!candlesBySymbol.TryGetValue(symbol, out var sourceCandles) || sourceCandles.Count == 0)
                    continue;

                StrategyStaticStateResetter.ResetMovingAverageTrendStrategyState();
                var profile = BacktestApplication.BuildBroadReachabilityScannerProfile(symbol, interval);
                var replayContext = BacktestApplication.BuildProfileReplayContext(settings, profile);
                var reachabilitySettings = CandidateReachabilitySettings.FromConfiguration(replayContext.Configuration);
                var collector = new CandidateReachabilityCollector(reachabilitySettings);
                var profileSymbols = symbol.ToString();
                var blockedEntries = new List<BlockedEntryRecord>();

                var aggregate = CandleAggregator.Aggregate(symbol, sourceCandles, "1m", interval);
                if (aggregate.Candles.Count == 0)
                    continue;

                var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                BacktestApplication.RunSymbolReplay(
                    interval,
                    profile.ProfileName,
                    profileSymbols,
                    replayContext.Strategy,
                    symbol,
                    aggregate.Candles,
                    quantity,
                    settings.ForceCloseAtEnd,
                    replayContext.Guard,
                    replayContext.PullbackV2Filter,
                    replayContext.Simulator,
                    replayContext.SignalStats,
                    blockedEntries,
                    cancellationToken,
                    replayContext.BnbPullbackGuard,
                    replayContext.RuntimeSnapshot.ProfitLockThresholdPercent,
                    replayContext.RuntimeSnapshot.EnablePullbackFollowThroughV2,
                    replayContext.RetestContinuationModel,
                    collector,
                    sourceCandles);

                allCandidates.AddRange(collector.Records);
            }
        }

        var rankings = BroadReachabilityRankingAggregator.BuildRankings(allCandidates);
        var discoveryAnswers = BroadReachabilityRankingAggregator.BuildDiscoveryAnswers(allCandidates, rankings);

        Directory.CreateDirectory(settings.OutputDirectory);
        var reportWriter = new BroadReachabilityReportWriter(settings.OutputDirectory);
        await reportWriter.WriteAsync(allCandidates, rankings, discoveryAnswers, cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "run-metadata.json"),
            JsonSerializer.Serialize(new
            {
                mode = "broad-reachability-scan",
                settings.DataDirectory,
                settings.OutputDirectory,
                settings.BootstrapDays,
                symbols = symbols.Select(s => s.ToString()).ToArray(),
                intervals,
                candidateCount = allCandidates.Count,
                rankingCount = rankings.Count
            }, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new BroadReachabilityScanRunResult(
            allCandidates,
            rankings,
            discoveryAnswers,
            symbols,
            intervals);
    }
}
