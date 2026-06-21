using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.DecisionEngine;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Interfaces.Services.Decision;

namespace TradingBot.Backtest;

public sealed class BacktestApplication(BacktestSettings settings)
{
    public async Task<BacktestRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var allSummaries = new List<ReplaySummaryRow>();
        var allTrades = new List<SimulatedTrade>();
        var allBlockedEntries = new List<BlockedEntryRecord>();
        var aggregationDiagnostics = new List<AggregationDiagnosticsRecord>();
        var allIssues = new List<DataQualityIssue>();

        var dataLoader = new HistoricalKlineDataLoader(settings);
        var isMultiIntervalRun = settings.Intervals.Count > 1;

        var profiles = BuildDefaultProfiles();
        var allSymbols = profiles.SelectMany(p => p.Symbols).Distinct().ToArray();
        var validatedDataBySymbol = new Dictionary<TradingSymbol, SymbolValidationResult>();
        foreach (var symbol in allSymbols)
        {
            var validation = await dataLoader.LoadAndValidateAsync(symbol, cancellationToken);
            validatedDataBySymbol[symbol] = validation;
            allIssues.AddRange(validation.Issues);
        }

        foreach (var interval in settings.Intervals)
        {
            var intervalSummaries = new List<ReplaySummaryRow>();
            var intervalTrades = new List<SimulatedTrade>();
            var intervalBlockedEntries = new List<BlockedEntryRecord>();
            foreach (var profile in profiles)
            {
                StrategyStaticStateResetter.ResetMovingAverageTrendStrategyState();

                var configuration = BuildConfiguration(profile.ConfigOverrides);
                var trendSettings = new TrendStateSettings
                {
                    MinTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinTrendStrengthPercent") ?? 0.001m),
                    StrongTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:StrongTrendStrengthPercent") ?? 0.003m),
                    MinSlopePercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinSlopePercent") ?? 0.0005m),
                    LowVolatilityRangePercentThreshold = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:LowVolatilityRangePercentThreshold") ?? 0.0008m),
                    MinRangeCandlesForVolatilityCheck = Math.Max(2, configuration.GetValue<int?>("DecisionEngine:TrendState:MinRangeCandlesForVolatilityCheck") ?? 10)
                };

                ITrendStateService trendService = new TrendStateService(
                    NullLogger<TrendStateService>.Instance,
                    Options.Create(trendSettings));
                IVolatilityService volatilityService = new VolatilityService(configuration);
                IAtrService atrService = new AtrService(configuration);
                IMarketConditionService marketConditionService = new MarketConditionService(configuration, volatilityService, atrService);
                var positionManager = new PositionManager();
                var marketStateTracker = new MarketStateTracker();

                var strategy = new MovingAverageTrendStrategy(
                    NullLogger<MovingAverageTrendStrategy>.Instance,
                    trendService,
                    positionManager,
                    marketStateTracker,
                    marketConditionService,
                    configuration);

                var profileSymbols = string.Join("+", profile.Symbols.Select(s => s.ToString()));
                var executionCostSettings = new ExecutionCostSettings(
                    settings.FeeRatePercent,
                    settings.EstimatedSpreadPercent,
                    settings.SlippagePercent);
                var simulator = new ExecutionSimulator(executionCostSettings, ReadExitPolicySettings(configuration));
                var guard = new BacktestEntryGuard(configuration, executionCostSettings);
                var bnbPullbackGuard = CreateBnbPullbackGuard(configuration);
                var retestContinuationModel = CreateRetestContinuationModel(configuration);
                var pullbackV2Filter = new PullbackFollowThroughV2Filter(configuration, executionCostSettings);
                var signalStats = new ProfileSignalStats();
                var runtimeSnapshot = ReadProfileRuntimeSnapshot(configuration);

                var profileTrades = new List<SimulatedTrade>();
                foreach (var symbol in profile.Symbols)
                {
                    var validation = validatedDataBySymbol[symbol];
                    if (validation.Candles.Count == 0)
                        continue;

                    var aggregate = CandleAggregator.Aggregate(symbol, validation.Candles, "1m", interval);
                    var inheritedGapCount = CountGapWarnings(validation.Issues);
                    aggregationDiagnostics.Add(new AggregationDiagnosticsRecord
                    {
                        Interval = interval,
                        SourceInterval = "1m",
                        TargetInterval = interval,
                        Symbol = symbol,
                        InputCandleCount = aggregate.InputCandleCount,
                        OutputCandleCount = aggregate.OutputCandleCount,
                        DroppedIncompleteFinalBucketCount = aggregate.DroppedIncompleteFinalBucketCount,
                        InheritedGapCount = inheritedGapCount
                    });
                    allIssues.Add(new DataQualityIssue(
                        interval,
                        symbol,
                        "info",
                        $"Aggregation diagnostics: source=1m,target={interval},input={aggregate.InputCandleCount},output={aggregate.OutputCandleCount},droppedIncompleteFinalBucketCount={aggregate.DroppedIncompleteFinalBucketCount},inheritedGapCount={inheritedGapCount}."));

                    if (aggregate.Candles.Count == 0)
                        continue;

                    var quantity = ResolveQuantity(configuration, symbol);
                    var replayTrades = RunSymbolReplay(
                        interval,
                        profile.ProfileName,
                        profileSymbols,
                        strategy,
                        symbol,
                        aggregate.Candles,
                        quantity,
                        settings.ForceCloseAtEnd,
                        guard,
                        pullbackV2Filter,
                        simulator,
                        signalStats,
                        intervalBlockedEntries,
                        cancellationToken,
                        bnbPullbackGuard,
                        runtimeSnapshot.ProfitLockThresholdPercent,
                        runtimeSnapshot.EnablePullbackFollowThroughV2,
                        retestContinuationModel);
                    profileTrades.AddRange(replayTrades);
                }

                intervalTrades.AddRange(profileTrades);
                intervalSummaries.Add(ReplaySummaryAggregator.BuildSummary(
                    interval,
                    profile.ProfileName,
                    profileSymbols,
                    profileTrades,
                    signalStats,
                    runtimeSnapshot));
            }

            var intervalOutputDirectory = ResolveIntervalOutputDirectory(settings.OutputDirectory, interval, isMultiIntervalRun);
            var reportWriter = new ReplayReportWriter(intervalOutputDirectory);
            var intervalIssues = allIssues.Where(x => x.Interval == "1m" || x.Interval == interval).ToArray();
            await reportWriter.WriteAsync(intervalSummaries, intervalTrades, intervalBlockedEntries, intervalIssues, cancellationToken);

            allSummaries.AddRange(intervalSummaries);
            allTrades.AddRange(intervalTrades);
            allBlockedEntries.AddRange(intervalBlockedEntries);
        }

        if (isMultiIntervalRun)
        {
            var crossIntervalReportWriter = new ReplayReportWriter(settings.OutputDirectory);
            await crossIntervalReportWriter.WriteCrossIntervalSummaryAsync(allSummaries, cancellationToken);
            await crossIntervalReportWriter.WriteAggregationDiagnosticsAsync(aggregationDiagnostics, cancellationToken);
        }

