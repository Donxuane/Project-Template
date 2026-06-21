using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record MeanReversionRangeBounceV1CandidateRecord
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
    public decimal RangeHigh { get; init; }
    public decimal RangeLow { get; init; }
    public decimal RangeMidpoint { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal DistanceToRangeLowPercent { get; init; }
    public decimal DistanceToRangeHighPercent { get; init; }
    public decimal EntryRejectionCandleBodyPercent { get; init; }
    public decimal EntryRejectionWickPercent { get; init; }
    public bool CloseBackInsideRange { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal AtrPercent { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? RewardRisk { get; init; }
    public decimal? ForwardMfe15Percent { get; init; }
    public decimal? ForwardMfe30Percent { get; init; }
    public decimal? ForwardMfe60Percent { get; init; }
    public decimal? ForwardMae15Percent { get; init; }
    public decimal? ForwardMae30Percent { get; init; }
    public decimal? ForwardMae60Percent { get; init; }
    public bool TargetReachableWithin60m { get; init; }
    public int? TimeToTargetMinutes { get; init; }
    public decimal? NetPnlQuote { get; init; }
    public decimal? GrossPnlQuote { get; init; }
    public decimal? FeeAndSpreadEstimateQuote { get; init; }
    public string? ExitReason { get; init; }
    public decimal DurationMinutes { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public bool? IsWinner { get; init; }
}

public sealed record MeanReversionRangeBounceV1SummaryRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string Interval { get; init; } = "1m";
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int BlockedCount { get; init; }
    public int TargetReachableCount { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public int TradesCount { get; init; }
    public int NetWinnerCount { get; init; }
    public decimal? AvgExpectedMovePercent { get; init; }
    public string RepeatabilityVerdict { get; init; } = string.Empty;
}

public sealed record MeanReversionRangeBounceV1ExitBreakdownRow
{
    public string ExitReason { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal? AvgDurationMinutes { get; init; }
}

public sealed record MeanReversionRangeBounceV1WindowRobustnessRow
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

public sealed record MeanReversionRangeBounceV1RunResult(
    IReadOnlyList<MeanReversionRangeBounceV1CandidateRecord> Candidates,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<MeanReversionRangeBounceV1SummaryRow> Summaries,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<MeanReversionRangeBounceV1ExitBreakdownRow> ExitBreakdown,
    IReadOnlyList<MeanReversionRangeBounceV1WindowRobustnessRow> WindowRobustness,
    int ProfileCount);

public sealed record MeanReversionRangeBounceV1ReplayContext(
    MeanReversionRangeBounceV1Model Model,
    ExecutionSimulator Simulator,
    ProfileSignalStats SignalStats,
    BacktestExitPolicySettings ExitPolicy,
    ExecutionCostSettings ExecutionCosts,
    IConfiguration Configuration);

internal static class MeanReversionRangeBounceV1Replay
{
    private static readonly StrategySignalResult HoldSignal = new() { Signal = TradeSignal.Hold };

    public static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string symbolsText,
        MeanReversionRangeBounceV1ReplayContext context,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        List<BlockedEntryRecord> blockedEntriesDestination,
        List<MeanReversionRangeBounceV1CandidateRecord> candidateDestination,
        IReadOnlyList<KlineCandle>? sourceOneMinuteCandles,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        var trades = new List<SimulatedTrade>();
        var model = context.Model;
        var simulator = context.Simulator;
        var signalStats = context.SignalStats;
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
                candles, i, interval, symbol, context.ExecutionCosts, context.Configuration);
            if (step.Kind == MeanReversionRangeBounceV1StepKind.NoAction)
                continue;

            signalStats.RawBuySignals++;
            var entryPrice = snapshot.CurrentPrice;
            var forward = AnalyzeForward(sourceOneMinuteCandles, snapshot.TimestampUtc, entryPrice, step.Diagnostics.ExpectedMovePercent);

            if (step.Kind == MeanReversionRangeBounceV1StepKind.Blocked)
            {
                signalStats.IncrementStrategyRejected(step.RejectionReason ?? "MeanReversionRangeBounceV1:Rejected");
                blockedEntriesDestination.Add(BuildBlockedEntry(interval, profileName, symbolsText, symbol, snapshot.TimestampUtc, step));
                candidateDestination.Add(BuildCandidateRecord(
                    windowLabel, interval, profileName, symbolsText, symbol, snapshot.TimestampUtc,
                    entryPrice, step.Diagnostics, forward, executed: false));
                continue;
            }

            signalStats.ExecutedBuySignals++;
            candidateDestination.Add(BuildCandidateRecord(
                windowLabel, interval, profileName, symbolsText, symbol, snapshot.TimestampUtc,
                entryPrice, step.Diagnostics, forward, executed: true));

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

    private static TargetForwardAnalytics AnalyzeForward(
        IReadOnlyList<KlineCandle>? sourceOneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        decimal? expectedMovePercent)
    {
        if (sourceOneMinuteCandles is null || sourceOneMinuteCandles.Count == 0 || entryPrice <= 0m)
            return new TargetForwardAnalytics();

        var forward = CandidateForwardOutcomeAnalyzer.Analyze(sourceOneMinuteCandles, entryTimeUtc, entryPrice, expectedMovePercent);
        var (reachable, minutes) = ComputeTargetReachability(sourceOneMinuteCandles, entryTimeUtc, entryPrice, expectedMovePercent);
        return new TargetForwardAnalytics
        {
            ForwardMfe15Percent = forward.ForwardMfe15Percent,
            ForwardMfe30Percent = forward.ForwardMfe30Percent,
            ForwardMfe60Percent = forward.ForwardMfe60Percent,
            ForwardMae15Percent = forward.ForwardMae15Percent,
            ForwardMae30Percent = forward.ForwardMae30Percent,
            ForwardMae60Percent = forward.ForwardMae60Percent,
            TargetReachableWithin60m = reachable,
            TimeToTargetMinutes = minutes
        };
    }

    private static (bool Reachable, int? Minutes) ComputeTargetReachability(
        IReadOnlyList<KlineCandle> oneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice,
        decimal? expectedMovePercent)
    {
        if (!expectedMovePercent.HasValue || expectedMovePercent.Value <= 0m)
            return (false, null);

        var targetPrice = entryPrice * (1m + expectedMovePercent.Value / 100m);
        var entryIdx = -1;
        for (var i = 0; i < oneMinuteCandles.Count; i++)
        {
            if (oneMinuteCandles[i].OpenTimeUtc >= entryTimeUtc)
            {
                entryIdx = i;
                break;
            }
        }

        if (entryIdx < 0)
            return (false, null);

        var horizon = Math.Min(oneMinuteCandles.Count - 1, entryIdx + 60);
        for (var i = entryIdx; i <= horizon; i++)
        {
            if (oneMinuteCandles[i].High >= targetPrice)
                return (true, i - entryIdx);
        }

        return (false, null);
    }

    private static void AttachTradeOutcomes(
        List<MeanReversionRangeBounceV1CandidateRecord> candidates,
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
                ExitReason = NormalizeExitReason(trade.ExitReason, trade.ProfitLockThresholdPercent),
                DurationMinutes = trade.DurationMinutes,
                MfePercent = trade.MfePercent,
                MaePercent = trade.MaePercent,
                IsWinner = trade.NetPnlQuote > 0m
            };
        }
    }

    private static string? NormalizeExitReason(string? exitReason, decimal? profitLockThreshold)
    {
        if (string.Equals(exitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase)
            && profitLockThreshold is >= 99m)
        {
            return "ProfitTarget";
        }

        return exitReason;
    }

    private static MeanReversionRangeBounceV1CandidateRecord BuildCandidateRecord(
        string windowLabel,
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        decimal entryPrice,
        MeanReversionRangeBounceV1Diagnostics diagnostics,
        TargetForwardAnalytics forward,
        bool executed)
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
            RangeHigh = diagnostics.RangeHigh,
            RangeLow = diagnostics.RangeLow,
            RangeMidpoint = diagnostics.RangeMidpoint,
            RangeWidthPercent = diagnostics.RangeWidthPercent,
            DistanceToRangeLowPercent = diagnostics.DistanceToRangeLowPercent,
            DistanceToRangeHighPercent = diagnostics.DistanceToRangeHighPercent,
            EntryRejectionCandleBodyPercent = diagnostics.EntryRejectionCandleBodyPercent,
            EntryRejectionWickPercent = diagnostics.EntryRejectionWickPercent,
            CloseBackInsideRange = diagnostics.CloseBackInsideRange,
            TrendSlopePercent = diagnostics.TrendSlopePercent,
            AtrPercent = diagnostics.AtrPercent,
            TargetModelName = diagnostics.TargetModelName,
            ExpectedMovePercent = diagnostics.ExpectedMovePercent,
            RequiredGrossMovePercent = diagnostics.RequiredGrossMovePercent,
            StopDistancePercent = diagnostics.StopDistancePercent,
            RewardRisk = diagnostics.RewardRisk,
            ForwardMfe15Percent = forward.ForwardMfe15Percent,
            ForwardMfe30Percent = forward.ForwardMfe30Percent,
            ForwardMfe60Percent = forward.ForwardMfe60Percent,
            ForwardMae15Percent = forward.ForwardMae15Percent,
            ForwardMae30Percent = forward.ForwardMae30Percent,
            ForwardMae60Percent = forward.ForwardMae60Percent,
            TargetReachableWithin60m = forward.TargetReachableWithin60m,
            TimeToTargetMinutes = forward.TimeToTargetMinutes
        };

    private static BlockedEntryRecord BuildBlockedEntry(
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        MeanReversionRangeBounceV1StepResult step)
        => new()
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Reason = step.RejectionReason ?? "MeanReversionRangeBounceV1:Rejected",
            ExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            ExpectedTargetSource = step.Diagnostics.TargetModelName,
            SignalReason = "Range bounce candidate rejected.",
            RejectionLayer = "MeanReversionRangeBounceV1",
            TargetModelName = step.Diagnostics.TargetModelName,
            LockDistancePercent = step.Diagnostics.ExpectedMovePercent
        };

    private sealed record TargetForwardAnalytics
    {
        public decimal? ForwardMfe15Percent { get; init; }
        public decimal? ForwardMfe30Percent { get; init; }
        public decimal? ForwardMfe60Percent { get; init; }
        public decimal? ForwardMae15Percent { get; init; }
        public decimal? ForwardMae30Percent { get; init; }
        public decimal? ForwardMae60Percent { get; init; }
        public bool TargetReachableWithin60m { get; init; }
        public int? TimeToTargetMinutes { get; init; }
    }
}
