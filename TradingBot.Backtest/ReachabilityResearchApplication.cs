using TradingBot.Domain.Enums;

namespace TradingBot.Backtest;

public sealed class ReachabilityResearchApplication(BacktestSettings settings)
{
    public async Task<ReachabilityResearchRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var profiles = BacktestApplication.BuildReachabilityResearchProfiles(includeExperimental: true);
        var dataLoader = new HistoricalKlineDataLoader(settings);
        var validation = await dataLoader.LoadAndValidateAsync(TradingSymbol.BNBUSDT, cancellationToken);
        var candles = validation.Candles;
        if (settings.BootstrapDays.HasValue && candles.Count > 0)
        {
            var endUtc = candles[^1].OpenTimeUtc;
            var startUtc = endUtc.AddDays(-settings.BootstrapDays.Value);
            candles = CandleWindowSlicer.Slice(candles, startUtc, endUtc);
        }

        var allCandidates = new List<CandidateReachabilityRecord>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();

        foreach (var profile in profiles)
        {
            StrategyStaticStateResetter.ResetMovingAverageTrendStrategyState();
            var interval = BacktestApplication.ResolveReachabilityProfileInterval(profile);
            var replayContext = BacktestApplication.BuildProfileReplayContext(settings, profile);
            var reachabilitySettings = CandidateReachabilitySettings.FromConfiguration(replayContext.Configuration);
            var collector = new CandidateReachabilityCollector(reachabilitySettings);
            var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
            var profileTrades = new List<SimulatedTrade>();
            var profileBlockedEntries = new List<BlockedEntryRecord>();

            foreach (var symbol in profile.Symbols)
            {
                if (candles.Count == 0)
                    continue;

                var aggregate = CandleAggregator.Aggregate(symbol, candles, "1m", interval);
                if (aggregate.Candles.Count == 0)
                    continue;

                var quantity = BacktestApplication.ResolveQuantity(replayContext.Configuration, symbol);
                var replayTrades = BacktestApplication.RunSymbolReplay(
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
                    profileBlockedEntries,
                    cancellationToken,
                    replayContext.BnbPullbackGuard,
                    replayContext.RuntimeSnapshot.ProfitLockThresholdPercent,
                    replayContext.RuntimeSnapshot.EnablePullbackFollowThroughV2,
                    replayContext.RetestContinuationModel,
                    collector,
                    candles);
                profileTrades.AddRange(replayTrades);
            }

            allCandidates.AddRange(collector.Records);
            allTrades.AddRange(profileTrades);
            allBlockedEntries.AddRange(profileBlockedEntries);

            var profileOutput = Path.Combine(settings.OutputDirectory, profile.ProfileName);
            Directory.CreateDirectory(profileOutput);
            var reportWriter = new CandidateReachabilityReportWriter(profileOutput);
            await reportWriter.WriteAsync(collector.Records, profileTrades, cancellationToken);

            var standardWriter = new ReplayReportWriter(profileOutput);
            var summary = ReplaySummaryAggregator.BuildSummary(
                interval,
                profile.ProfileName,
                profileSymbols,
                profileTrades,
                replayContext.SignalStats,
                replayContext.RuntimeSnapshot);
            await standardWriter.WriteAsync(
                [summary],
                profileTrades,
                profileBlockedEntries,
                validation.Issues,
                cancellationToken);
        }

        var summaries = CandidateReachabilityAggregator.BuildSummaries(allCandidates, allTrades);
        var answers = CandidateReachabilityAggregator.BuildResearchAnswers(allCandidates, allTrades);
        Directory.CreateDirectory(settings.OutputDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "candidate-reachability-summary.json"),
            System.Text.Json.JsonSerializer.Serialize(summaries, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(settings.OutputDirectory, "reachability-research-answers.json"),
            System.Text.Json.JsonSerializer.Serialize(answers, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        return new ReachabilityResearchRunResult(
            allCandidates,
            allTrades,
            allBlockedEntries,
            summaries,
            answers,
            profiles.Count);
    }
}