        return new BacktestRunResult(allSummaries, allTrades, allBlockedEntries, aggregationDiagnostics, allIssues);
    }

    private IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string> overrides)
    {
        var builder = new ConfigurationBuilder()
            .AddJsonFile(settings.AppSettingsPath, optional: false);

        if (overrides.Count > 0)
            builder.AddInMemoryCollection(overrides.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value)));

        return builder.Build();
    }

    public static decimal ResolveQuantity(IConfiguration configuration, TradingSymbol symbol)
    {
        var symbolQty = configuration.GetValue<decimal?>($"DecisionEngine:SymbolQuantities:{symbol}");
        if (symbolQty.HasValue && symbolQty.Value > 0m)
            return symbolQty.Value;

        var fallback = configuration.GetValue<decimal?>("DecisionEngine:Quantity") ?? 0.001m;
        return Math.Max(0.00000001m, fallback);
    }

    public static BacktestExitPolicySettings ReadExitPolicySettings(IConfiguration configuration)
    {
        return new BacktestExitPolicySettings(
            ExitPolicyName: configuration.GetValue<string?>("Backtest:ExitPolicy:Name") ?? "OppositeSignalOnly",
            ProfitLockThresholdPercent: configuration.GetValue<decimal?>("Backtest:ExitPolicy:ProfitLockThresholdPercent"),
            EnableBreakevenAfterNetProfit: configuration.GetValue<bool?>("Backtest:ExitPolicy:EnableBreakevenAfterNetProfit") ?? false,
            BreakevenActivationNetProfitPercent: Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ExitPolicy:BreakevenActivationNetProfitPercent") ?? 0.12m),
            EnableTrailingAfterNetProfit: configuration.GetValue<bool?>("Backtest:ExitPolicy:EnableTrailingAfterNetProfit") ?? false,
            TrailingActivationNetProfitPercent: Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ExitPolicy:TrailingActivationNetProfitPercent") ?? 0.20m),
            TrailingStopPercent: Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ExitPolicy:TrailingStopPercent") ?? 0.10m),
            EnableStructuralStop: configuration.GetValue<bool?>("Backtest:ExitPolicy:EnableStructuralStop") ?? false,
            StructuralStopMode: configuration.GetValue<string?>("Backtest:ExitPolicy:StructuralStopMode") ?? "RangeLow",
            MaxHoldMinutes: configuration.GetValue<int?>("Backtest:ExitPolicy:MaxHoldMinutes"),
            EnableHalfLockBreakevenExit: configuration.GetValue<bool?>("Backtest:ExitPolicy:EnableHalfLockBreakevenExit") ?? false,
            EnableFeeAwareTimeStopExit: configuration.GetValue<bool?>("Backtest:ExitPolicy:EnableFeeAwareTimeStopExit") ?? false,
            EnableCostCoveredBreakevenExit: configuration.GetValue<bool?>("Backtest:ExitPolicy:EnableCostCoveredBreakevenExit") ?? false,
            CostCoverMinNetPercent: Math.Max(0m, configuration.GetValue<decimal?>("Backtest:ExitPolicy:CostCoverMinNetPercent") ?? 0m),
            NoProgressExitMinutes: configuration.GetValue<int?>("Backtest:ExitPolicy:NoProgressExitMinutes"));
    }

    public static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string symbolsText,
        MovingAverageTrendStrategy strategy,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        BacktestEntryGuard guard,
        PullbackFollowThroughV2Filter pullbackV2Filter,
        ExecutionSimulator simulator,
        ProfileSignalStats signalStats,
        List<BlockedEntryRecord> blockedEntriesDestination,
        CancellationToken cancellationToken,
        BnbPullbackEntryGuard? bnbPullbackGuard = null,
        decimal? profitLockThresholdPercent = null,
        bool isV2Profile = false,
        BnbRetestContinuationV1Model? retestContinuationModel = null,
        CandidateReachabilityCollector? reachabilityCollector = null,
        IReadOnlyList<KlineCandle>? sourceOneMinuteCandles = null)
    {
        var trades = new List<SimulatedTrade>();
        var required = Math.Max(2, strategy.RequiredPeriods);
        StrategyEvaluation? lastEvaluation = null;

        for (var i = 0; i < candles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i + 1 < required)
                continue;

            var snapshot = MarketSnapshotFactory.Build(candles, i);
            var signal = strategy.GenerateSignalAsync(snapshot, cancellationToken, allowStateMutation: true)
                .GetAwaiter()
                .GetResult();
            lastEvaluation = new StrategyEvaluation(signal, snapshot);

            if (signal.Signal == TradeSignal.Buy)
            {
                signalStats.RawBuySignals++;
                var stagedDecision = pullbackV2Filter.Evaluate(symbol, signal, snapshot, simulator.HasOpenPosition(symbol));
                if (stagedDecision.IsRejected)
                {
                    signalStats.IncrementStrategyRejected(stagedDecision.RejectionReason ?? "Strategy:UnknownReject");
                    blockedEntriesDestination.Add(new BlockedEntryRecord
                    {
                        Interval = interval,
                        ProfileName = profileName,
                        Symbols = symbolsText,
                        Symbol = symbol,
                        TimeUtc = snapshot.TimestampUtc,
                        Reason = stagedDecision.RejectionReason ?? "Strategy:UnknownReject",
                        Confidence = signal.Confidence,
                        ConfidenceThreshold = 0m,
                        ExpectedMovePercent = signal.ExpectedMovePercent,
                        ExpectedTargetSource = signal.ExpectedTargetSource,
                        SignalReason = signal.Reason,
                        RejectionLayer = "Strategy",
                        PullbackSetupDetected = stagedDecision.Diagnostics?.PullbackSetupDetected,
                        PullbackReclaimConfirmed = stagedDecision.Diagnostics?.PullbackReclaimConfirmed,
                        PullbackFollowThroughConfirmed = stagedDecision.Diagnostics?.PullbackFollowThroughConfirmed,
                        PullbackRejectedReason = stagedDecision.Diagnostics?.PullbackRejectedReason,
                        ReclaimReferencePrice = stagedDecision.Diagnostics?.ReclaimReferencePrice,
                        FollowThroughReferencePrice = stagedDecision.Diagnostics?.FollowThroughReferencePrice,
                        CandlesWaitedAfterReclaim = stagedDecision.Diagnostics?.CandlesWaitedAfterReclaim,
                        ResidualExpectedMovePercent = stagedDecision.Diagnostics?.ResidualExpectedMovePercent,
                        ResidualEstimatedNetMovePercent = stagedDecision.Diagnostics?.ResidualEstimatedNetMovePercent,
                        ResidualRewardRisk = stagedDecision.Diagnostics?.ResidualRewardRisk,
                        DistanceFromEntryToExpectedTargetPercent = stagedDecision.Diagnostics?.DistanceFromEntryToExpectedTargetPercent
                    });
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        signal, snapshot, "Strategy", stagedDecision.RejectionReason ?? "Strategy:UnknownReject", 0m, null, false);
                    continue;
                }

                if (stagedDecision.IsPending)
                {
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        signal, snapshot, "Strategy", "Strategy:PullbackFollowThroughPending", 0m, null, false);
                    continue;
                }

                var signalToExecute = stagedDecision.IsExecute && stagedDecision.SignalToExecute is not null
                    ? stagedDecision.SignalToExecute
                    : signal;

                if (BnbRetestContinuationReplayHelper.TryBlock(
                        retestContinuationModel,
                        symbol,
                        signalToExecute,
                        snapshot,
                        interval,
                        profitLockThresholdPercent,
                        profileName,
                        symbolsText,
                        snapshot.TimestampUtc,
                        signal.Confidence,
                        0m,
                        null,
                        stagedDecision.Diagnostics,
                        signalStats,
                        blockedEntriesDestination,
                        out signalToExecute,
                        out var retestDiagnostics))
                {
                    var retestReason = blockedEntriesDestination.Count > 0
                        ? blockedEntriesDestination[^1].Reason
                        : "RetestContinuation:Rejected";
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        signalToExecute, snapshot, "RetestContinuation", retestReason, 0m, null, false);
                    continue;
                }

                var decision = guard.Evaluate(symbol, signalToExecute, snapshot, simulator.HasOpenPosition(symbol));
                if (!decision.IsAllowed)
                {
                    signalStats.IncrementBlocked(decision.Reason);
                    blockedEntriesDestination.Add(new BlockedEntryRecord
                    {
                        Interval = interval,
                        ProfileName = profileName,
                        Symbols = symbolsText,
                        Symbol = symbol,
                        TimeUtc = snapshot.TimestampUtc,
                        Reason = decision.Reason,
                        Confidence = signal.Confidence,
                        ConfidenceThreshold = decision.ConfidenceThreshold,
                        ExpectedMovePercent = signalToExecute.ExpectedMovePercent,
                        EstimatedNetMovePercent = decision.EstimatedNetMovePercent,
                        ExpectedTargetSource = signalToExecute.ExpectedTargetSource,
                        SignalReason = signalToExecute.Reason,
                        RejectionLayer = "Guard",
                        PullbackSetupDetected = stagedDecision.Diagnostics?.PullbackSetupDetected,
                        PullbackReclaimConfirmed = stagedDecision.Diagnostics?.PullbackReclaimConfirmed,
                        PullbackFollowThroughConfirmed = stagedDecision.Diagnostics?.PullbackFollowThroughConfirmed,
                        PullbackRejectedReason = stagedDecision.Diagnostics?.PullbackRejectedReason,
                        ReclaimReferencePrice = stagedDecision.Diagnostics?.ReclaimReferencePrice,
                        FollowThroughReferencePrice = stagedDecision.Diagnostics?.FollowThroughReferencePrice,
                        CandlesWaitedAfterReclaim = stagedDecision.Diagnostics?.CandlesWaitedAfterReclaim,
                        ResidualExpectedMovePercent = stagedDecision.Diagnostics?.ResidualExpectedMovePercent,
                        ResidualEstimatedNetMovePercent = stagedDecision.Diagnostics?.ResidualEstimatedNetMovePercent,
                        ResidualRewardRisk = stagedDecision.Diagnostics?.ResidualRewardRisk,
                        DistanceFromEntryToExpectedTargetPercent = stagedDecision.Diagnostics?.DistanceFromEntryToExpectedTargetPercent
                    });
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        signalToExecute, snapshot, "Guard", decision.Reason, decision.ConfidenceThreshold, decision.EstimatedNetMovePercent, false);
                    continue;
                }

                if (BnbPullbackGuardReplayHelper.TryBlock(
                        bnbPullbackGuard,
                        symbol,
                        signalToExecute,
                        stagedDecision.Diagnostics,
                        interval,
                        profitLockThresholdPercent,
                        isV2Profile,
                        profileName,
                        symbolsText,
                        snapshot.TimestampUtc,
                        signal.Confidence,
                        decision.ConfidenceThreshold,
                        decision.EstimatedNetMovePercent,
                        signalStats,
                        blockedEntriesDestination,
                        out var bnbDiagnostics))
                {
                    var bnbReason = blockedEntriesDestination.Count > 0
                        ? blockedEntriesDestination[^1].Reason
                        : "BnbPullbackGuard:Rejected";
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        signalToExecute, snapshot, "BnbPullbackGuard", bnbReason, decision.ConfidenceThreshold, decision.EstimatedNetMovePercent, false);
                    continue;
                }

                signalStats.ExecutedBuySignals++;
                CandidateReachabilityReplayHelper.CaptureCandidate(
                    reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                    signalToExecute, snapshot, "Executed", string.Empty, decision.ConfidenceThreshold, decision.EstimatedNetMovePercent, true);
                simulator.OnSignal(
                    interval,
                    symbol,
                    quantity,
                    signalToExecute,
                    snapshot,
                    profileName,
                    symbolsText,
                    trades,
                    wasGuarded: true,
                    estimatedRoundTripCostPercent: decision.EstimatedRoundTripCostPercent,
                    estimatedNetMovePercent: decision.EstimatedNetMovePercent,
                    pullbackV2Diagnostics: stagedDecision.Diagnostics,
                    bnbGuardDiagnostics: bnbDiagnostics,
                    retestDiagnostics: retestDiagnostics);
                continue;
            }

            var pendingDecision = pullbackV2Filter.Evaluate(symbol, signal, snapshot, simulator.HasOpenPosition(symbol));
            if (pendingDecision.IsRejected)
            {
                signalStats.IncrementStrategyRejected(pendingDecision.RejectionReason ?? "Strategy:UnknownReject");
                blockedEntriesDestination.Add(new BlockedEntryRecord
                {
                    Interval = interval,
                    ProfileName = profileName,
                    Symbols = symbolsText,
                    Symbol = symbol,
                    TimeUtc = snapshot.TimestampUtc,
                    Reason = pendingDecision.RejectionReason ?? "Strategy:UnknownReject",
                    Confidence = signal.Confidence,
                    ConfidenceThreshold = 0m,
                    ExpectedMovePercent = signal.ExpectedMovePercent,
                    ExpectedTargetSource = signal.ExpectedTargetSource,
                    SignalReason = signal.Reason,
                    RejectionLayer = "Strategy",
                    PullbackSetupDetected = pendingDecision.Diagnostics?.PullbackSetupDetected,
                    PullbackReclaimConfirmed = pendingDecision.Diagnostics?.PullbackReclaimConfirmed,
                    PullbackFollowThroughConfirmed = pendingDecision.Diagnostics?.PullbackFollowThroughConfirmed,
                    PullbackRejectedReason = pendingDecision.Diagnostics?.PullbackRejectedReason,
                    ReclaimReferencePrice = pendingDecision.Diagnostics?.ReclaimReferencePrice,
                    FollowThroughReferencePrice = pendingDecision.Diagnostics?.FollowThroughReferencePrice,
                    CandlesWaitedAfterReclaim = pendingDecision.Diagnostics?.CandlesWaitedAfterReclaim,
                    ResidualExpectedMovePercent = pendingDecision.Diagnostics?.ResidualExpectedMovePercent,
                    ResidualEstimatedNetMovePercent = pendingDecision.Diagnostics?.ResidualEstimatedNetMovePercent,
                    ResidualRewardRisk = pendingDecision.Diagnostics?.ResidualRewardRisk,
                    DistanceFromEntryToExpectedTargetPercent = pendingDecision.Diagnostics?.DistanceFromEntryToExpectedTargetPercent
                });
                CandidateReachabilityReplayHelper.CaptureCandidate(
                    reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                    signal, snapshot, "Strategy", pendingDecision.RejectionReason ?? "Strategy:UnknownReject", 0m, null, false);
                continue;
            }

            if (pendingDecision.IsExecute && pendingDecision.SignalToExecute is not null)
            {
                var delayedSignalToExecute = pendingDecision.SignalToExecute;
                if (BnbRetestContinuationReplayHelper.TryBlock(
                        retestContinuationModel,
                        symbol,
                        delayedSignalToExecute,
                        snapshot,
                        interval,
                        profitLockThresholdPercent,
                        profileName,
                        symbolsText,
                        snapshot.TimestampUtc,
                        pendingDecision.SignalToExecute.Confidence,
                        0m,
                        null,
                        pendingDecision.Diagnostics,
                        signalStats,
                        blockedEntriesDestination,
                        out delayedSignalToExecute,
                        out var delayedRetestDiagnostics))
                {
                    var retestReason = blockedEntriesDestination.Count > 0
                        ? blockedEntriesDestination[^1].Reason
                        : "RetestContinuation:Rejected";
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        delayedSignalToExecute, snapshot, "RetestContinuation", retestReason, 0m, null, false);
                    continue;
                }

                var delayedGuardDecision = guard.Evaluate(symbol, delayedSignalToExecute, snapshot, simulator.HasOpenPosition(symbol));
                if (!delayedGuardDecision.IsAllowed)
                {
                    signalStats.IncrementBlocked(delayedGuardDecision.Reason);
                    blockedEntriesDestination.Add(new BlockedEntryRecord
                    {
                        Interval = interval,
                        ProfileName = profileName,
                        Symbols = symbolsText,
                        Symbol = symbol,
                        TimeUtc = snapshot.TimestampUtc,
                        Reason = delayedGuardDecision.Reason,
                        Confidence = pendingDecision.SignalToExecute.Confidence,
                        ConfidenceThreshold = delayedGuardDecision.ConfidenceThreshold,
                        ExpectedMovePercent = delayedSignalToExecute.ExpectedMovePercent,
                        EstimatedNetMovePercent = delayedGuardDecision.EstimatedNetMovePercent,
                        ExpectedTargetSource = delayedSignalToExecute.ExpectedTargetSource,
                        SignalReason = delayedSignalToExecute.Reason,
                        RejectionLayer = "Guard",
                        PullbackSetupDetected = pendingDecision.Diagnostics?.PullbackSetupDetected,
                        PullbackReclaimConfirmed = pendingDecision.Diagnostics?.PullbackReclaimConfirmed,
                        PullbackFollowThroughConfirmed = pendingDecision.Diagnostics?.PullbackFollowThroughConfirmed,
                        PullbackRejectedReason = pendingDecision.Diagnostics?.PullbackRejectedReason,
                        ReclaimReferencePrice = pendingDecision.Diagnostics?.ReclaimReferencePrice,
                        FollowThroughReferencePrice = pendingDecision.Diagnostics?.FollowThroughReferencePrice,
                        CandlesWaitedAfterReclaim = pendingDecision.Diagnostics?.CandlesWaitedAfterReclaim,
                        ResidualExpectedMovePercent = pendingDecision.Diagnostics?.ResidualExpectedMovePercent,
                        ResidualEstimatedNetMovePercent = pendingDecision.Diagnostics?.ResidualEstimatedNetMovePercent,
                        ResidualRewardRisk = pendingDecision.Diagnostics?.ResidualRewardRisk,
                        DistanceFromEntryToExpectedTargetPercent = pendingDecision.Diagnostics?.DistanceFromEntryToExpectedTargetPercent
                    });
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        delayedSignalToExecute, snapshot, "Guard", delayedGuardDecision.Reason, delayedGuardDecision.ConfidenceThreshold, delayedGuardDecision.EstimatedNetMovePercent, false);
                    continue;
                }

                if (BnbPullbackGuardReplayHelper.TryBlock(
                        bnbPullbackGuard,
                        symbol,
                        delayedSignalToExecute,
                        pendingDecision.Diagnostics,
                        interval,
                        profitLockThresholdPercent,
                        isV2Profile,
                        profileName,
                        symbolsText,
                        snapshot.TimestampUtc,
                        pendingDecision.SignalToExecute.Confidence,
                        delayedGuardDecision.ConfidenceThreshold,
                        delayedGuardDecision.EstimatedNetMovePercent,
                        signalStats,
                        blockedEntriesDestination,
                        out var delayedBnbDiagnostics))
                {
                    var bnbReason = blockedEntriesDestination.Count > 0
                        ? blockedEntriesDestination[^1].Reason
                        : "BnbPullbackGuard:Rejected";
                    CandidateReachabilityReplayHelper.CaptureCandidate(
                        reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                        delayedSignalToExecute, snapshot, "BnbPullbackGuard", bnbReason, delayedGuardDecision.ConfidenceThreshold, delayedGuardDecision.EstimatedNetMovePercent, false);
                    continue;
                }

                signalStats.ExecutedBuySignals++;
                CandidateReachabilityReplayHelper.CaptureCandidate(
                    reachabilityCollector, sourceOneMinuteCandles, guard, interval, profileName, symbolsText, symbol,
                    delayedSignalToExecute, snapshot, "Executed", string.Empty, delayedGuardDecision.ConfidenceThreshold, delayedGuardDecision.EstimatedNetMovePercent, true);
                simulator.OnSignal(
                    interval,
                    symbol,
                    quantity,
                    delayedSignalToExecute,
                    snapshot,
                    profileName,
                    symbolsText,
                    trades,
                    wasGuarded: true,
                    estimatedRoundTripCostPercent: delayedGuardDecision.EstimatedRoundTripCostPercent,
                    estimatedNetMovePercent: delayedGuardDecision.EstimatedNetMovePercent,
                    pullbackV2Diagnostics: pendingDecision.Diagnostics,
                    bnbGuardDiagnostics: delayedBnbDiagnostics,
                    retestDiagnostics: delayedRetestDiagnostics);
                continue;
            }

            simulator.OnSignal(interval, symbol, quantity, signal, snapshot, profileName, symbolsText, trades);
        }

        if (forceCloseAtEnd && lastEvaluation is not null)
        {
            simulator.ForceClose(symbol, lastEvaluation.Snapshot, "EndOfData", profileName, symbolsText, trades);
        }

        return trades;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildDefaultProfiles()
    {
        var strategyVariants = new[]
        {
            new
            {
                Name = "current-guarded-baseline",
                Overrides = new Dictionary<string, string>()
            },
            new
            {
                Name = "lowvol-disabled",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.00",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true"
                }
            },
            new
            {
                Name = "lowvol-disabled-highvol-pullback-block",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.00",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnablePullbackOverrideHighVolatilityBlock"] = "true"
                }
            },
            new
            {
                Name = "pullback-disabled",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true"
                }
            },
            new
            {
                Name = "pullback-strict-rr-1.40",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.40",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true"
                }
            },
            new
            {
                Name = "pullback-strict-rr-1.60",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.60",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true"
                }
            },
            new
            {
                Name = "pullback-nearhigh-trendstrength-strict",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendNearRecentHighRejection"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresRewardRisk"] = "1.50",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresTrendStrengthPercent"] = "0.0012",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true"
                }
            },
            new
            {
                Name = "pullback-reclaim-prevhigh-rr-1.20",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30"
                }
            },
            new
            {
                Name = "pullback-reclaim-followthrough-v2",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:PullbackFollowThroughV2:Enabled"] = "true"
                }
            },
            new
            {
                Name = "pullback-reclaim-followthrough-v3",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:PullbackFollowThroughV2:Enabled"] = "true",
                    ["Backtest:PullbackFollowThroughV3:Enabled"] = "true",
                    ["PullbackV3MinResidualExpectedMovePercent"] = "0.35",
                    ["PullbackV3MinResidualNetMovePercent"] = "0.12",
                    ["PullbackV3MinResidualRewardRisk"] = "1.25",
                    ["PullbackV3RejectIfTargetAlreadyMostlyConsumed"] = "true",
                    ["PullbackV3MaxTargetConsumedPercent"] = "55"
                }
            },
            new
            {
                Name = "pullback-v2-profitlock-90",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:PullbackFollowThroughV2:Enabled"] = "true",
                    ["Backtest:ExitPolicy:Name"] = "ProfitLock90",
                    ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "90",
                    ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                    ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
                }
            },
            new
            {
                Name = "pullback-v2-profitlock-95",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:PullbackFollowThroughV2:Enabled"] = "true",
                    ["Backtest:ExitPolicy:Name"] = "ProfitLock95",
                    ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "95",
                    ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                    ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
                }
            },
            new
            {
                Name = "pullback-v2-profitlock-98",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:PullbackFollowThroughV2:Enabled"] = "true",
                    ["Backtest:ExitPolicy:Name"] = "ProfitLock98",
                    ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "98",
                    ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                    ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
                }
            },
            new
            {
                Name = "pullback-prevhigh-profitlock-90",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:ExitPolicy:Name"] = "ProfitLock90",
                    ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "90",
                    ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                    ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
                }
            },
            new
            {
                Name = "pullback-prevhigh-profitlock-95",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:ExitPolicy:Name"] = "ProfitLock95",
                    ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "95",
                    ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                    ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
                }
            },
            new
            {
                Name = "pullback-prevhigh-profitlock-98",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
                    ["Backtest:ExitPolicy:Name"] = "ProfitLock98",
                    ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "98",
                    ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                    ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
                }
            },
            new
            {
                Name = "lowvol-strict-confirmed",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "20",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutBufferPercent"] = "0.0010",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles"] = "2",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:RequireNoImmediateBearishCandleAfterBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:MinBreakoutSlopePercent"] = "0.0004"
                }
            },
            new
            {
                Name = "lowvol-very-strict",
                Overrides = new Dictionary<string, string>
                {
                    ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles"] = "24",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutBufferPercent"] = "0.0020",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles"] = "2",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:RequireNoImmediateBearishCandleAfterBreakout"] = "true",
                    ["DecisionEngine:MovingAverageCrossoverStrategy:MinBreakoutSlopePercent"] = "0.0008"
                }
            }
        };

        var symbolSets = new[]
        {
            new[] { TradingSymbol.ETHUSDT },
            new[] { TradingSymbol.BNBUSDT },
            new[] { TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT },
            new[] { TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT }
        };

        var profiles = new List<ReplayProfileDefinition>();
        foreach (var variant in strategyVariants)
        {
            foreach (var symbols in symbolSets)
            {
                var symbolLabel = string.Join("+", symbols.Select(s => s.ToString().Replace("USDT", string.Empty, StringComparison.OrdinalIgnoreCase)));
                profiles.Add(new ReplayProfileDefinition(
                    $"{variant.Name}-{symbolLabel}",
                    symbols,
                    variant.Overrides));
            }
        }

        return profiles;
    }

    private static ProfileRuntimeSnapshot ReadProfileRuntimeSnapshot(IConfiguration configuration)
    {
        return new ProfileRuntimeSnapshot(
            EnableLowVolatilityBreakoutEntry: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry") ?? true,
            EnableNormalTrendPullbackContinuationOverride: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride") ?? false,
            NormalTrendPullbackMinExpectedRewardRisk: Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk") ?? 0.80m),
            EnableNormalTrendMinDistanceToInvalidationFilter: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter") ?? false,
            NormalTrendMinDistanceToInvalidationPercent: Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent") ?? 0.15m),
            EnableNormalTrendNearRecentHighRejection: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendNearRecentHighRejection") ?? false,
            NormalTrendNearRecentHighRequiresRewardRisk: Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresRewardRisk") ?? 1.20m),
            NormalTrendNearRecentHighRequiresTrendStrengthPercent: configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendNearRecentHighRequiresTrendStrengthPercent"),
            EnablePullbackOverrideHighVolatilityBlock: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:EnablePullbackOverrideHighVolatilityBlock") ?? false,
            EnableNormalTrendPullbackReclaimConfirmationFilter: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter") ?? false,
            NormalTrendPullbackReclaimMode: configuration.GetValue<string?>("DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode") ?? "PreviousCandleHigh",
            EnablePullbackFollowThroughV2: configuration.GetValue<bool?>("Backtest:PullbackFollowThroughV2:Enabled") ?? false,
            EnablePullbackFollowThroughV3: configuration.GetValue<bool?>("Backtest:PullbackFollowThroughV3:Enabled") ?? false,
            PullbackV3MinResidualExpectedMovePercent: Math.Max(0m, configuration.GetValue<decimal?>("PullbackV3MinResidualExpectedMovePercent") ?? 0.35m),
            PullbackV3MinResidualNetMovePercent: Math.Max(0m, configuration.GetValue<decimal?>("PullbackV3MinResidualNetMovePercent") ?? 0.12m),
            PullbackV3MinResidualRewardRisk: Math.Max(0m, configuration.GetValue<decimal?>("PullbackV3MinResidualRewardRisk") ?? 1.25m),
            PullbackV3RejectIfTargetAlreadyMostlyConsumed: configuration.GetValue<bool?>("PullbackV3RejectIfTargetAlreadyMostlyConsumed") ?? true,
            PullbackV3MaxTargetConsumedPercent: Math.Clamp(configuration.GetValue<decimal?>("PullbackV3MaxTargetConsumedPercent") ?? 55m, 0m, 100m),
            ExitPolicyName: configuration.GetValue<string?>("Backtest:ExitPolicy:Name") ?? "OppositeSignalOnly",
            ProfitLockThresholdPercent: configuration.GetValue<decimal?>("Backtest:ExitPolicy:ProfitLockThresholdPercent"),
            BreakoutLookbackCandles: Math.Max(2, configuration.GetValue<int?>("DecisionEngine:MovingAverageCrossoverStrategy:BreakoutLookbackCandles") ?? 10),
            BreakoutBufferPercent: Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:BreakoutBufferPercent") ?? 0m),
            BreakoutConfirmationCandles: Math.Max(1, configuration.GetValue<int?>("DecisionEngine:MovingAverageCrossoverStrategy:BreakoutConfirmationCandles") ?? 1),
            MinBreakoutSlopePercent: Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:MovingAverageCrossoverStrategy:MinBreakoutSlopePercent") ?? 0m),
            UseConfirmedClosedCandlesForLowVolBreakout: configuration.GetValue<bool?>("DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout") ?? false,
            BnbPullbackGuardEnabled: configuration.GetValue<bool?>("Backtest:BnbPullbackGuard:Enabled") ?? false,
            BnbPullbackGuardMode: configuration.GetValue<string?>("Backtest:BnbPullbackGuard:Mode") ?? "Combined");
    }

    public static string ResolveIntervalOutputDirectory(string baseOutputDirectory, string interval, bool multiIntervalRun)
        => multiIntervalRun ? Path.Combine(baseOutputDirectory, interval) : baseOutputDirectory;

    public static IReadOnlyList<ReplayProfileDefinition> BuildLegacyRobustnessCandidateProfiles()
    {
        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pullback-v2-profitlock-90-BNB",
            "pullback-v2-profitlock-95-BNB",
            "pullback-v2-profitlock-98-BNB",
            "pullback-prevhigh-profitlock-90-BNB",
            "pullback-prevhigh-profitlock-95-BNB",
            "pullback-prevhigh-profitlock-98-BNB",
            "pullback-v2-profitlock-90-ETH+BNB+SOL",
            "pullback-v2-profitlock-95-ETH+BNB+SOL",
            "pullback-v2-profitlock-98-ETH+BNB+SOL",
            "pullback-prevhigh-profitlock-90-ETH+BNB+SOL",
            "pullback-prevhigh-profitlock-95-ETH+BNB+SOL",
            "pullback-prevhigh-profitlock-98-ETH+BNB+SOL"
        };

        return BuildDefaultProfiles()
            .Where(p => candidateNames.Contains(p.ProfileName))
            .ToArray();
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRobustnessCandidateProfiles()
        => BuildBnbRetestRobustnessProfiles();

    public static IReadOnlyList<ReplayProfileDefinition> BuildBnbRetestRobustnessProfiles()
    {
        var profiles = new List<ReplayProfileDefinition>();
        profiles.AddRange(BuildBnbRetestContinuationV1Profiles());
        profiles.AddRange(BuildBnbRetestTargetVariantProfiles());
        profiles.AddRange(BuildBnbRetestComparisonProfiles());
        return profiles;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildBnbRetestContinuationV1Profiles()
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var (threshold, policyName, thresholdValue) in new[]
        {
            ("90", "ProfitLock90", "90"),
            ("95", "ProfitLock95", "95"),
            ("98", "ProfitLock98", "98")
        })
        {
            var overrides = CreateRetestContinuationBaseOverrides(BnbRetestTargetModelName.CappedExpectedMoveTarget);
            overrides["Backtest:ExitPolicy:Name"] = policyName;
            overrides["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = thresholdValue;
            profiles.Add(new ReplayProfileDefinition(
                $"bnb-retest-continuation-v1-profitlock-{threshold}",
                [TradingSymbol.BNBUSDT],
                overrides));
        }

        return profiles;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildBnbRetestTargetVariantProfiles()
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var (suffix, model) in new (string, BnbRetestTargetModelName)[]
        {
            ("raw", BnbRetestTargetModelName.RawCurrentModelTarget),
            ("capped", BnbRetestTargetModelName.CappedExpectedMoveTarget),
            ("atr", BnbRetestTargetModelName.AtrLimitedTarget),
            ("range", BnbRetestTargetModelName.RecentRangeRetestTarget)
        })
        {
            var overrides = CreateRetestContinuationBaseOverrides(model);
            overrides["Backtest:ExitPolicy:Name"] = "ProfitLock98";
            overrides["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "98";
            profiles.Add(new ReplayProfileDefinition(
                $"bnb-retest-continuation-v1-{suffix}-profitlock-98",
                [TradingSymbol.BNBUSDT],
                overrides));
        }

        return profiles;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildBnbRetestComparisonProfiles()
    {
        var guardProfiles = BuildBnbGuardProfiles();
        var baselineProfiles = BuildDefaultProfiles();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bnb-guard-prevhigh-profitlock-98-fields",
            "bnb-guard-v2-profitlock-98",
            "pullback-prevhigh-profitlock-98-BNB",
            "pullback-v2-profitlock-98-BNB"
        };

        var v2BaselineOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "true"
        };

        var comparisons = new List<ReplayProfileDefinition>();
        comparisons.AddRange(guardProfiles.Where(p => names.Contains(p.ProfileName)));

        foreach (var baseline in baselineProfiles.Where(p => names.Contains(p.ProfileName)))
        {
            if (baseline.ProfileName.StartsWith("pullback-v2-", StringComparison.OrdinalIgnoreCase))
            {
                var merged = new Dictionary<string, string>(baseline.ConfigOverrides, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in v2BaselineOverrides)
                    merged[kv.Key] = kv.Value;
                comparisons.Add(new ReplayProfileDefinition(baseline.ProfileName, baseline.Symbols, merged));
            }
            else
            {
                comparisons.Add(baseline);
            }
        }

        return comparisons
            .GroupBy(p => p.ProfileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static Dictionary<string, string> CreateRetestContinuationBaseOverrides(BnbRetestTargetModelName targetModel)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
            ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "true",
            ["Backtest:BnbRetestContinuationV1:TargetModel"] = targetModel.ToString(),
            ["Backtest:BnbRetestContinuationV1:MaxCappedExpectedMovePercent"] = "0.50",
            ["Backtest:BnbRetestContinuationV1:MaxConsecutiveBullishTrendCandles"] = "2",
            ["Backtest:BnbRetestContinuationV1:MaxTrendStrengthPercent"] = "0.00090",
            ["Backtest:BnbRetestContinuationV1:MaxDistanceToInvalidationPercent"] = "0.40",
            ["Backtest:BnbRetestContinuationV1:RejectPreviousCandleBearish"] = "true",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
        };
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildBnbGuardProfiles()
    {
        var profiles = new List<ReplayProfileDefinition>();
        var modes = new (string Suffix, string Mode)[]
        {
            ("", "Combined"),
            ("-fields", "FieldsOnly"),
            ("-lockreach", "LockReachabilityOnly")
        };

        var prevhighBase = new Dictionary<string, string>
        {
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
            ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
            ["Backtest:BnbPullbackGuard:Enabled"] = "true"
        };

        var v2Extra = new Dictionary<string, string>
        {
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "true"
        };

        foreach (var (threshold, policyName, thresholdValue) in new[]
        {
            ("90", "ProfitLock90", "90"),
            ("95", "ProfitLock95", "95"),
            ("98", "ProfitLock98", "98")
        })
        {
            foreach (var (suffix, mode) in modes)
            {
                var prevhighOverrides = new Dictionary<string, string>(prevhighBase, StringComparer.OrdinalIgnoreCase)
                {
                    ["Backtest:BnbPullbackGuard:Mode"] = mode,
                    ["Backtest:ExitPolicy:Name"] = policyName,
                    ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = thresholdValue,
                    ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                    ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
                };
                profiles.Add(new ReplayProfileDefinition(
                    $"bnb-guard-prevhigh-profitlock-{threshold}{suffix}",
                    [TradingSymbol.BNBUSDT],
                    prevhighOverrides));

                var v2Overrides = new Dictionary<string, string>(prevhighOverrides, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in v2Extra)
                    v2Overrides[kv.Key] = kv.Value;

                profiles.Add(new ReplayProfileDefinition(
                    $"bnb-guard-v2-profitlock-{threshold}{suffix}",
                    [TradingSymbol.BNBUSDT],
                    v2Overrides));
            }
        }

        return profiles;
    }

    public static ReplayProfileDefinition BuildBroadReachabilityScannerProfile(TradingSymbol symbol, string interval)
    {
        var overrides = BuildReachabilityResearchBaseOverrides();
        overrides["Backtest:ReachabilityResearch:ProfileInterval"] = interval;
        return new ReplayProfileDefinition(
            $"reachability-scan-{symbol}-{interval}",
            [symbol],
            overrides);
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildReachabilityResearchProfiles(bool includeExperimental = true)
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            profiles.Add(new ReplayProfileDefinition(
                $"bnb-reachability-research-{interval}",
                [TradingSymbol.BNBUSDT],
                BuildReachabilityResearchOverrides(interval)));
        }

        if (includeExperimental)
        {
            profiles.Add(new ReplayProfileDefinition(
                "bnb-reachability-confidence-relaxed-1m",
                [TradingSymbol.BNBUSDT],
                BuildReachabilityConfidenceRelaxedOverrides()));
        }

        return profiles;
    }

    public static string ResolveReachabilityProfileInterval(ReplayProfileDefinition profile)
    {
        if (profile.ProfileName.EndsWith("-1m", StringComparison.OrdinalIgnoreCase))
            return "1m";
        if (profile.ProfileName.EndsWith("-3m", StringComparison.OrdinalIgnoreCase))
            return "3m";
        if (profile.ProfileName.EndsWith("-5m", StringComparison.OrdinalIgnoreCase))
            return "5m";
        return profile.ConfigOverrides.TryGetValue("Backtest:ReachabilityResearch:ProfileInterval", out var configured)
            ? configured
            : "1m";
    }

    private static Dictionary<string, string> BuildReachabilityResearchOverrides(string interval)
    {
        var overrides = BuildReachabilityResearchBaseOverrides();
        overrides["Backtest:ReachabilityResearch:ProfileInterval"] = interval;
        return overrides;
    }

    private static Dictionary<string, string> BuildReachabilityConfidenceRelaxedOverrides()
    {
        var overrides = BuildReachabilityResearchBaseOverrides();
        overrides["Backtest:ReachabilityResearch:ProfileInterval"] = "1m";
        overrides["Backtest:ReachabilityConfidenceRelaxation:Enabled"] = "true";
        overrides["Backtest:ReachabilityConfidenceRelaxation:MaxLock90DistancePercent"] = "0.40";
        overrides["Backtest:ReachabilityConfidenceRelaxation:MaxDistanceToInvalidationPercent"] = "0.40";
        overrides["Backtest:ReachabilityConfidenceRelaxation:RelaxedMinConfidence"] = "0.65";
        return overrides;
    }

    private static Dictionary<string, string> BuildReachabilityResearchBaseOverrides()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:ReachabilityResearch:Enabled"] = "true",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackMinExpectedRewardRisk"] = "1.20",
            ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForEntryQuality"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:UseConfirmedClosedCandlesForLowVolBreakout"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackReclaimConfirmationFilter"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendPullbackReclaimMode"] = "PreviousCandleHigh",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendMinDistanceToInvalidationFilter"] = "true",
            ["DecisionEngine:MovingAverageCrossoverStrategy:NormalTrendMinDistanceToInvalidationPercent"] = "0.30",
            ["Backtest:ExitPolicy:Name"] = "ProfitLock98",
            ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "98",
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
        };
    }

    public ProfileReplayContext BuildProfileReplayContext(ReplayProfileDefinition profile)
        => BuildProfileReplayContext(settings, profile);

    public static ProfileReplayContext BuildProfileReplayContext(BacktestSettings settings, ReplayProfileDefinition profile)
    {
        var app = new BacktestApplication(settings);
        var configuration = app.BuildConfiguration(profile.ConfigOverrides);
        var trendSettings = new TrendStateSettings
        {
            MinTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinTrendStrengthPercent") ?? 0.001m),
            StrongTrendStrengthPercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:StrongTrendStrengthPercent") ?? 0.003m),
            MinSlopePercent = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:MinSlopePercent") ?? 0.0005m),
            LowVolatilityRangePercentThreshold = Math.Max(0m, configuration.GetValue<decimal?>("DecisionEngine:TrendState:LowVolatilityRangePercentThreshold") ?? 0.0008m),
            MinRangeCandlesForVolatilityCheck = Math.Max(2, configuration.GetValue<int?>("DecisionEngine:TrendState:MinRangeCandlesForVolatilityCheck") ?? 10)
        };

        ITrendStateService trendService = new TrendStateService(
            NullLogger<TrendStateService>.Instance,
            Options.Create(trendSettings));
        IVolatilityService volatilityService = new VolatilityService(configuration);
        IAtrService atrService = new AtrService(configuration);
        IMarketConditionService marketConditionService = new MarketConditionService(configuration, volatilityService, atrService);
        var positionManager = new PositionManager();
        var marketStateTracker = new MarketStateTracker();

        var strategy = new MovingAverageTrendStrategy(
            NullLogger<MovingAverageTrendStrategy>.Instance,
            trendService,
            positionManager,
            marketStateTracker,
            marketConditionService,
            configuration);

        var executionCostSettings = new ExecutionCostSettings(
            settings.FeeRatePercent,
            settings.EstimatedSpreadPercent,
            settings.SlippagePercent);

        var runtimeSnapshot = ReadProfileRuntimeSnapshot(configuration);
        return new ProfileReplayContext(
            strategy,
            new BacktestEntryGuard(configuration, executionCostSettings),
            CreateBnbPullbackGuard(configuration),
            CreateRetestContinuationModel(configuration),
            new PullbackFollowThroughV2Filter(configuration, executionCostSettings),
            new ExecutionSimulator(executionCostSettings, ReadExitPolicySettings(configuration)),
            new ProfileSignalStats(),
            runtimeSnapshot,
            configuration);
    }

    private static BnbPullbackEntryGuard? CreateBnbPullbackGuard(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool?>("Backtest:BnbPullbackGuard:Enabled") ?? false;
        return enabled ? new BnbPullbackEntryGuard(configuration) : null;
    }

    private static BnbRetestContinuationV1Model? CreateRetestContinuationModel(IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool?>("Backtest:BnbRetestContinuationV1:Enabled") ?? false;
        return enabled ? new BnbRetestContinuationV1Model(configuration) : null;
    }

    private static int CountGapWarnings(IReadOnlyList<DataQualityIssue> issues)
        => issues.Count(x => x.Message.Contains("Gap detected:", StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<ReplayProfileDefinition> ResolveRangeExpansionProfiles(BacktestSettings settings)
    {
        if (settings.RunRangeExpansionTargetFloorExperiment)
            return BuildRangeExpansionTargetFloorExperimentProfiles(settings.RunRangeExpansionFastIncludeComparison);
        if (settings.RunRangeExpansionV2Fast)
            return BuildRangeExpansionV2FastProfiles();
        if (settings.RunRangeExpansionFast)
            return BuildRangeExpansionFastProfiles(settings.RunRangeExpansionFastIncludeComparison);
        return BuildRangeExpansionBreakoutProfiles(includeComparisonProfiles: true);
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionTargetFloorExperimentProfiles(bool includeComparisonProfiles = true)
    {
        var profiles = new List<ReplayProfileDefinition>();
        var modes = new (string Label, RangeExpansionTargetFloorMode Mode)[]
        {
            ("current", RangeExpansionTargetFloorMode.Current),
            ("relaxed", RangeExpansionTargetFloorMode.Relaxed),
            ("costaware", RangeExpansionTargetFloorMode.CostAware)
        };
        foreach (var (modeLabel, mode) in modes)
        {
            foreach (var (symbolLabel, symbol) in new (string, TradingSymbol)[]
            {
                ("ETH", TradingSymbol.ETHUSDT),
                ("BNB", TradingSymbol.BNBUSDT),
                ("SOL", TradingSymbol.SOLUSDT)
            })
            {
                profiles.Add(CreateRangeExpansionProfile(
                    $"range-expansion-v1-targetfloor-{modeLabel}-{symbolLabel}-1m-profitlock-90",
                    [symbol],
                    "1m",
                    "ProfitLock90",
                    "90",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Backtest:RangeExpansionBreakoutV1:TargetFloorMode"] = mode.ToString()
                    }));
            }

            if (includeComparisonProfiles)
            {
                profiles.Add(CreateRangeExpansionProfile(
                    $"range-expansion-v1-targetfloor-{modeLabel}-ETH+BNB+SOL-1m-profitlock-90",
                    [TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT],
                    "1m",
                    "ProfitLock90",
                    "90",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Backtest:RangeExpansionBreakoutV1:TargetFloorMode"] = mode.ToString()
                    }));
            }
        }

        return profiles;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionFastProfiles(bool includeComparisonProfiles = true)
        => BuildRangeExpansionFastProfilesInternal(includeComparisonProfiles, "range-expansion-v1-fast", null);

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionV2FastProfiles(
        IReadOnlyDictionary<string, string>? extraOverrides = null)
        => BuildRangeExpansionFastProfilesInternal(
            includeComparisonProfiles: true,
            "range-expansion-v2-fast",
            MergeV2ExperimentalOverrides(extraOverrides));

    private static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionFastProfilesInternal(
        bool includeComparisonProfiles,
        string variantPrefix,
        IReadOnlyDictionary<string, string>? extraOverrides)
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var (symbolLabel, symbol) in new (string, TradingSymbol)[]
        {
            ("ETH", TradingSymbol.ETHUSDT),
            ("BNB", TradingSymbol.BNBUSDT),
            ("SOL", TradingSymbol.SOLUSDT)
        })
        {
            profiles.Add(CreateRangeExpansionProfile(
                $"{variantPrefix}-{symbolLabel}-1m-profitlock-90",
                [symbol],
                "1m",
                "ProfitLock90",
                "90",
                extraOverrides));
        }

        if (includeComparisonProfiles)
        {
            profiles.Add(CreateRangeExpansionProfile(
                $"{variantPrefix}-ETH+BNB+SOL-1m-profitlock-90",
                [TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT],
                "1m",
                "ProfitLock90",
                "90",
                extraOverrides));
        }

        return profiles;
    }

    private static ReplayProfileDefinition CreateRangeExpansionProfile(
        string profileName,
        IReadOnlyList<TradingSymbol> symbols,
        string interval,
        string policyName,
        string policyValue,
        IReadOnlyDictionary<string, string>? extraOverrides)
    {
        var overrides = CreateRangeExpansionBaseOverrides(interval, policyName, policyValue);
        if (extraOverrides is not null)
        {
            foreach (var kv in extraOverrides)
                overrides[kv.Key] = kv.Value;
        }

        return new ReplayProfileDefinition(profileName, symbols, overrides);
    }

    private static Dictionary<string, string> MergeV2ExperimentalOverrides(IReadOnlyDictionary<string, string>? extraOverrides)
    {
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RequireFollowThroughCloseAboveBreakoutHigh"] = "true",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RequireBreakoutBodyStrengthPercent"] = "40",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:MaxBreakoutCandleRangeToAtrRatio"] = "1.50",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:MaxMaeRiskProxyPercent"] = "0.35",
            ["Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:TighterAntiChaseCapPercent"] = "0.12",
            ["Backtest:RangeExpansionBreakoutV1:MaxDistanceFromBreakoutPercent"] = "0.10"
        };
        if (extraOverrides is null)
            return defaults;
        foreach (var kv in extraOverrides)
            defaults[kv.Key] = kv.Value;
        return defaults;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionBreakoutProfiles(bool includeComparisonProfiles = true)
    {
        var profiles = new List<ReplayProfileDefinition>();
        var symbols = new (string Label, TradingSymbol Symbol)[]
        {
            ("ETH", TradingSymbol.ETHUSDT),
            ("BNB", TradingSymbol.BNBUSDT),
            ("SOL", TradingSymbol.SOLUSDT)
        };
        var intervals = new[] { "1m", "3m", "5m" };
        var locks = new (string Threshold, string PolicyName, string PolicyValue)[]
        {
            ("90", "ProfitLock90", "90"),
            ("95", "ProfitLock95", "95"),
            ("98", "ProfitLock98", "98")
        };

        foreach (var interval in intervals)
        {
            foreach (var (symbolLabel, symbol) in symbols)
            {
                foreach (var (threshold, policyName, policyValue) in locks)
                {
                    profiles.Add(new ReplayProfileDefinition(
                        $"range-expansion-v1-{symbolLabel}-{interval}-profitlock-{threshold}",
                        [symbol],
                        CreateRangeExpansionBaseOverrides(interval, policyName, policyValue)));
                }
            }

            if (includeComparisonProfiles)
            {
                foreach (var (threshold, policyName, policyValue) in locks)
                {
                    profiles.Add(new ReplayProfileDefinition(
                        $"range-expansion-v1-ETH+BNB+SOL-{interval}-profitlock-{threshold}",
                        [TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT],
                        CreateRangeExpansionBaseOverrides(interval, policyName, policyValue)));
                }
            }
        }

        return profiles;
    }

    public static string ResolveRangeExpansionProfileInterval(ReplayProfileDefinition profile)
    {
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            if (profile.ProfileName.Contains($"-{interval}-", StringComparison.OrdinalIgnoreCase))
                return interval;
        }

        return profile.ConfigOverrides.TryGetValue("Backtest:RangeExpansionBreakoutV1:ProfileInterval", out var configured)
            ? configured
            : "1m";
    }

    public static RangeExpansionReplayContext BuildRangeExpansionReplayContext(
        BacktestSettings settings,
        ReplayProfileDefinition profile)
    {
        var app = new BacktestApplication(settings);
        var configuration = app.BuildConfiguration(profile.ConfigOverrides);
        var executionCostSettings = new ExecutionCostSettings(
            settings.FeeRatePercent,
            settings.EstimatedSpreadPercent,
            settings.SlippagePercent);
        var exitPolicy = ReadExitPolicySettings(configuration);
        return new RangeExpansionReplayContext(
            new RangeExpansionBreakoutV1Model(configuration),
            new ExecutionSimulator(executionCostSettings, exitPolicy),
            new ProfileSignalStats(),
            exitPolicy,
            executionCostSettings,
            configuration);
    }

    private static Dictionary<string, string> CreateRangeExpansionBaseOverrides(
        string interval,
        string policyName,
        string policyValue)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV1:ProfileInterval"] = interval,
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "false",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "false",
            ["Backtest:ExitPolicy:Name"] = policyName,
            ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = policyValue,
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "true"
        };
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionBreakoutV2Profiles(bool includeComparisonProfiles = true)
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var (symbolLabel, symbol) in new (string, TradingSymbol)[]
        {
            ("ETH", TradingSymbol.ETHUSDT),
            ("BNB", TradingSymbol.BNBUSDT),
            ("SOL", TradingSymbol.SOLUSDT)
        })
        {
            profiles.Add(CreateRangeExpansionV2Profile(
                $"range-expansion-v2-{symbolLabel}-1m-profitlock-90",
                [symbol],
                "1m",
                "ProfitLock90",
                "90",
                null));
        }

        if (includeComparisonProfiles)
        {
            profiles.Add(CreateRangeExpansionV2Profile(
                "range-expansion-v2-ETH+BNB+SOL-1m-profitlock-90",
                [TradingSymbol.ETHUSDT, TradingSymbol.BNBUSDT, TradingSymbol.SOLUSDT],
                "1m",
                "ProfitLock90",
                "90",
                null));
        }

        return profiles;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionV21FastProfiles()
    {
        var symbol = TradingSymbol.BNBUSDT;
        var tighterEntry = CreateV21TighterEntryOverrides();
        var halfLockBreakeven = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"] = "true"
        };
        var timestop30 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:ExitPolicy:MaxHoldMinutes"] = "30"
        };

        return
        [
            CreateRangeExpansionV21Profile("baseline", symbol, null),
            CreateRangeExpansionV21Profile("timestop-30", symbol, timestop30),
            CreateRangeExpansionV21Profile("halflock-breakeven", symbol, halfLockBreakeven),
            CreateRangeExpansionV21Profile("tighter-entry", symbol, tighterEntry),
            CreateRangeExpansionV21Profile("tighter-entry-halflock-breakeven", symbol, MergeOverrides(tighterEntry, halfLockBreakeven)),
            CreateRangeExpansionV21Profile("tighter-entry-timestop-30", symbol, MergeOverrides(tighterEntry, timestop30))
        ];
    }

    private static Dictionary<string, string> MergeOverrides(
        params IReadOnlyDictionary<string, string>[] sources)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var kv in source)
                merged[kv.Key] = kv.Value;
        }

        return merged;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionV22Profiles()
    {
        var symbol = TradingSymbol.BNBUSDT;
        var halfLock = CreateV22HalfLockOverrides();
        var inflation = CreateV22InflationFilterOverrides();
        var stopRisk = CreateV22StopRiskFilterOverrides();
        var failedBreakout = CreateV22FailedBreakoutFilterOverrides();
        var combined = CreateV22CombinedFilterOverrides();

        return
        [
            CreateRangeExpansionV22Profile("baseline-halflock", symbol, halfLock),
            CreateRangeExpansionV22Profile("inflation-filter-halflock", symbol, MergeOverrides(halfLock, inflation)),
            CreateRangeExpansionV22Profile("stop-risk-filter-halflock", symbol, MergeOverrides(halfLock, stopRisk)),
            CreateRangeExpansionV22Profile("failed-breakout-filter-halflock", symbol, MergeOverrides(halfLock, failedBreakout)),
            CreateRangeExpansionV22Profile("combined-filter-halflock", symbol, MergeOverrides(halfLock, combined)),
            CreateRangeExpansionV22Profile("timestop-fee-breakeven-0.10-halflock", symbol, MergeOverrides(halfLock, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "true",
                ["Backtest:ExitPolicy:BreakevenActivationNetProfitPercent"] = "0.10"
            })),
            CreateRangeExpansionV22Profile("timestop-fee-aware-halflock", symbol, MergeOverrides(halfLock, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Backtest:ExitPolicy:EnableFeeAwareTimeStopExit"] = "true"
            }))
        ];
    }

    private static ReplayProfileDefinition CreateRangeExpansionV22Profile(
        string variantLabel,
        TradingSymbol symbol,
        IReadOnlyDictionary<string, string>? extraOverrides)
        => CreateRangeExpansionV2Profile(
            $"range-expansion-v22-bnb-{variantLabel}-1m-profitlock-90",
            [symbol],
            "1m",
            "ProfitLock90",
            "90",
            extraOverrides);

    private static Dictionary<string, string> CreateV22HalfLockOverrides()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"] = "true"
        };

    private static Dictionary<string, string> CreateV22InflationFilterOverrides()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableInflationFilter"] = "true"
        };

    private static Dictionary<string, string> CreateV22StopRiskFilterOverrides()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableStopRiskFilter"] = "true"
        };

    private static Dictionary<string, string> CreateV22FailedBreakoutFilterOverrides()
        => CreateV23FailedBreakoutFilterOverrides();

    private static Dictionary<string, string> CreateV23FailedBreakoutFilterOverrides(
        decimal? maxGivebackAtEntryPercent = null,
        decimal? minFollowThroughCloseStrengthPercent = null,
        decimal? minBreakoutBodyStrengthPercent = null)
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableFailedBreakoutFilter"] = "true"
        };

        if (maxGivebackAtEntryPercent.HasValue)
            overrides["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MaxGivebackAtEntryPercent"] =
                maxGivebackAtEntryPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (minFollowThroughCloseStrengthPercent.HasValue)
            overrides["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:MinFollowThroughCloseStrengthPercent"] =
                minFollowThroughCloseStrengthPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (minBreakoutBodyStrengthPercent.HasValue)
            overrides["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:FailedBreakoutMinBreakoutBodyStrengthPercent"] =
                minBreakoutBodyStrengthPercent.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return overrides;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionV23Profiles()
    {
        var symbol = TradingSymbol.BNBUSDT;
        var halfLock = CreateV22HalfLockOverrides();
        const decimal refGiveback = 35m;
        const decimal refFollow = 55m;

        var profiles = new List<ReplayProfileDefinition>
        {
            CreateRangeExpansionV23Profile("baseline-halflock", symbol, halfLock),
            CreateRangeExpansionV23Profile("failed-breakout-ref-halflock", symbol,
                MergeOverrides(halfLock, CreateV23FailedBreakoutFilterOverrides(refGiveback, refFollow)))
        };

        foreach (var giveback in new[] { 25m, 30m, 35m, 40m, 45m })
        {
            profiles.Add(CreateRangeExpansionV23Profile(
                $"sweep-giveback-{giveback:0}-halflock",
                symbol,
                MergeOverrides(halfLock, CreateV23FailedBreakoutFilterOverrides(giveback, refFollow))));
        }

        foreach (var follow in new[] { 45m, 50m, 55m, 60m, 65m })
        {
            profiles.Add(CreateRangeExpansionV23Profile(
                $"sweep-follow-{follow:0}-halflock",
                symbol,
                MergeOverrides(halfLock, CreateV23FailedBreakoutFilterOverrides(refGiveback, follow))));
        }

        foreach (var body in new[] { 75m, 80m, 84m, 88m, 90m })
        {
            profiles.Add(CreateRangeExpansionV23Profile(
                $"sweep-body-{body:0}-halflock",
                symbol,
                MergeOverrides(halfLock, CreateV23FailedBreakoutFilterOverrides(refGiveback, refFollow, body))));
        }

        var combined = new (string Label, decimal? Giveback, decimal? Follow, decimal? Body)[]
        {
            ("combo-giveback30-follow60-halflock", 30m, 60m, null),
            ("combo-giveback40-follow50-halflock", 40m, 50m, null),
            ("combo-giveback30-body84-halflock", 30m, refFollow, 84m),
            ("combo-giveback35-follow60-body84-halflock", refGiveback, 60m, 84m),
            ("combo-giveback30-follow55-body88-halflock", 30m, refFollow, 88m)
        };

        foreach (var (label, giveback, follow, body) in combined)
        {
            profiles.Add(CreateRangeExpansionV23Profile(
                label,
                symbol,
                MergeOverrides(halfLock, CreateV23FailedBreakoutFilterOverrides(giveback, follow, body))));
        }

        return profiles;
    }

    private static ReplayProfileDefinition CreateRangeExpansionV23Profile(
        string variantLabel,
        TradingSymbol symbol,
        IReadOnlyDictionary<string, string>? extraOverrides)
        => CreateRangeExpansionV2Profile(
            $"range-expansion-v23-bnb-{variantLabel}-1m-profitlock-90",
            [symbol],
            "1m",
            "ProfitLock90",
            "90",
            extraOverrides);

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionV24Profiles()
    {
        var symbol = TradingSymbol.BNBUSDT;
        var body80 = CreateV24Body80EntryOverrides();
        var halfLock = CreateV22HalfLockOverrides();

        var profiles = new List<ReplayProfileDefinition>
        {
            CreateRangeExpansionV24Profile("v24-body80-halflock-current", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, halfLock)),
            CreateRangeExpansionV24Profile("v24-body80-profitlock80", symbol, "ProfitLock80", "80",
                MergeOverrides(body80, halfLock)),
            CreateRangeExpansionV24Profile("v24-body80-profitlock85", symbol, "ProfitLock85", "85",
                MergeOverrides(body80, halfLock)),
            CreateRangeExpansionV24Profile("v24-body80-costcover-breakeven", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, CreateV24CostCoverOverrides(0m))),
            CreateRangeExpansionV24Profile("v24-body80-halflock-costcover", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, CreateV24CostCoverOverrides(0.02m))),
            CreateRangeExpansionV24Profile("v24-body80-no-progress-exit-20m", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, halfLock, CreateV24NoProgressOverrides(20))),
            CreateRangeExpansionV24Profile("v24-body80-no-progress-exit-30m", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, halfLock, CreateV24NoProgressOverrides(30))),
            CreateRangeExpansionV24Profile("v24-body80-timestop-30", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, halfLock, CreateV24TimeStopOverrides(30))),
            CreateRangeExpansionV24Profile("v24-body80-timestop-45", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, halfLock, CreateV24TimeStopOverrides(45))),
            CreateRangeExpansionV24Profile("v24-body80-combo-halflock-costcover-no-progress-30m", symbol, "ProfitLock90", "90",
                MergeOverrides(body80, CreateV24CostCoverOverrides(0.02m), CreateV24NoProgressOverrides(30))),
            CreateRangeExpansionV24Profile("v24-body80-combo-profitlock85-costcover", symbol, "ProfitLock85", "85",
                MergeOverrides(body80, CreateV24CostCoverOverrides(0m)))
        };

        return profiles;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRangeExpansionV2FeasibilityProfiles(bool includeComparison = false)
    {
        var profiles = new List<ReplayProfileDefinition>();
        AddFeasibilitySymbolProfiles(profiles, TradingSymbol.BNBUSDT, "bnb");

        if (includeComparison)
        {
            AddFeasibilitySymbolProfiles(profiles, TradingSymbol.ETHUSDT, "eth");
            AddFeasibilitySymbolProfiles(profiles, TradingSymbol.SOLUSDT, "sol");
        }

        return profiles;
    }

    private static void AddFeasibilitySymbolProfiles(
        List<ReplayProfileDefinition> profiles,
        TradingSymbol symbol,
        string symbolLabel)
    {
        var body80 = CreateV24Body80EntryOverrides();
        var halfLock = CreateV22HalfLockOverrides();
        var failedBreakoutRef = CreateV23FailedBreakoutFilterOverrides(35m, 55m);

        profiles.Add(CreateRangeExpansionV2FeasibilityProfile(
            $"{symbolLabel}-body80-halflock-current", symbol, "ProfitLock90", "90",
            MergeOverrides(body80, halfLock)));
        profiles.Add(CreateRangeExpansionV2FeasibilityProfile(
            $"{symbolLabel}-body80-halflock-costcover", symbol, "ProfitLock90", "90",
            MergeOverrides(body80, CreateV24CostCoverOverrides(0.02m))));
        profiles.Add(CreateRangeExpansionV2FeasibilityProfile(
            $"{symbolLabel}-failed-breakout-ref-halflock", symbol, "ProfitLock90", "90",
            MergeOverrides(halfLock, failedBreakoutRef)));
    }

    private static ReplayProfileDefinition CreateRangeExpansionV2FeasibilityProfile(
        string variantLabel,
        TradingSymbol symbol,
        string policyName,
        string policyValue,
        IReadOnlyDictionary<string, string>? extraOverrides)
        => CreateRangeExpansionV2Profile(
            $"range-expansion-v2-feasibility-{variantLabel}-1m-{policyName.ToLowerInvariant()}",
            [symbol],
            "1m",
            policyName,
            policyValue,
            extraOverrides);

    private static Dictionary<string, string> CreateV24Body80EntryOverrides()
        => CreateV23FailedBreakoutFilterOverrides(35m, 55m, 80m);

    private static Dictionary<string, string> CreateV24CostCoverOverrides(decimal minNetPercent)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:ExitPolicy:EnableCostCoveredBreakevenExit"] = "true",
            ["Backtest:ExitPolicy:CostCoverMinNetPercent"] = minNetPercent.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    private static Dictionary<string, string> CreateV24NoProgressOverrides(int minutes)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:ExitPolicy:NoProgressExitMinutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    private static Dictionary<string, string> CreateV24TimeStopOverrides(int minutes)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:ExitPolicy:MaxHoldMinutes"] = minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

    private static ReplayProfileDefinition CreateRangeExpansionV24Profile(
        string variantLabel,
        TradingSymbol symbol,
        string policyName,
        string policyValue,
        IReadOnlyDictionary<string, string>? extraOverrides)
        => CreateRangeExpansionV2Profile(
            $"range-expansion-v24-bnb-{variantLabel}-1m-{policyName.ToLowerInvariant()}",
            [symbol],
            "1m",
            policyName,
            policyValue,
            extraOverrides);

    private static Dictionary<string, string> CreateV22CombinedFilterOverrides()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableInflationFilter"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableStopRiskFilter"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableBreakoutQualityFilter"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:SeparatorFilters:EnableFailedBreakoutFilter"] = "true"
        };

    private static ReplayProfileDefinition CreateRangeExpansionV21Profile(
        string variantLabel,
        TradingSymbol symbol,
        IReadOnlyDictionary<string, string>? extraOverrides)
        => CreateRangeExpansionV2Profile(
            $"range-expansion-v21-bnb-{variantLabel}-1m-profitlock-90",
            [symbol],
            "1m",
            "ProfitLock90",
            "90",
            extraOverrides);

    private static Dictionary<string, string> CreateV21TighterEntryOverrides()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinBreakoutCloseAboveRangePercent"] = "0.08",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinBreakoutBodyStrengthPercent"] = "60",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MaxBreakoutCandleRangeToAtrRatio"] = "1.40",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinVolumeExpansionRatio"] = "1.25",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinAtrExpansionRatio"] = "1.08",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MaxStructuralStopToLock90Ratio"] = "3.00",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MinRangeWidthPercent"] = "0.20",
            ["Backtest:RangeExpansionBreakoutV2:ExperimentalFilters:MaxRangeWidthPercent"] = "0.52"
        };

    private static ReplayProfileDefinition CreateRangeExpansionV2Profile(
        string profileName,
        IReadOnlyList<TradingSymbol> symbols,
        string interval,
        string policyName,
        string policyValue,
        IReadOnlyDictionary<string, string>? extraOverrides)
    {
        var overrides = CreateRangeExpansionV2BaseOverrides(interval, policyName, policyValue);
        if (extraOverrides is not null)
        {
            foreach (var kv in extraOverrides)
                overrides[kv.Key] = kv.Value;
        }

        return new ReplayProfileDefinition(profileName, symbols, overrides);
    }

    public static string ResolveRangeExpansionV2ProfileInterval(ReplayProfileDefinition profile)
    {
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            if (profile.ProfileName.Contains($"-{interval}-", StringComparison.OrdinalIgnoreCase))
                return interval;
        }

        return profile.ConfigOverrides.TryGetValue("Backtest:RangeExpansionBreakoutV2:ProfileInterval", out var configured)
            ? configured
            : "1m";
    }

    public static RangeExpansionV2ReplayContext BuildRangeExpansionV2ReplayContext(
        BacktestSettings settings,
        ReplayProfileDefinition profile)
    {
        var app = new BacktestApplication(settings);
        var configuration = app.BuildConfiguration(profile.ConfigOverrides);
        var executionCostSettings = new ExecutionCostSettings(
            settings.FeeRatePercent,
            settings.EstimatedSpreadPercent,
            settings.SlippagePercent);
        var exitPolicy = ReadExitPolicySettings(configuration);
        return new RangeExpansionV2ReplayContext(
            new RangeExpansionBreakoutV2Model(configuration),
            new ExecutionSimulator(executionCostSettings, exitPolicy),
            new ProfileSignalStats(),
            exitPolicy,
            executionCostSettings,
            configuration);
    }

    private static Dictionary<string, string> CreateRangeExpansionV2BaseOverrides(
        string interval,
        string policyName,
        string policyValue)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:Enabled"] = "false",
            ["Backtest:RangeExpansionBreakoutV2:Enabled"] = "true",
            ["Backtest:RangeExpansionBreakoutV2:ProfileInterval"] = interval,
            ["Backtest:RangeExpansionBreakoutV2:RequiredNetProfitPercent"] = "0.10",
            ["Backtest:RangeExpansionBreakoutV2:TargetModelName"] = "HybridMaxReasonableTarget",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "false",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "false",
            ["Backtest:ExitPolicy:Name"] = policyName,
            ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = policyValue,
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableStructuralStop"] = "true",
            ["Backtest:ExitPolicy:StructuralStopMode"] = "RangeLow",
            ["Backtest:ExitPolicy:MaxHoldMinutes"] = "60"
        };
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildImpulseContinuationV1Profiles(bool includeResearchVariants = false)
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var (symbolLabel, symbol) in new (string, TradingSymbol)[]
        {
            ("ETH", TradingSymbol.ETHUSDT),
            ("BNB", TradingSymbol.BNBUSDT),
            ("SOL", TradingSymbol.SOLUSDT)
        })
        {
            foreach (var interval in new[] { "1m", "3m", "5m" })
            {
                profiles.Add(CreateImpulseContinuationV1Profile(
                    $"impulse-continuation-v1-{symbolLabel}-{interval}-lock90",
                    [symbol],
                    interval,
                    "ProfitLock90",
                    "90",
                    requiredNetProfitPercent: "0.10",
                    maxHoldMinutes: "60",
                    targetModelName: "HybridReasonableTarget",
                    structuralStopMode: "RangeLow",
                    extraOverrides: null));
            }
        }

        if (!includeResearchVariants)
            return profiles;

        foreach (var researchInterval in new[] { "5m", "1m" })
        {
            foreach (var (symbolLabel, symbol) in new (string, TradingSymbol)[]
            {
                ("ETH", TradingSymbol.ETHUSDT),
                ("BNB", TradingSymbol.BNBUSDT),
                ("SOL", TradingSymbol.SOLUSDT)
            })
            {
                foreach (var net in new[] { "0.15", "0.20" })
                {
                    var netLabel = net == "0.15" ? "net15" : "net20";
                    profiles.Add(CreateImpulseContinuationV1Profile(
                        $"impulse-continuation-v1-{symbolLabel}-{researchInterval}-{netLabel}-lock90",
                        [symbol],
                        researchInterval,
                        "ProfitLock90",
                        "90",
                        requiredNetProfitPercent: net,
                        maxHoldMinutes: "60",
                        targetModelName: "HybridReasonableTarget",
                        structuralStopMode: "RangeLow",
                        extraOverrides: null));
                }

                foreach (var hold in new[] { ("30", "hold30"), ("120", "hold120") })
                {
                    profiles.Add(CreateImpulseContinuationV1Profile(
                        $"impulse-continuation-v1-{symbolLabel}-{researchInterval}-{hold.Item2}-lock90",
                        [symbol],
                        researchInterval,
                        "ProfitLock90",
                        "90",
                        requiredNetProfitPercent: "0.10",
                        maxHoldMinutes: hold.Item1,
                        targetModelName: "HybridReasonableTarget",
                        structuralStopMode: "RangeLow",
                        extraOverrides: null));
                }

                foreach (var target in new[]
                {
                    "ImpulseMeasuredMoveTarget",
                    "AtrExpansionTarget",
                    "RecentSwingTarget"
                })
                {
                    var targetLabel = target switch
                    {
                        "ImpulseMeasuredMoveTarget" => "impulse-move",
                        "AtrExpansionTarget" => "atr-expand",
                        _ => "swing-target"
                    };
                    profiles.Add(CreateImpulseContinuationV1Profile(
                        $"impulse-continuation-v1-{symbolLabel}-{researchInterval}-{targetLabel}-lock90",
                        [symbol],
                        researchInterval,
                        "ProfitLock90",
                        "90",
                        requiredNetProfitPercent: "0.10",
                        maxHoldMinutes: "60",
                        targetModelName: target,
                        structuralStopMode: "RangeLow",
                        extraOverrides: null));
                }

                profiles.Add(CreateImpulseContinuationV1Profile(
                    $"impulse-continuation-v1-{symbolLabel}-{researchInterval}-midpoint-stop-lock90",
                    [symbol],
                    researchInterval,
                    "ProfitLock90",
                    "90",
                    requiredNetProfitPercent: "0.10",
                    maxHoldMinutes: "60",
                    targetModelName: "HybridReasonableTarget",
                    structuralStopMode: "RangeMidpoint",
                    extraOverrides: null));
            }
        }

        return profiles;
    }

    private static ReplayProfileDefinition CreateImpulseContinuationV1Profile(
        string profileName,
        IReadOnlyList<TradingSymbol> symbols,
        string interval,
        string policyName,
        string policyValue,
        string requiredNetProfitPercent,
        string maxHoldMinutes,
        string targetModelName,
        string structuralStopMode,
        IReadOnlyDictionary<string, string>? extraOverrides)
    {
        var overrides = CreateImpulseContinuationV1BaseOverrides(
            interval, policyName, policyValue, requiredNetProfitPercent, maxHoldMinutes, targetModelName, structuralStopMode);
        if (extraOverrides is not null)
        {
            foreach (var kv in extraOverrides)
                overrides[kv.Key] = kv.Value;
        }

        return new ReplayProfileDefinition(profileName, symbols, overrides);
    }

    public static string ResolveImpulseContinuationV1ProfileInterval(ReplayProfileDefinition profile)
    {
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            if (profile.ProfileName.Contains($"-{interval}-", StringComparison.OrdinalIgnoreCase))
                return interval;
        }

        return profile.ConfigOverrides.TryGetValue("Backtest:ImpulseContinuationV1:ProfileInterval", out var configured)
            ? configured
            : "1m";
    }

    public static ImpulseContinuationV1ReplayContext BuildImpulseContinuationV1ReplayContext(
        BacktestSettings settings,
        ReplayProfileDefinition profile)
    {
        var app = new BacktestApplication(settings);
        var configuration = app.BuildConfiguration(profile.ConfigOverrides);
        var executionCostSettings = new ExecutionCostSettings(
            settings.FeeRatePercent,
            settings.EstimatedSpreadPercent,
            settings.SlippagePercent);
        var exitPolicy = ReadExitPolicySettings(configuration);
        return new ImpulseContinuationV1ReplayContext(
            new ImpulseContinuationV1Model(configuration),
            new ExecutionSimulator(executionCostSettings, exitPolicy),
            new ProfileSignalStats(),
            exitPolicy,
            executionCostSettings,
            configuration);
    }

    private static Dictionary<string, string> CreateImpulseContinuationV1BaseOverrides(
        string interval,
        string policyName,
        string policyValue,
        string requiredNetProfitPercent,
        string maxHoldMinutes,
        string targetModelName,
        string structuralStopMode)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:Enabled"] = "false",
            ["Backtest:RangeExpansionBreakoutV2:Enabled"] = "false",
            ["Backtest:ImpulseContinuationV1:Enabled"] = "true",
            ["Backtest:ImpulseContinuationV1:ProfileInterval"] = interval,
            ["Backtest:ImpulseContinuationV1:RequiredNetProfitPercent"] = requiredNetProfitPercent,
            ["Backtest:ImpulseContinuationV1:TargetModelName"] = targetModelName,
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "false",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "false",
            ["Backtest:ExitPolicy:Name"] = policyName,
            ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = policyValue,
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"] = "false",
            ["Backtest:ExitPolicy:EnableStructuralStop"] = "true",
            ["Backtest:ExitPolicy:StructuralStopMode"] = structuralStopMode,
            ["Backtest:ExitPolicy:MaxHoldMinutes"] = maxHoldMinutes
        };
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildMeanReversionRangeBounceV1Profiles(bool includeResearchVariants = false)
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var (symbolLabel, symbol) in new (string, TradingSymbol)[]
        {
            ("ETH", TradingSymbol.ETHUSDT),
            ("BNB", TradingSymbol.BNBUSDT),
            ("SOL", TradingSymbol.SOLUSDT)
        })
        {
            foreach (var interval in new[] { "1m", "3m", "5m" })
            {
                profiles.Add(CreateMeanReversionRangeBounceV1Profile(
                    $"mean-reversion-range-v1-{symbolLabel}-{interval}-midpoint-target",
                    [symbol],
                    interval,
                    rangeLookbackCandles: "30",
                    requiredNetProfitPercent: "0.10",
                    maxHoldMinutes: "60",
                    targetModelName: "RangeMidpointTarget",
                    stopMode: "RangeLowBuffer",
                    profitLockThresholdPercent: "100",
                    exitPolicyName: "ProfitTarget",
                    extraOverrides: null));

                if (!includeResearchVariants)
                    continue;

                foreach (var lookback in new[] { ("20", "lookback20"), ("50", "lookback50") })
                {
                    profiles.Add(CreateMeanReversionRangeBounceV1Profile(
                        $"mean-reversion-range-v1-{symbolLabel}-{interval}-{lookback.Item2}-midpoint-target",
                        [symbol], interval, lookback.Item1, "0.10", "60", "RangeMidpointTarget", "RangeLowBuffer", "100", "ProfitTarget", null));
                }

                foreach (var net in new[] { ("0.15", "net15"), ("0.20", "net20") })
                {
                    profiles.Add(CreateMeanReversionRangeBounceV1Profile(
                        $"mean-reversion-range-v1-{symbolLabel}-{interval}-{net.Item2}-midpoint-target",
                        [symbol], interval, "30", net.Item1, "60", "RangeMidpointTarget", "RangeLowBuffer", "100", "ProfitTarget", null));
                }

                foreach (var hold in new[] { ("30", "hold30"), ("120", "hold120") })
                {
                    profiles.Add(CreateMeanReversionRangeBounceV1Profile(
                        $"mean-reversion-range-v1-{symbolLabel}-{interval}-{hold.Item2}-midpoint-target",
                        [symbol], interval, "30", "0.10", hold.Item1, "RangeMidpointTarget", "RangeLowBuffer", "100", "ProfitTarget", null));
                }

                foreach (var (target, label) in new[]
                {
                    ("RangeSixtyPercentTarget", "range60-target"),
                    ("RangeHighTarget", "rangehigh-target"),
                    ("AtrLimitedTarget", "atr-limited-target")
                })
                {
                    profiles.Add(CreateMeanReversionRangeBounceV1Profile(
                        $"mean-reversion-range-v1-{symbolLabel}-{interval}-{label}",
                        [symbol], interval, "30", "0.10", "60", target, "RangeLowBuffer", "100", "ProfitTarget", null));
                }

                profiles.Add(CreateMeanReversionRangeBounceV1Profile(
                    $"mean-reversion-range-v1-{symbolLabel}-{interval}-rejection-stop-midpoint-target",
                    [symbol], interval, "30", "0.10", "60", "RangeMidpointTarget", "RejectionCandleLow", "100", "ProfitTarget", null));

                profiles.Add(CreateMeanReversionRangeBounceV1Profile(
                    $"mean-reversion-range-v1-{symbolLabel}-{interval}-lock90-midpoint-target",
                    [symbol], interval, "30", "0.10", "60", "RangeMidpointTarget", "RangeLowBuffer", "90", "ProfitLock90", null));
            }
        }

        return profiles;
    }

    private static ReplayProfileDefinition CreateMeanReversionRangeBounceV1Profile(
        string profileName,
        IReadOnlyList<TradingSymbol> symbols,
        string interval,
        string rangeLookbackCandles,
        string requiredNetProfitPercent,
        string maxHoldMinutes,
        string targetModelName,
        string stopMode,
        string profitLockThresholdPercent,
        string exitPolicyName,
        IReadOnlyDictionary<string, string>? extraOverrides)
    {
        var overrides = CreateMeanReversionRangeBounceV1BaseOverrides(
            interval, rangeLookbackCandles, requiredNetProfitPercent, maxHoldMinutes,
            targetModelName, stopMode, profitLockThresholdPercent, exitPolicyName);
        if (extraOverrides is not null)
        {
            foreach (var kv in extraOverrides)
                overrides[kv.Key] = kv.Value;
        }

        return new ReplayProfileDefinition(profileName, symbols, overrides);
    }

    public static string ResolveMeanReversionRangeBounceV1ProfileInterval(ReplayProfileDefinition profile)
    {
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            if (profile.ProfileName.Contains($"-{interval}-", StringComparison.OrdinalIgnoreCase))
                return interval;
        }

        return profile.ConfigOverrides.TryGetValue("Backtest:MeanReversionRangeBounceV1:ProfileInterval", out var configured)
            ? configured
            : "1m";
    }

    public static MeanReversionRangeBounceV1ReplayContext BuildMeanReversionRangeBounceV1ReplayContext(
        BacktestSettings settings,
        ReplayProfileDefinition profile)
    {
        var app = new BacktestApplication(settings);
        var configuration = app.BuildConfiguration(profile.ConfigOverrides);
        var executionCostSettings = new ExecutionCostSettings(
            settings.FeeRatePercent,
            settings.EstimatedSpreadPercent,
            settings.SlippagePercent);
        var exitPolicy = ReadExitPolicySettings(configuration);
        return new MeanReversionRangeBounceV1ReplayContext(
            new MeanReversionRangeBounceV1Model(configuration),
            new ExecutionSimulator(executionCostSettings, exitPolicy),
            new ProfileSignalStats(),
            exitPolicy,
            executionCostSettings,
            configuration);
    }

    private static Dictionary<string, string> CreateMeanReversionRangeBounceV1BaseOverrides(
        string interval,
        string rangeLookbackCandles,
        string requiredNetProfitPercent,
        string maxHoldMinutes,
        string targetModelName,
        string stopMode,
        string profitLockThresholdPercent,
        string exitPolicyName)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:Enabled"] = "false",
            ["Backtest:RangeExpansionBreakoutV2:Enabled"] = "false",
            ["Backtest:ImpulseContinuationV1:Enabled"] = "false",
            ["Backtest:MeanReversionRangeBounceV1:Enabled"] = "true",
            ["Backtest:MeanReversionRangeBounceV1:ProfileInterval"] = interval,
            ["Backtest:MeanReversionRangeBounceV1:RangeLookbackCandles"] = rangeLookbackCandles,
            ["Backtest:MeanReversionRangeBounceV1:RequiredNetProfitPercent"] = requiredNetProfitPercent,
            ["Backtest:MeanReversionRangeBounceV1:TargetModelName"] = targetModelName,
            ["Backtest:MeanReversionRangeBounceV1:StopMode"] = stopMode,
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "false",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "false",
            ["Backtest:ExitPolicy:Name"] = exitPolicyName,
            ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = profitLockThresholdPercent,
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"] = "false",
            ["Backtest:ExitPolicy:EnableStructuralStop"] = "true",
            ["Backtest:ExitPolicy:StructuralStopMode"] = "RangeLow",
            ["Backtest:ExitPolicy:MaxHoldMinutes"] = maxHoldMinutes
        };
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildHigherTimeframeMomentumPullbackV1Profiles(bool includeResearchVariants = false)
    {
        var profiles = new List<ReplayProfileDefinition>();
        foreach (var (symbolLabel, symbol) in new (string, TradingSymbol)[]
        {
            ("ETH", TradingSymbol.ETHUSDT),
            ("BNB", TradingSymbol.BNBUSDT),
            ("SOL", TradingSymbol.SOLUSDT)
        })
        {
            foreach (var interval in new[] { "15m", "30m" })
            {
                profiles.Add(CreateHigherTimeframeMomentumPullbackV1Profile(
                    $"htf-momentum-v1-{symbolLabel}-{interval}-hybrid-target",
                    [symbol],
                    interval,
                    requiredNetProfitPercent: "0.50",
                    maxHoldMinutes: "480",
                    targetModelName: "HybridMinReasonableTarget",
                    stopMode: "PullbackLow",
                    profitLockThresholdPercent: "100",
                    exitPolicyName: "ProfitTarget",
                    extraOverrides: null));

                if (!includeResearchVariants)
                    continue;

                foreach (var net in new[] { ("0.30", "net30"), ("0.75", "net75") })
                {
                    profiles.Add(CreateHigherTimeframeMomentumPullbackV1Profile(
                        $"htf-momentum-v1-{symbolLabel}-{interval}-{net.Item2}-hybrid-target",
                        [symbol], interval, net.Item1, "480", "HybridMinReasonableTarget", "PullbackLow", "100", "ProfitTarget", null));
                }

                foreach (var (target, label) in new[]
                {
                    ("RecentSwingHighTarget", "swing-target"),
                    ("AtrMultipleTarget", "atr-target"),
                    ("TrendContinuationMeasuredMoveTarget", "trend-move-target")
                })
                {
                    profiles.Add(CreateHigherTimeframeMomentumPullbackV1Profile(
                        $"htf-momentum-v1-{symbolLabel}-{interval}-{label}",
                        [symbol], interval, "0.50", "480", target, "PullbackLow", "100", "ProfitTarget", null));
                }

                foreach (var hold in new[] { ("240", "hold4h"), ("720", "hold12h") })
                {
                    profiles.Add(CreateHigherTimeframeMomentumPullbackV1Profile(
                        $"htf-momentum-v1-{symbolLabel}-{interval}-{hold.Item2}-hybrid-target",
                        [symbol], interval, "0.50", hold.Item1, "HybridMinReasonableTarget", "PullbackLow", "100", "ProfitTarget", null));
                }

                profiles.Add(CreateHigherTimeframeMomentumPullbackV1Profile(
                    $"htf-momentum-v1-{symbolLabel}-{interval}-lock90-hybrid-target",
                    [symbol], interval, "0.50", "480", "HybridMinReasonableTarget", "PullbackLow", "90", "ProfitLock90", null));
            }
        }

        return profiles;
    }

    private static ReplayProfileDefinition CreateHigherTimeframeMomentumPullbackV1Profile(
        string profileName,
        IReadOnlyList<TradingSymbol> symbols,
        string interval,
        string requiredNetProfitPercent,
        string maxHoldMinutes,
        string targetModelName,
        string stopMode,
        string profitLockThresholdPercent,
        string exitPolicyName,
        IReadOnlyDictionary<string, string>? extraOverrides)
    {
        var overrides = CreateHigherTimeframeMomentumPullbackV1BaseOverrides(
            interval, requiredNetProfitPercent, maxHoldMinutes,
            targetModelName, stopMode, profitLockThresholdPercent, exitPolicyName);
        if (extraOverrides is not null)
        {
            foreach (var kv in extraOverrides)
                overrides[kv.Key] = kv.Value;
        }

        return new ReplayProfileDefinition(profileName, symbols, overrides);
    }

    public static string ResolveHigherTimeframeMomentumPullbackV1ProfileInterval(ReplayProfileDefinition profile)
    {
        foreach (var interval in new[] { "15m", "30m" })
        {
            if (profile.ProfileName.Contains($"-{interval}-", StringComparison.OrdinalIgnoreCase))
                return interval;
        }

        return profile.ConfigOverrides.TryGetValue("Backtest:HigherTimeframeMomentumPullbackV1:ProfileInterval", out var configured)
            ? configured
            : "15m";
    }

    public static HigherTimeframeMomentumPullbackV1ReplayContext BuildHigherTimeframeMomentumPullbackV1ReplayContext(
        BacktestSettings settings,
        ReplayProfileDefinition profile)
    {
        var app = new BacktestApplication(settings);
        var configuration = app.BuildConfiguration(profile.ConfigOverrides);
        var executionCostSettings = new ExecutionCostSettings(
            settings.FeeRatePercent,
            settings.EstimatedSpreadPercent,
            settings.SlippagePercent);
        var exitPolicy = ReadExitPolicySettings(configuration);
        return new HigherTimeframeMomentumPullbackV1ReplayContext(
            new HigherTimeframeMomentumPullbackV1Model(configuration),
            new ExecutionSimulator(executionCostSettings, exitPolicy),
            new ProfileSignalStats(),
            exitPolicy,
            executionCostSettings,
            configuration);
    }

    private static Dictionary<string, string> CreateHigherTimeframeMomentumPullbackV1BaseOverrides(
        string interval,
        string requiredNetProfitPercent,
        string maxHoldMinutes,
        string targetModelName,
        string stopMode,
        string profitLockThresholdPercent,
        string exitPolicyName)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:Enabled"] = "false",
            ["Backtest:RangeExpansionBreakoutV2:Enabled"] = "false",
            ["Backtest:ImpulseContinuationV1:Enabled"] = "false",
            ["Backtest:MeanReversionRangeBounceV1:Enabled"] = "false",
            ["Backtest:HigherTimeframeMomentumPullbackV1:Enabled"] = "true",
            ["Backtest:HigherTimeframeMomentumPullbackV1:ProfileInterval"] = interval,
            ["Backtest:HigherTimeframeMomentumPullbackV1:RequiredNetProfitPercent"] = requiredNetProfitPercent,
            ["Backtest:HigherTimeframeMomentumPullbackV1:TargetModelName"] = targetModelName,
            ["Backtest:HigherTimeframeMomentumPullbackV1:StopMode"] = stopMode,
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "false",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "false",
            ["Backtest:ExitPolicy:Name"] = exitPolicyName,
            ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = profitLockThresholdPercent,
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"] = "false",
            ["Backtest:ExitPolicy:EnableStructuralStop"] = "true",
            ["Backtest:ExitPolicy:StructuralStopMode"] = "RangeLow",
            ["Backtest:ExitPolicy:MaxHoldMinutes"] = maxHoldMinutes
        };
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRegimeGatedLongEdgeV1Profiles(bool includeResearchVariants = false)
    {
        var profiles = new List<ReplayProfileDefinition>();
        var targetStopMatrix = new (decimal Target, decimal Stop, string Label)[]
        {
            (0.50m, 0.50m, "t050-s050"),
            (0.75m, 0.50m, "t075-s050"),
            (1.00m, 0.75m, "t100-s075")
        };

        void AddProfiles(
            TradingSymbol symbol,
            string symbolLabel,
            string interval,
            RegimeGatedLongEdgeV1GateName gate,
            decimal target,
            decimal stop,
            int maxHoldMinutes,
            RegimeGatedLongEdgeV1EntryConfirmationMode confirmation,
            string? profileSuffix = null)
        {
            if (gate == RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline
                && (symbol != TradingSymbol.BNBUSDT || !string.Equals(interval, "30m", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            var gateSlug = gate switch
            {
                RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnGate => "elevated-vol-market-return",
                RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate => "wide-range-near-low",
                RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnWithBtcFavorableGate => "elevated-vol-market-return-btc-favorable",
                RegimeGatedLongEdgeV1GateName.WideRangeNearLowWithBtcFavorableGate => "wide-range-near-low-btc-favorable",
                _ => "bnb30m-baseline"
            };
            var confirmSlug = confirmation.ToString().ToLowerInvariant();
            var holdHours = maxHoldMinutes / 60;
            var suffix = profileSuffix ?? $"{target:F2}-{stop:F2}-hold{holdHours}h-{confirmSlug}";
            profiles.Add(CreateRegimeGatedLongEdgeV1Profile(
                $"regime-gated-v1-{gateSlug}-{symbolLabel}-{interval}-{suffix}",
                [symbol],
                interval,
                gate,
                target,
                stop,
                maxHoldMinutes.ToString(),
                confirmation));
        }

        foreach (var (target, stop, _) in targetStopMatrix)
        {
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnGate, target, stop, 480, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose);
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate, target, stop, 480, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose);
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline, target, stop, 480, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose);
        }

        if (!includeResearchVariants)
            return profiles;

        foreach (var hold in new[] { (240, "hold4h"), (720, "hold12h") })
        {
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnGate, 0.50m, 0.50m, hold.Item1, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose, $"t050-s050-{hold.Item2}-nextclose");
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate, 0.50m, 0.50m, hold.Item1, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose, $"t050-s050-{hold.Item2}-nextclose");
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline, 0.50m, 0.50m, hold.Item1, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose, $"t050-s050-{hold.Item2}-nextclose");
        }

        foreach (var confirmation in new[]
        {
            RegimeGatedLongEdgeV1EntryConfirmationMode.NextOpen,
            RegimeGatedLongEdgeV1EntryConfirmationMode.CloseAbovePrevHigh,
            RegimeGatedLongEdgeV1EntryConfirmationMode.CloseAboveShortMa,
            RegimeGatedLongEdgeV1EntryConfirmationMode.BullishNearLow
        })
        {
            var confirmSlug = confirmation.ToString().ToLowerInvariant();
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnGate, 0.50m, 0.50m, 480, confirmation, $"t050-s050-hold8h-{confirmSlug}");
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate, 0.50m, 0.50m, 480, confirmation, $"t050-s050-hold8h-{confirmSlug}");
            AddProfiles(TradingSymbol.BNBUSDT, "BNB", "30m", RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline, 0.50m, 0.50m, 480, confirmation, $"t050-s050-hold8h-{confirmSlug}");
        }

        foreach (var (symbol, label) in new (TradingSymbol, string)[]
        {
            (TradingSymbol.SOLUSDT, "SOL"),
            (TradingSymbol.ETHUSDT, "ETH")
        })
        {
            foreach (var interval in symbol == TradingSymbol.SOLUSDT ? new[] { "15m", "30m" } : new[] { "30m" })
            {
                AddProfiles(symbol, label, interval, RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnGate, 0.50m, 0.50m, 480, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose, "t050-s050-hold8h-nextclose");
                AddProfiles(symbol, label, interval, RegimeGatedLongEdgeV1GateName.WideRangeNearLowGate, 0.50m, 0.50m, 480, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose, "t050-s050-hold8h-nextclose");
                if (symbol == TradingSymbol.ETHUSDT)
                    AddProfiles(symbol, label, interval, RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline, 0.50m, 0.50m, 480, RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose, "t050-s050-hold8h-nextclose");
            }
        }

        return profiles;
    }

    public static IReadOnlyList<ReplayProfileDefinition> BuildRegimeGatedLongEdgeV1BtcContextProfiles()
    {
        var profiles = new List<ReplayProfileDefinition>();
        var targetStopMatrix = new (decimal Target, decimal Stop)[]
        {
            (0.50m, 0.50m),
            (0.75m, 0.50m),
            (1.00m, 0.75m)
        };
        var holds = new[] { (240, "hold4h"), (480, "hold8h"), (720, "hold12h") };
        foreach (var gate in new[]
        {
            RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnWithBtcFavorableGate,
            RegimeGatedLongEdgeV1GateName.WideRangeNearLowWithBtcFavorableGate
        })
        {
            var gateSlug = gate == RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnWithBtcFavorableGate
                ? "elevated-vol-market-return-btc-favorable"
                : "wide-range-near-low-btc-favorable";
            foreach (var (target, stop) in targetStopMatrix)
            {
                foreach (var (holdMinutes, holdLabel) in holds)
                {
                    var suffix = $"{target:F2}-{stop:F2}-{holdLabel}-nextclose";
                    profiles.Add(CreateRegimeGatedLongEdgeV1Profile(
                        $"regime-gated-v1-btc-{gateSlug}-BNB-30m-{suffix}",
                        [TradingSymbol.BNBUSDT],
                        "30m",
                        gate,
                        target,
                        stop,
                        holdMinutes.ToString(),
                        RegimeGatedLongEdgeV1EntryConfirmationMode.NextClose));
                }
            }
        }

        return profiles;
    }

    private static ReplayProfileDefinition CreateRegimeGatedLongEdgeV1Profile(
        string profileName,
        IReadOnlyList<TradingSymbol> symbols,
        string interval,
        RegimeGatedLongEdgeV1GateName gate,
        decimal targetPercent,
        decimal stopPercent,
        string maxHoldMinutes,
        RegimeGatedLongEdgeV1EntryConfirmationMode confirmation)
    {
        var overrides = CreateRegimeGatedLongEdgeV1BaseOverrides(interval, gate, targetPercent, stopPercent, maxHoldMinutes, confirmation);
        return new ReplayProfileDefinition(profileName, symbols, overrides);
    }

    public static string ResolveRegimeGatedLongEdgeV1ProfileInterval(ReplayProfileDefinition profile)
    {
        foreach (var interval in new[] { "15m", "30m" })
        {
            if (profile.ProfileName.Contains($"-{interval}-", StringComparison.OrdinalIgnoreCase))
                return interval;
        }

        return profile.ConfigOverrides.TryGetValue("Backtest:RegimeGatedLongEdgeV1:ProfileInterval", out var configured)
            ? configured
            : "30m";
    }

    public static string ResolveRegimeGatedLongEdgeV1RuleName(ReplayProfileDefinition profile)
    {
        if (profile.ConfigOverrides.TryGetValue("Backtest:RegimeGatedLongEdgeV1:RegimeGateName", out var gate))
            return gate;
        return RegimeGatedLongEdgeV1GateName.Bnb30mUnconditionalPositiveBaseline.ToString();
    }

    public static IReadOnlyDictionary<string, string> ResolveRegimeGatedLongEdgeV1GateThresholds(ReplayProfileDefinition? profile = null)
    {
        var thresholds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ElevatedVolMarketReturnGate.VolatilityRegime"] = "Elevated",
            ["ElevatedVolMarketReturnGate.MarketWideReturnProxyNote"] = "Study rule used all Elevated-vol observations; min/max proxy unset unless configured.",
            ["WideRangeNearLowGate.RangeWidthMinPercent"] = "0.8608",
            ["WideRangeNearLowGate.RangeWidthMaxPercent"] = "10.5309",
            ["WideRangeNearLowGate.DistanceFromRecentLowMinPercent"] = "0",
            ["WideRangeNearLowGate.DistanceFromRecentLowMaxPercent"] = "0.1671"
        };

        if (profile is null)
            return thresholds;

        var keys = new[]
        {
            "Backtest:RegimeGatedLongEdgeV1:RangeWidthMinPercent",
            "Backtest:RegimeGatedLongEdgeV1:RangeWidthMaxPercent",
            "Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMinPercent",
            "Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMaxPercent",
            "Backtest:RegimeGatedLongEdgeV1:MinMarketWideReturnProxyPercent",
            "Backtest:RegimeGatedLongEdgeV1:MaxMarketWideReturnProxyPercent"
        };
        foreach (var key in keys)
        {
            if (profile.ConfigOverrides.TryGetValue(key, out var value))
                thresholds[key] = value;
        }

        return thresholds;
    }

    public static RegimeGatedLongEdgeV1ReplayContext BuildRegimeGatedLongEdgeV1ReplayContext(
        BacktestSettings settings,
        ReplayProfileDefinition profile,
        BtcContextIndex? btcContext,
        MarketWideContextIndex? marketWideContext)
    {
        var app = new BacktestApplication(settings);
        var configuration = app.BuildConfiguration(profile.ConfigOverrides);
        var executionCostSettings = new ExecutionCostSettings(
            settings.FeeRatePercent,
            settings.EstimatedSpreadPercent,
            settings.SlippagePercent);
        var exitPolicy = ReadExitPolicySettings(configuration);
        return new RegimeGatedLongEdgeV1ReplayContext(
            new RegimeGatedLongEdgeV1Model(configuration),
            new ExecutionSimulator(executionCostSettings, exitPolicy),
            new ProfileSignalStats(),
            exitPolicy,
            executionCostSettings,
            configuration,
            btcContext,
            marketWideContext);
    }

    private static Dictionary<string, string> CreateRegimeGatedLongEdgeV1BaseOverrides(
        string interval,
        RegimeGatedLongEdgeV1GateName gate,
        decimal targetPercent,
        decimal stopPercent,
        string maxHoldMinutes,
        RegimeGatedLongEdgeV1EntryConfirmationMode confirmation)
    {
        var requireBtcFavorable = gate is RegimeGatedLongEdgeV1GateName.ElevatedVolMarketReturnWithBtcFavorableGate
            or RegimeGatedLongEdgeV1GateName.WideRangeNearLowWithBtcFavorableGate;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backtest:RangeExpansionBreakoutV1:Enabled"] = "false",
            ["Backtest:RangeExpansionBreakoutV2:Enabled"] = "false",
            ["Backtest:ImpulseContinuationV1:Enabled"] = "false",
            ["Backtest:MeanReversionRangeBounceV1:Enabled"] = "false",
            ["Backtest:HigherTimeframeMomentumPullbackV1:Enabled"] = "false",
            ["Backtest:RegimeGatedLongEdgeV1:Enabled"] = "true",
            ["Backtest:RegimeGatedLongEdgeV1:ProfileInterval"] = interval,
            ["Backtest:RegimeGatedLongEdgeV1:RegimeGateName"] = gate.ToString(),
            ["Backtest:RegimeGatedLongEdgeV1:EntryConfirmationMode"] = confirmation.ToString(),
            ["Backtest:RegimeGatedLongEdgeV1:TargetPercent"] = targetPercent.ToString("F2"),
            ["Backtest:RegimeGatedLongEdgeV1:StopPercent"] = stopPercent.ToString("F2"),
            ["Backtest:RegimeGatedLongEdgeV1:RangeWidthMinPercent"] = "0.8608",
            ["Backtest:RegimeGatedLongEdgeV1:RangeWidthMaxPercent"] = "10.5309",
            ["Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMinPercent"] = "0",
            ["Backtest:RegimeGatedLongEdgeV1:DistanceFromRecentLowMaxPercent"] = "0.1671",
            ["Backtest:RegimeGatedLongEdgeV1:RequireBtcFavorable"] = requireBtcFavorable ? "true" : "false",
            ["Backtest:RegimeGatedLongEdgeV1:RequireBtcAboveMediumMa"] = "true",
            ["Backtest:RegimeGatedLongEdgeV1:MinBtcReturn30mPercent"] = "0",
            ["Backtest:RegimeGatedLongEdgeV1:AllowedBtcTrendRegimes"] = "BtcUp,BtcFlat",
            ["Backtest:RegimeGatedLongEdgeV1:AllowedBtcMarketDirectionBuckets"] = "RiskOn,Neutral",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableLowVolatilityBreakoutEntry"] = "false",
            ["DecisionEngine:MovingAverageCrossoverStrategy:EnableNormalTrendPullbackContinuationOverride"] = "false",
            ["Backtest:BnbRetestContinuationV1:Enabled"] = "false",
            ["Backtest:BnbPullbackGuard:Enabled"] = "false",
            ["Backtest:PullbackFollowThroughV2:Enabled"] = "false",
            ["Backtest:ExitPolicy:Name"] = "ProfitTarget",
            ["Backtest:ExitPolicy:ProfitLockThresholdPercent"] = "100",
            ["Backtest:ExitPolicy:EnableBreakevenAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableTrailingAfterNetProfit"] = "false",
            ["Backtest:ExitPolicy:EnableHalfLockBreakevenExit"] = "false",
            ["Backtest:ExitPolicy:EnableStructuralStop"] = "true",
            ["Backtest:ExitPolicy:StructuralStopMode"] = "RangeLow",
            ["Backtest:ExitPolicy:MaxHoldMinutes"] = maxHoldMinutes
        };
    }
}
