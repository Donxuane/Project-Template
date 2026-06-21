using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record ImpulseContinuationV1CandidateRecord
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
    public decimal ImpulseBodyStrengthPercent { get; init; }
    public decimal ImpulseRangePercent { get; init; }
    public decimal ImpulseRangeVsAverage { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public decimal CloseNearHighPercent { get; init; }
    public bool FollowThroughConfirmed { get; init; }
    public decimal? FollowThroughCloseStrengthPercent { get; init; }
    public decimal? EntryDistanceFromImpulseHighPercent { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? Lock90DistancePercent { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? RequiredNetProfitPercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public decimal? Lock90NetProfitPercent { get; init; }
    public string? TargetModelName { get; init; }
    public bool TargetWasCapped { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? StopToLockRatio { get; init; }
    public decimal? ForwardMfe15Percent { get; init; }
    public decimal? ForwardMfe30Percent { get; init; }
    public decimal? ForwardMfe60Percent { get; init; }
    public decimal? ForwardMae15Percent { get; init; }
    public decimal? ForwardMae30Percent { get; init; }
    public decimal? ForwardMae60Percent { get; init; }
    public bool Lock90ReachableWithin60m { get; init; }
    public int? TimeToLock90Minutes { get; init; }
    public bool Lock90MeetsRequiredGross { get; init; }
    public decimal? NetPnlQuote { get; init; }
    public decimal? GrossPnlQuote { get; init; }
    public decimal? FeeAndSpreadEstimateQuote { get; init; }
    public string? ExitReason { get; init; }
    public decimal DurationMinutes { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public bool? IsWinner { get; init; }
}

public sealed record ImpulseContinuationV1SummaryRow
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
    public decimal? AvgExpectedMovePercent { get; init; }
    public string RepeatabilityVerdict { get; init; } = string.Empty;
}

public sealed record ImpulseContinuationV1ExitBreakdownRow
{
    public string ExitReason { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal? AvgDurationMinutes { get; init; }
}

public sealed record ImpulseContinuationV1WindowRobustnessRow
{
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "1m";
    public int Window30dCandidates { get; init; }
    public int Window60dCandidates { get; init; }
    public int Window90dCandidates { get; init; }
    public int Window30dTrades { get; init; }
    public int Window60dTrades { get; init; }
    public int Window90dTrades { get; init; }
    public decimal Window30dNetPnl { get; init; }
    public decimal Window60dNetPnl { get; init; }
    public decimal Window90dNetPnl { get; init; }
    public string RobustnessVerdict { get; init; } = string.Empty;
}

public sealed record ImpulseContinuationV1RunResult(
    IReadOnlyList<ImpulseContinuationV1CandidateRecord> Candidates,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<ImpulseContinuationV1SummaryRow> Summaries,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<ImpulseContinuationV1ExitBreakdownRow> ExitBreakdown,
    IReadOnlyList<ImpulseContinuationV1WindowRobustnessRow> WindowRobustness,
    int ProfileCount);

public sealed record ImpulseContinuationV1ReplayContext(
    ImpulseContinuationV1Model Model,
    ExecutionSimulator Simulator,
    ProfileSignalStats SignalStats,
    BacktestExitPolicySettings ExitPolicy,
    ExecutionCostSettings ExecutionCosts,
    IConfiguration Configuration);

internal static class ImpulseContinuationV1Replay
{
    private static readonly StrategySignalResult HoldSignal = new() { Signal = TradeSignal.Hold };

    public static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string symbolsText,
        ImpulseContinuationV1ReplayContext context,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        List<BlockedEntryRecord> blockedEntriesDestination,
        List<ImpulseContinuationV1CandidateRecord> candidateDestination,
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
            if (step.Kind == ImpulseContinuationV1StepKind.NoAction)
                continue;

            signalStats.RawBuySignals++;
            var entryPrice = snapshot.CurrentPrice;
            var forward = AnalyzeForward(sourceOneMinuteCandles, snapshot.TimestampUtc, entryPrice, step.Diagnostics.ExpectedMovePercent);
            var requiredGross = step.Diagnostics.RequiredGrossMovePercent ?? 0m;
            var lock90MeetsGross = step.Diagnostics.Lock90DistancePercent is >= 0m
                && step.Diagnostics.Lock90DistancePercent.Value >= requiredGross;

            if (step.Kind == ImpulseContinuationV1StepKind.Blocked)
            {
                signalStats.IncrementStrategyRejected(step.RejectionReason ?? "ImpulseContinuationV1:Rejected");
                blockedEntriesDestination.Add(BuildBlockedEntry(interval, profileName, symbolsText, symbol, snapshot.TimestampUtc, step));
                candidateDestination.Add(BuildCandidateRecord(
                    windowLabel, interval, profileName, symbolsText, symbol, snapshot.TimestampUtc,
                    entryPrice, step.Diagnostics, forward, executed: false, lock90MeetsGross));
                continue;
            }

            signalStats.ExecutedBuySignals++;
            candidateDestination.Add(BuildCandidateRecord(
                windowLabel, interval, profileName, symbolsText, symbol, snapshot.TimestampUtc,
                entryPrice, step.Diagnostics, forward, executed: true, lock90MeetsGross));

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
        List<ImpulseContinuationV1CandidateRecord> candidates,
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

            candidates[index] = candidate with
            {
                NetPnlQuote = trade.NetPnlQuote,
                GrossPnlQuote = trade.GrossPnlQuote,
                FeeAndSpreadEstimateQuote = trade.FeeAndSpreadEstimateQuote,
                ExitReason = trade.ExitReason,
                DurationMinutes = trade.DurationMinutes,
                MfePercent = trade.MfePercent,
                MaePercent = trade.MaePercent,
                IsWinner = trade.NetPnlQuote > 0m
            };
        }
    }

    private static ImpulseContinuationV1CandidateRecord BuildCandidateRecord(
        string windowLabel,
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        decimal entryPrice,
        ImpulseContinuationV1Diagnostics diagnostics,
        ForwardOutcomeAnalytics forward,
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
            ImpulseBodyStrengthPercent = diagnostics.ImpulseBodyStrengthPercent,
            ImpulseRangePercent = diagnostics.ImpulseRangePercent,
            ImpulseRangeVsAverage = diagnostics.ImpulseRangeVsAverage,
            VolumeExpansionRatio = diagnostics.VolumeExpansionRatio,
            CloseNearHighPercent = diagnostics.CloseNearHighPercent,
            FollowThroughConfirmed = diagnostics.FollowThroughConfirmed,
            FollowThroughCloseStrengthPercent = diagnostics.FollowThroughCloseStrengthPercent,
            EntryDistanceFromImpulseHighPercent = diagnostics.EntryDistanceFromImpulseHighPercent,
            ExpectedMovePercent = diagnostics.ExpectedMovePercent,
            Lock90DistancePercent = forward.Lock90DistancePercent ?? diagnostics.Lock90DistancePercent,
            EstimatedRoundTripCostPercent = diagnostics.EstimatedRoundTripCostPercent,
            RequiredNetProfitPercent = diagnostics.RequiredNetProfitPercent,
            RequiredGrossMovePercent = diagnostics.RequiredGrossMovePercent,
            Lock90NetProfitPercent = diagnostics.Lock90NetProfitPercent,
            TargetModelName = diagnostics.TargetModelName,
            TargetWasCapped = diagnostics.TargetWasCapped,
            StopDistancePercent = diagnostics.StopDistancePercent,
            StopToLockRatio = diagnostics.StopToLockRatio,
            ForwardMfe15Percent = forward.ForwardMfe15Percent,
            ForwardMfe30Percent = forward.ForwardMfe30Percent,
            ForwardMfe60Percent = forward.ForwardMfe60Percent,
            ForwardMae15Percent = forward.ForwardMae15Percent,
            ForwardMae30Percent = forward.ForwardMae30Percent,
            ForwardMae60Percent = forward.ForwardMae60Percent,
            Lock90ReachableWithin60m = forward.Lock90ReachableWithin60m,
            TimeToLock90Minutes = forward.TimeToLock90Minutes,
            Lock90MeetsRequiredGross = lock90MeetsGross
        };

    private static BlockedEntryRecord BuildBlockedEntry(
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        ImpulseContinuationV1StepResult step)
        => new()
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Reason = step.RejectionReason ?? "ImpulseContinuationV1:Rejected",
            ExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            ExpectedTargetSource = step.Diagnostics.TargetModelName,
            SignalReason = "Impulse continuation candidate rejected.",
            RejectionLayer = "ImpulseContinuationV1",
            TargetModelName = step.Diagnostics.TargetModelName,
            TargetWasCapped = step.Diagnostics.TargetWasCapped,
            CapReason = step.Diagnostics.CapReason,
            CappedExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            LockDistancePercent = step.Diagnostics.Lock90DistancePercent
        };
}
