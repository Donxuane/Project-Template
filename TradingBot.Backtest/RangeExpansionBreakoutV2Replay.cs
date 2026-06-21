using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record RangeExpansionV2CandidateRecord
{
    public string WindowLabel { get; init; } = string.Empty;
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime TimeUtc { get; init; }
    public bool Executed { get; init; }
    public string? RejectionReason { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal? BreakoutBodyStrengthPercent { get; init; }
    public decimal? BreakoutCloseAboveRangePercent { get; init; }
    public decimal? BreakoutCandleRangePercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal AtrExpansionRatio { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? RequiredNetProfitPercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public decimal? Lock90NetProfitPercent { get; init; }
    public bool Lock90ReachableWithin60m { get; init; }
    public bool Lock90MeetsRequiredGross { get; init; }
    public decimal? ForwardMfe60Percent { get; init; }
    public decimal? ForwardMae60Percent { get; init; }
    public bool ExpectedMoveInflated { get; init; }
    public decimal? NetPnlQuote { get; init; }
    public decimal? GrossPnlQuote { get; init; }
    public decimal? FeeAndSpreadEstimateQuote { get; init; }
    public string? ExitReason { get; init; }
    public decimal DurationMinutes { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public bool? IsWinner { get; init; }
    public decimal? ForwardMfe15Percent { get; init; }
    public decimal? ForwardMfe30Percent { get; init; }
    public decimal? ForwardMae15Percent { get; init; }
    public decimal? ForwardMae30Percent { get; init; }
    public int? TimeToLock90Minutes { get; init; }
    public decimal? HalfLockDistancePercent { get; init; }
    public bool DidReachHalfLockBeforeStop { get; init; }
    public bool DidReachHalfLockBeforeTimeStop { get; init; }
    public decimal? MfeBeforeStopPercent { get; init; }
    public decimal? MaeBeforeProfitLockPercent { get; init; }
    public int? TimeToMaxFavorableMinutes { get; init; }
    public int? TimeToStopMinutes { get; init; }
    public int? TimeToMfeMinutes { get; init; }
    public int? TimeToMaeMinutes { get; init; }
    public decimal? TimeStopExitMovePercent { get; init; }
    public bool TimeStopWasNearBreakeven { get; init; }
    public bool TimeStopWasGrossPositive { get; init; }
    public bool TimeStopWasNetNegativeOnlyDueToFees { get; init; }
    public string? OutcomeBucket { get; init; }
    public decimal? StructuralStopDistancePercent { get; init; }
    public decimal? BreakoutCandleRangeToAtrRatio { get; init; }
    public decimal? RealizedMoveProxyPercent { get; init; }
    public bool ExpectedMoveInflatedAtEntry { get; init; }
    public decimal? StopToLockRatio { get; init; }
    public decimal? GivebackAtEntryPercent { get; init; }
    public decimal? FollowThroughCloseStrengthPercent { get; init; }
}

public sealed record RangeExpansionV2SummaryRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int BlockedCount { get; init; }
    public int Lock90MeetsRequiredGrossCount { get; init; }
    public int Lock90ReachableCount { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public int TradesCount { get; init; }
    public int NetWinnerCount { get; init; }
    public string RepeatabilityVerdict { get; init; } = string.Empty;
}

public sealed record RangeExpansionV2ExitBreakdownRow
{
    public string ExitReason { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal? AvgDurationMinutes { get; init; }
}

public sealed record RangeExpansionV2RunResult(
    IReadOnlyList<RangeExpansionV2CandidateRecord> Candidates,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<RangeExpansionV2SummaryRow> Summaries,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<RangeExpansionV2ExitBreakdownRow> ExitBreakdown,
    RangeExpansionV2ExtendedDiagnostics? ExtendedDiagnostics,
    int ProfileCount);

public sealed record RangeExpansionV2ReplayContext(
    RangeExpansionBreakoutV2Model Model,
    ExecutionSimulator Simulator,
    ProfileSignalStats SignalStats,
    BacktestExitPolicySettings ExitPolicy,
    ExecutionCostSettings ExecutionCosts,
    IConfiguration Configuration);

internal static class RangeExpansionBreakoutV2Replay
{
    private static readonly StrategySignalResult HoldSignal = new() { Signal = TradeSignal.Hold };

    public static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string symbolsText,
        RangeExpansionV2ReplayContext context,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        List<BlockedEntryRecord> blockedEntriesDestination,
        List<RangeExpansionV2CandidateRecord> candidateDestination,
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
                    interval, symbol, quantity, HoldSignal, snapshot,
                    profileName, symbolsText, trades);
            }

            var step = model.ProcessCandle(
                candles, i, interval, symbol, profitLockThreshold,
                context.ExecutionCosts, context.Configuration);
            if (step.Kind == RangeExpansionV2StepKind.NoAction)
                continue;

            signalStats.RawBuySignals++;
            var entryPrice = snapshot.CurrentPrice;
            var forward = AnalyzeForward(sourceOneMinuteCandles, snapshot.TimestampUtc, entryPrice, step.Diagnostics.ExpectedMovePercent);
            var requiredGross = step.Diagnostics.RequiredGrossMovePercent ?? 0m;
            var lock90MeetsGross = step.Diagnostics.Lock90DistancePercent is >= 0m
                && step.Diagnostics.Lock90DistancePercent.Value >= requiredGross;
            var inflated = step.Diagnostics.ExpectedMovePercent.HasValue
                && forward.ForwardMfe60Percent.HasValue
                && step.Diagnostics.ExpectedMovePercent.Value > forward.ForwardMfe60Percent.Value * 1.25m;

            if (step.Kind == RangeExpansionV2StepKind.Blocked)
            {
                signalStats.IncrementStrategyRejected(step.RejectionReason ?? "RangeExpansionV2:Rejected");
                blockedEntriesDestination.Add(BuildBlockedEntry(interval, profileName, symbolsText, symbol, snapshot.TimestampUtc, step));
                candidateDestination.Add(BuildCandidateRecord(
                    windowLabel, interval, profileName, symbolsText, symbol, snapshot.TimestampUtc,
                    entryPrice, step.Diagnostics, forward, inflated, executed: false, lock90MeetsGross));
                continue;
            }

            signalStats.ExecutedBuySignals++;
            candidateDestination.Add(BuildCandidateRecord(
                windowLabel, interval, profileName, symbolsText, symbol, snapshot.TimestampUtc,
                entryPrice, step.Diagnostics, forward, inflated, executed: true, lock90MeetsGross));

            var roundTripCost = RangeExpansionCostModel.ComputeRoundTripCostPercent(context.ExecutionCosts);
            simulator.OnSignal(
                interval, symbol, quantity, step.Signal, snapshot,
                profileName, symbolsText, trades,
                wasGuarded: false,
                estimatedRoundTripCostPercent: roundTripCost,
                estimatedNetMovePercent: step.Diagnostics.ExpectedMovePercent);
        }

        if (forceCloseAtEnd && lastSnapshot is not null)
            simulator.ForceClose(symbol, lastSnapshot, "EndOfData", profileName, symbolsText, trades);

        AttachTradeOutcomes(candidateDestination, trades, profileName, interval, symbol);
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
                Lock90DistancePercent = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 90m)
            };
        }

        return CandidateForwardOutcomeAnalyzer.Analyze(sourceOneMinuteCandles, entryTimeUtc, entryPrice, expectedMovePercent);
    }

    private static void AttachTradeOutcomes(
        List<RangeExpansionV2CandidateRecord> candidates,
        IReadOnlyList<SimulatedTrade> trades,
        string profileName,
        string interval,
        TradingSymbol symbol)
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
                continue;

            candidates[index] = RangeExpansionV2DiagnosticsAggregator.EnrichFailureTiming(candidate with
            {
                NetPnlQuote = trade.NetPnlQuote,
                GrossPnlQuote = trade.GrossPnlQuote,
                FeeAndSpreadEstimateQuote = trade.FeeAndSpreadEstimateQuote,
                ExitReason = trade.ExitReason,
                DurationMinutes = trade.DurationMinutes,
                MfePercent = trade.MfePercent,
                MaePercent = trade.MaePercent,
                IsWinner = trade.NetPnlQuote > 0m
            });
        }
    }

    private static RangeExpansionV2CandidateRecord BuildCandidateRecord(
        string windowLabel,
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        decimal entryPrice,
        RangeExpansionBreakoutV2Diagnostics diagnostics,
        ForwardOutcomeAnalytics forward,
        bool inflated,
        bool executed,
        bool lock90MeetsGross)
        => new()
        {
            WindowLabel = windowLabel,
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Executed = executed,
            RejectionReason = diagnostics.RejectionReason,
            EntryPrice = entryPrice,
            RangeWidthPercent = diagnostics.RangeWidthPercent,
            BreakoutBodyStrengthPercent = diagnostics.BreakoutBodyStrengthPercent,
            BreakoutCloseAboveRangePercent = diagnostics.BreakoutCloseAboveRangePercent,
            BreakoutCandleRangePercent = diagnostics.BreakoutCandleRangePercent,
            AtrPercent = diagnostics.AtrPercent,
            AtrExpansionRatio = diagnostics.AtrExpansionRatio,
            VolumeExpansionRatio = diagnostics.VolumeExpansionRatio,
            TargetModelName = diagnostics.TargetModelName,
            ExpectedMovePercent = diagnostics.ExpectedMovePercent,
            Lock90DistancePercent = forward.Lock90DistancePercent ?? diagnostics.Lock90DistancePercent,
            EstimatedRoundTripCostPercent = diagnostics.EstimatedRoundTripCostPercent,
            RequiredNetProfitPercent = diagnostics.RequiredNetProfitPercent,
            RequiredGrossMovePercent = diagnostics.RequiredGrossMovePercent,
            Lock90NetProfitPercent = diagnostics.Lock90NetProfitPercent,
            Lock90ReachableWithin60m = forward.Lock90ReachableWithin60m,
            Lock90MeetsRequiredGross = lock90MeetsGross,
            ForwardMfe15Percent = forward.ForwardMfe15Percent,
            ForwardMfe30Percent = forward.ForwardMfe30Percent,
            ForwardMfe60Percent = forward.ForwardMfe60Percent,
            ForwardMae15Percent = forward.ForwardMae15Percent,
            ForwardMae30Percent = forward.ForwardMae30Percent,
            ForwardMae60Percent = forward.ForwardMae60Percent,
            TimeToLock90Minutes = forward.TimeToLock90Minutes,
            ExpectedMoveInflated = inflated,
            StructuralStopDistancePercent = diagnostics.RangeLow > 0m && entryPrice > 0m
                ? (entryPrice - diagnostics.RangeLow) / entryPrice * 100m
                : null,
            BreakoutCandleRangeToAtrRatio = diagnostics.AtrPercent > 0m && diagnostics.BreakoutCandleRangePercent.HasValue
                ? diagnostics.BreakoutCandleRangePercent.Value / diagnostics.AtrPercent
                : null,
            RealizedMoveProxyPercent = diagnostics.RealizedMoveProxyPercent,
            ExpectedMoveInflatedAtEntry = diagnostics.ExpectedMoveInflatedAtEntry,
            StopToLockRatio = diagnostics.StopToLockRatio ?? (diagnostics.RangeLow > 0m && entryPrice > 0m && diagnostics.Lock90DistancePercent is > 0m
                ? Math.Round((entryPrice - diagnostics.RangeLow) / entryPrice * 100m / diagnostics.Lock90DistancePercent.Value, 6)
                : null),
            GivebackAtEntryPercent = diagnostics.GivebackAtEntryPercent,
            FollowThroughCloseStrengthPercent = diagnostics.FollowThroughCloseStrengthPercent
        };

    private static BlockedEntryRecord BuildBlockedEntry(
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        RangeExpansionBreakoutV2StepResult step)
        => new()
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Reason = step.RejectionReason ?? "RangeExpansionV2:Rejected",
            ExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            ExpectedTargetSource = step.Diagnostics.TargetModelName,
            SignalReason = "Range expansion V2 candidate rejected.",
            RejectionLayer = "RangeExpansionV2",
            TargetModelName = step.Diagnostics.TargetModelName,
            TargetWasCapped = step.Diagnostics.TargetWasCapped,
            CapReason = step.Diagnostics.CapReason,
            CappedExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            LockDistancePercent = step.Diagnostics.Lock90DistancePercent
        };
}
