using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record RangeExpansionCandidateRecord
{
    public string WindowLabel { get; init; } = string.Empty;
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime TimeUtc { get; init; }
    public bool Executed { get; init; }
    public string RejectionLayer { get; init; } = string.Empty;
    public string? RejectionReason { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal RangeHigh { get; init; }
    public decimal RangeLow { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal BreakoutBufferPercent { get; init; }
    public decimal BreakoutClose { get; init; }
    public bool BreakoutConfirmed { get; init; }
    public bool FollowThroughConfirmed { get; init; }
    public decimal? DistanceFromBreakoutPercent { get; init; }
    public decimal AtrPercent { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public decimal? Lock95DistancePercent { get; init; }
    public decimal? Lock98DistancePercent { get; init; }
    public bool TargetWasCapped { get; init; }
    public string? CapReason { get; init; }
    public decimal? MaxAllowedLockDistancePercent { get; init; }
    public decimal? ForwardMfe15Percent { get; init; }
    public decimal? ForwardMfe30Percent { get; init; }
    public decimal? ForwardMfe60Percent { get; init; }
    public decimal? ForwardMae15Percent { get; init; }
    public decimal? ForwardMae30Percent { get; init; }
    public decimal? ForwardMae60Percent { get; init; }
    public bool Lock90ReachableWithin60m { get; init; }
    public bool Lock95ReachableWithin60m { get; init; }
    public bool Lock98ReachableWithin60m { get; init; }
    public int? TimeToLock90Minutes { get; init; }
    public int? TimeToLock95Minutes { get; init; }
    public int? TimeToLock98Minutes { get; init; }
    public bool ExpectedMoveInflated { get; init; }
    public decimal? NetPnlQuote { get; init; }
    public string? ExitReason { get; init; }
    public string? ExitPolicyName { get; init; }
    public decimal? ProfitLockThresholdPercent { get; init; }
    public decimal? GrossPnlQuote { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public decimal? GivebackFromMfePercent { get; init; }
    public decimal? CapturedMfePercent { get; init; }
    public decimal DurationMinutes { get; init; }
    public decimal? BreakoutCloseAboveRangePercent { get; init; }
    public decimal? BreakoutBodyStrengthPercent { get; init; }
    public decimal? BreakoutCandleRangePercent { get; init; }
    public int CandidateAgeCandles { get; init; } = 1;
    public string? VolatilityRegime { get; init; }
    public decimal? TrendStrengthPercent { get; init; }
    public decimal? ShortMaSlopePercent { get; init; }
    public bool Lock90ReachedBeforeMaeThreshold { get; init; }
    public decimal? MaeBeforeTargetPercent { get; init; }
    public int? EntryToMaxFavorableMinutes { get; init; }
    public bool? IsWinner { get; init; }
    public bool IsProfitLockExit { get; init; }
    public bool IsOppositeSignalExit { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? RequiredNetProfitPercent { get; init; }
    public decimal? RequiredGrossProfitPercent { get; init; }
    public decimal? Lock90NetProfitPercent { get; init; }
    public decimal? Lock95NetProfitPercent { get; init; }
    public decimal? Lock98NetProfitPercent { get; init; }
    public bool Lock90ReachableAndNetProfitableWithin60m { get; init; }
    public bool Lock95ReachableAndNetProfitableWithin60m { get; init; }
    public bool Lock98ReachableAndNetProfitableWithin60m { get; init; }
    public bool ForwardMfe60NetTradable { get; init; }
    public decimal? FeeAndSpreadEstimateQuote { get; init; }
    public bool ExcludedFromPnlAggregates { get; init; }
    public bool OrphanExecutedCandidate { get; init; }
    public string? ReportingConsistencyFlag { get; init; }
}

public sealed record RangeExpansionSummaryRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int BlockedCount { get; init; }
    public int Lock90ReachableCount { get; init; }
    public int Lock90ReachableExecutedCount { get; init; }
    public decimal Lock90ReachableRate { get; init; }
    public decimal InflationRate { get; init; }
    public decimal? MedianExpectedMovePercent { get; init; }
    public decimal? MedianForwardMfe60Percent { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public int TradesCount { get; init; }
    public string RepeatabilityVerdict { get; init; } = string.Empty;
}

public sealed record RangeExpansionRobustnessRow
{
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int TotalCandidates { get; init; }
    public int TotalLock90Reachable { get; init; }
    public decimal TotalNetPnlQuote { get; init; }
    public IReadOnlyDictionary<string, int> Lock90ReachableByWindow { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, decimal> NetPnlByWindow { get; init; } = new Dictionary<string, decimal>();
    public string FamilyVerdict { get; init; } = string.Empty;
}

public sealed record RangeExpansionBreakoutRunResult(
    IReadOnlyList<RangeExpansionCandidateRecord> Candidates,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<RangeExpansionSummaryRow> Summaries,
    IReadOnlyList<RangeExpansionRobustnessRow> RobustnessSummaries,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    RangeExpansionDiagnosticsBundle Diagnostics,
    int ProfileCount);

public sealed record RangeExpansionReplayContext(
    RangeExpansionBreakoutV1Model Model,
    ExecutionSimulator Simulator,
    ProfileSignalStats SignalStats,
    BacktestExitPolicySettings ExitPolicy,
    ExecutionCostSettings ExecutionCosts,
    IConfiguration Configuration);

internal static class RangeExpansionBreakoutReplay
{
    private static readonly StrategySignalResult HoldSignal = new() { Signal = TradeSignal.Hold };

    public static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string symbolsText,
        RangeExpansionReplayContext context,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        List<BlockedEntryRecord> blockedEntriesDestination,
        List<RangeExpansionCandidateRecord> candidateDestination,
        IReadOnlyList<KlineCandle>? sourceOneMinuteCandles,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        var trades = new List<SimulatedTrade>();
        var model = context.Model;
        var simulator = context.Simulator;
        var signalStats = context.SignalStats;
        var profitLockThreshold = context.ExitPolicy.ProfitLockThresholdPercent;
        var minWarmup = model.IsEnabled ? model.MinRequiredCandles : 2;

        model.Reset();
        MarketSnapshot? lastSnapshot = null;

        for (var i = 0; i < candles.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (i + 1 < minWarmup)
                continue;

            var snapshot = MarketSnapshotFactory.Build(candles, i);
            lastSnapshot = snapshot;

            if (simulator.HasOpenPosition(symbol))
            {
                simulator.OnSignal(
                    interval,
                    symbol,
                    quantity,
                    HoldSignal,
                    snapshot,
                    profileName,
                    symbolsText,
                    trades);
            }

            var step = model.ProcessCandle(
                candles,
                i,
                interval,
                symbol,
                profitLockThreshold,
                context.ExecutionCosts,
                context.Configuration);
            if (step.Kind == RangeExpansionStepKind.NoAction)
                continue;

            signalStats.RawBuySignals++;
            var entryPrice = snapshot.CurrentPrice;
            var forward = AnalyzeForward(sourceOneMinuteCandles, snapshot.TimestampUtc, entryPrice, step.Diagnostics.ExpectedMovePercent);
            var inflated = step.Diagnostics.ExpectedMovePercent.HasValue
                           && forward.ForwardMfe60Percent.HasValue
                           && step.Diagnostics.ExpectedMovePercent.Value > forward.ForwardMfe60Percent.Value * 1.25m;

            if (step.Kind == RangeExpansionStepKind.Blocked)
            {
                signalStats.IncrementStrategyRejected(step.RejectionReason ?? "RangeExpansion:Rejected");
                blockedEntriesDestination.Add(BuildBlockedEntry(
                    interval,
                    profileName,
                    symbolsText,
                    symbol,
                    snapshot.TimestampUtc,
                    step));
                candidateDestination.Add(BuildCandidateRecord(
                    windowLabel,
                    interval,
                    profileName,
                    symbolsText,
                    symbol,
                    snapshot.TimestampUtc,
                    entryPrice,
                    step.Diagnostics,
                    forward,
                    inflated,
                    executed: false,
                    rejectionLayer: "RangeExpansion",
                    rejectionReason: step.RejectionReason,
                    context: context,
                    profitLockThresholdPercent: profitLockThreshold));
                continue;
            }

            signalStats.ExecutedBuySignals++;
            var roundTripCost = RangeExpansionCostModel.ComputeRoundTripCostPercent(context.ExecutionCosts);
            candidateDestination.Add(BuildCandidateRecord(
                windowLabel,
                interval,
                profileName,
                symbolsText,
                symbol,
                snapshot.TimestampUtc,
                entryPrice,
                step.Diagnostics,
                forward,
                inflated,
                executed: true,
                rejectionLayer: "Executed",
                rejectionReason: null,
                exitPolicyName: context.ExitPolicy.ExitPolicyName,
                profitLockThresholdPercent: profitLockThreshold,
                signal: step.Signal,
                context: context));

            simulator.OnSignal(
                interval,
                symbol,
                quantity,
                step.Signal,
                snapshot,
                profileName,
                symbolsText,
                trades,
                wasGuarded: false,
                estimatedRoundTripCostPercent: roundTripCost,
                estimatedNetMovePercent: step.Diagnostics.ExpectedMovePercent,
                retestDiagnostics: MapDiagnostics(step.Diagnostics));
        }

        if (forceCloseAtEnd && lastSnapshot is not null)
            simulator.ForceClose(symbol, lastSnapshot, "EndOfData", profileName, symbolsText, trades);

        AttachTradeOutcomes(candidateDestination, trades, profileName, interval, symbol, context.ExecutionCosts, context.Configuration);
        return trades;
    }

    private static ForwardOutcomeAnalytics AnalyzeForward(
        IReadOnlyList<KlineCandle>? sourceOneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        decimal? expectedMovePercent)
    {
        if (sourceOneMinuteCandles is null || sourceOneMinuteCandles.Count == 0)
        {
            return new ForwardOutcomeAnalytics
            {
                Lock90DistancePercent = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 90m),
                Lock95DistancePercent = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 95m),
                Lock98DistancePercent = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 98m)
            };
        }

        return CandidateForwardOutcomeAnalyzer.Analyze(
            sourceOneMinuteCandles,
            entryTimeUtc,
            entryPrice,
            expectedMovePercent);
    }

    private static void AttachTradeOutcomes(
        List<RangeExpansionCandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        string profileName,
        string interval,
        TradingSymbol symbol,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        var tradeLookup = trades
            .GroupBy(t => $"{t.ProfileName}|{t.Interval}|{t.Symbol}|{t.EntryTimeUtc:O}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (!candidate.Executed
                || candidate.Symbol != symbol
                || !string.Equals(candidate.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(candidate.Interval, interval, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = $"{candidate.ProfileName}|{candidate.Interval}|{candidate.Symbol}|{candidate.TimeUtc:O}";
            if (!tradeLookup.TryGetValue(key, out var trade))
            {
                candidates[index] = candidate with
                {
                    OrphanExecutedCandidate = true,
                    ExcludedFromPnlAggregates = true,
                    ReportingConsistencyFlag = "OrphanExecutedCandidateNoMatchingTrade"
                };
                continue;
            }

            var cost = RangeExpansionCostModel.Compute(
                executionCosts,
                configuration,
                candidate.ExpectedMovePercent,
                candidate.Lock90DistancePercent,
                candidate.Lock95DistancePercent,
                candidate.Lock98DistancePercent,
                candidate.ForwardMfe60Percent,
                candidate.Lock90ReachableWithin60m,
                candidate.Lock95ReachableWithin60m,
                candidate.Lock98ReachableWithin60m,
                trade.ProfitLockThresholdPercent);

            var exitReason = string.IsNullOrWhiteSpace(trade.ExitReason) ? "UnknownExit" : trade.ExitReason;
            var excluded = string.Equals(exitReason, "UnknownExit", StringComparison.OrdinalIgnoreCase);
            var isProfitLock = string.Equals(exitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase);

            candidates[index] = RangeExpansionCostModel.Apply(candidate, cost) with
            {
                NetPnlQuote = trade.NetPnlQuote,
                GrossPnlQuote = trade.GrossPnlQuote,
                FeeAndSpreadEstimateQuote = trade.FeeAndSpreadEstimateQuote,
                ExitReason = exitReason,
                ExitPolicyName = trade.ExitPolicyName,
                ProfitLockThresholdPercent = trade.ProfitLockThresholdPercent,
                MfePercent = trade.MfePercent,
                MaePercent = trade.MaePercent,
                GivebackFromMfePercent = trade.GivebackFromMfePercent,
                CapturedMfePercent = trade.CapturedMfePercent,
                DurationMinutes = trade.DurationMinutes,
                VolatilityRegime = trade.VolatilityRegime ?? candidate.VolatilityRegime,
                TrendStrengthPercent = trade.TrendStrengthPercent ?? candidate.TrendStrengthPercent,
                ShortMaSlopePercent = trade.ShortMaSlopePercent ?? candidate.ShortMaSlopePercent,
                Lock90ReachedBeforeMaeThreshold = candidate.TimeToLock90Minutes.HasValue
                    && (!candidate.ForwardMae15Percent.HasValue || candidate.ForwardMae15Percent.Value >= -0.20m),
                MaeBeforeTargetPercent = candidate.TimeToLock90Minutes.HasValue
                    ? candidate.ForwardMae15Percent
                    : candidate.ForwardMae60Percent,
                EntryToMaxFavorableMinutes = candidate.TimeToLock90Minutes,
                IsWinner = trade.NetPnlQuote > 0m,
                IsProfitLockExit = isProfitLock,
                IsOppositeSignalExit = string.Equals(exitReason, "OppositeSignal", StringComparison.OrdinalIgnoreCase),
                ExcludedFromPnlAggregates = excluded,
                ReportingConsistencyFlag = excluded ? "MissingExitReason" : null
            };
        }
    }

    private static RangeExpansionCandidateRecord BuildCandidateRecord(
        string windowLabel,
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        decimal entryPrice,
        RangeExpansionBreakoutDiagnostics diagnostics,
        ForwardOutcomeAnalytics forward,
        bool inflated,
        bool executed,
        string rejectionLayer,
        string? rejectionReason,
        string? exitPolicyName = null,
        decimal? profitLockThresholdPercent = null,
        StrategySignalResult? signal = null,
        RangeExpansionReplayContext? context = null)
    {
        var record = new RangeExpansionCandidateRecord
        {
            WindowLabel = windowLabel,
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Executed = executed,
            RejectionLayer = rejectionLayer,
            RejectionReason = rejectionReason ?? diagnostics.RejectionReason,
            EntryPrice = entryPrice,
            RangeHigh = diagnostics.RangeHigh,
            RangeLow = diagnostics.RangeLow,
            RangeWidthPercent = diagnostics.RangeWidthPercent,
            BreakoutBufferPercent = diagnostics.BreakoutBufferPercent,
            BreakoutClose = diagnostics.BreakoutClose,
            BreakoutConfirmed = diagnostics.BreakoutConfirmed,
            FollowThroughConfirmed = diagnostics.FollowThroughConfirmed,
            BreakoutCloseAboveRangePercent = diagnostics.BreakoutCloseAboveRangePercent,
            BreakoutBodyStrengthPercent = diagnostics.BreakoutBodyStrengthPercent,
            BreakoutCandleRangePercent = diagnostics.BreakoutCandleRangePercent,
            CandidateAgeCandles = diagnostics.CandidateAgeCandles,
            VolatilityRegime = signal?.VolatilityRegime,
            DistanceFromBreakoutPercent = diagnostics.DistanceFromBreakoutPercent,
            AtrPercent = diagnostics.AtrPercent,
            TargetModelName = diagnostics.TargetModelName,
            ExpectedMovePercent = diagnostics.ExpectedMovePercent,
            Lock90DistancePercent = forward.Lock90DistancePercent ?? diagnostics.Lock90DistancePercent,
            Lock95DistancePercent = forward.Lock95DistancePercent ?? diagnostics.Lock95DistancePercent,
            Lock98DistancePercent = forward.Lock98DistancePercent ?? diagnostics.Lock98DistancePercent,
            TargetWasCapped = diagnostics.TargetWasCapped,
            CapReason = diagnostics.CapReason,
            MaxAllowedLockDistancePercent = diagnostics.MaxAllowedLockDistancePercent,
            ForwardMfe15Percent = forward.ForwardMfe15Percent,
            ForwardMfe30Percent = forward.ForwardMfe30Percent,
            ForwardMfe60Percent = forward.ForwardMfe60Percent,
            ForwardMae15Percent = forward.ForwardMae15Percent,
            ForwardMae30Percent = forward.ForwardMae30Percent,
            ForwardMae60Percent = forward.ForwardMae60Percent,
            Lock90ReachableWithin60m = forward.Lock90ReachableWithin60m,
            Lock95ReachableWithin60m = forward.Lock95ReachableWithin60m,
            Lock98ReachableWithin60m = forward.Lock98ReachableWithin60m,
            TimeToLock90Minutes = forward.TimeToLock90Minutes,
            TimeToLock95Minutes = forward.TimeToLock95Minutes,
            TimeToLock98Minutes = forward.TimeToLock98Minutes,
            ExpectedMoveInflated = inflated,
            ExitPolicyName = exitPolicyName,
            ProfitLockThresholdPercent = profitLockThresholdPercent
        };

        if (context is null)
            return record;

        var cost = RangeExpansionCostModel.Compute(
            context.ExecutionCosts,
            context.Configuration,
            record.ExpectedMovePercent,
            record.Lock90DistancePercent,
            record.Lock95DistancePercent,
            record.Lock98DistancePercent,
            record.ForwardMfe60Percent,
            record.Lock90ReachableWithin60m,
            record.Lock95ReachableWithin60m,
            record.Lock98ReachableWithin60m,
            profitLockThresholdPercent);

        return RangeExpansionCostModel.Apply(record, cost);
    }

    private static BlockedEntryRecord BuildBlockedEntry(
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        RangeExpansionBreakoutStepResult step)
        => new()
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Reason = step.RejectionReason ?? "RangeExpansion:Rejected",
            ExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            ExpectedTargetSource = step.Diagnostics.TargetModelName,
            SignalReason = "Range expansion breakout candidate rejected.",
            RejectionLayer = "RangeExpansion",
            TargetModelName = step.Diagnostics.TargetModelName,
            TargetWasCapped = step.Diagnostics.TargetWasCapped,
            CapReason = step.Diagnostics.CapReason,
            CappedExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            LockDistancePercent = step.Diagnostics.Lock90DistancePercent
        };

    private static BnbRetestContinuationDiagnostics MapDiagnostics(RangeExpansionBreakoutDiagnostics diagnostics)
        => new()
        {
            RetestContinuationEnabled = false,
            CappedExpectedMovePercent = diagnostics.ExpectedMovePercent,
            TargetModelName = diagnostics.TargetModelName,
            TargetWasCapped = diagnostics.TargetWasCapped,
            CapReason = diagnostics.CapReason,
            LockDistancePercent = diagnostics.Lock90DistancePercent
        };
}
