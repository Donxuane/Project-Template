using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record HigherTimeframeMomentumPullbackV1CandidateRecord
{
    public string WindowLabel { get; init; } = string.Empty;
    public string Interval { get; init; } = "15m";
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public DateTime TimeUtc { get; init; }
    public bool Executed { get; init; }
    public string? RejectionReason { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal TrendStrengthPercent { get; init; }
    public decimal PullbackDepthPercent { get; init; }
    public decimal? DistanceToMaPercent { get; init; }
    public bool ReclaimConfirmed { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? RewardRisk { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ForwardMfe4hPercent { get; init; }
    public decimal? ForwardMfe8hPercent { get; init; }
    public decimal? ForwardMfe12hPercent { get; init; }
    public decimal? ForwardMae4hPercent { get; init; }
    public decimal? ForwardMae8hPercent { get; init; }
    public decimal? ForwardMae12hPercent { get; init; }
    public decimal? NetPnlQuote { get; init; }
    public decimal? GrossPnlQuote { get; init; }
    public decimal? FeeAndSpreadEstimateQuote { get; init; }
    public string? ExitReason { get; init; }
    public decimal DurationMinutes { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public bool? IsWinner { get; init; }
}

public sealed record HigherTimeframeMomentumPullbackV1SummaryRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string Interval { get; init; } = "15m";
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public int CandidateCount { get; init; }
    public int ExecutedCount { get; init; }
    public int BlockedCount { get; init; }
    public decimal EstimatedNetPnlQuote { get; init; }
    public int TradesCount { get; init; }
    public int NetWinnerCount { get; init; }
    public decimal? AvgExpectedMovePercent { get; init; }
    public string RepeatabilityVerdict { get; init; } = string.Empty;
}

public sealed record HigherTimeframeMomentumPullbackV1ExitBreakdownRow
{
    public string ExitReason { get; init; } = string.Empty;
    public int Count { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal? AvgDurationMinutes { get; init; }
}

public sealed record HigherTimeframeMomentumPullbackV1WindowRobustnessRow
{
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "15m";
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

public sealed record HigherTimeframeMomentumPullbackV1RunResult(
    IReadOnlyList<HigherTimeframeMomentumPullbackV1CandidateRecord> Candidates,
    IReadOnlyList<SimulatedTrade> Trades,
    IReadOnlyList<BlockedEntryRecord> BlockedEntries,
    IReadOnlyList<HigherTimeframeMomentumPullbackV1SummaryRow> Summaries,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    IReadOnlyList<HigherTimeframeMomentumPullbackV1ExitBreakdownRow> ExitBreakdown,
    IReadOnlyList<HigherTimeframeMomentumPullbackV1WindowRobustnessRow> WindowRobustness,
    int ProfileCount);

public sealed record HigherTimeframeMomentumPullbackV1ReplayContext(
    HigherTimeframeMomentumPullbackV1Model Model,
    ExecutionSimulator Simulator,
    ProfileSignalStats SignalStats,
    BacktestExitPolicySettings ExitPolicy,
    ExecutionCostSettings ExecutionCosts,
    IConfiguration Configuration);

internal static class HigherTimeframeMomentumPullbackV1Replay
{
    private static readonly StrategySignalResult HoldSignal = new() { Signal = TradeSignal.Hold };

    public static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string symbolsText,
        HigherTimeframeMomentumPullbackV1ReplayContext context,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        List<BlockedEntryRecord> blockedEntriesDestination,
        List<HigherTimeframeMomentumPullbackV1CandidateRecord> candidateDestination,
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
            if (step.Kind == HigherTimeframeMomentumPullbackV1StepKind.NoAction)
                continue;

            signalStats.RawBuySignals++;
            var entryPrice = snapshot.CurrentPrice;
            var forward = AnalyzeForward(sourceOneMinuteCandles, snapshot.TimestampUtc, entryPrice);

            if (step.Kind == HigherTimeframeMomentumPullbackV1StepKind.Blocked)
            {
                signalStats.IncrementStrategyRejected(step.RejectionReason ?? "HigherTimeframeMomentumPullbackV1:Rejected");
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
                interval, symbol, quantity, step.Signal!, snapshot,
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

    private static HtfForwardAnalytics AnalyzeForward(
        IReadOnlyList<KlineCandle>? sourceOneMinuteCandles,
        DateTime entryTimeUtc,
        decimal entryPrice)
    {
        if (sourceOneMinuteCandles is null || sourceOneMinuteCandles.Count == 0 || entryPrice <= 0m)
            return new HtfForwardAnalytics();

        return new HtfForwardAnalytics
        {
            ForwardMfe4hPercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMfePercent(sourceOneMinuteCandles, entryTimeUtc, entryPrice, 240),
            ForwardMfe8hPercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMfePercent(sourceOneMinuteCandles, entryTimeUtc, entryPrice, 480),
            ForwardMfe12hPercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMfePercent(sourceOneMinuteCandles, entryTimeUtc, entryPrice, 720),
            ForwardMae4hPercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMaePercent(sourceOneMinuteCandles, entryTimeUtc, entryPrice, 240),
            ForwardMae8hPercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMaePercent(sourceOneMinuteCandles, entryTimeUtc, entryPrice, 480),
            ForwardMae12hPercent = CandidateForwardOutcomeAnalyzer.ComputeForwardMaePercent(sourceOneMinuteCandles, entryTimeUtc, entryPrice, 720)
        };
    }

    private static void AttachTradeOutcomes(
        List<HigherTimeframeMomentumPullbackV1CandidateRecord> candidates,
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

    private static HigherTimeframeMomentumPullbackV1CandidateRecord BuildCandidateRecord(
        string windowLabel,
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        decimal entryPrice,
        HigherTimeframeMomentumPullbackV1Diagnostics diagnostics,
        HtfForwardAnalytics forward,
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
            TrendSlopePercent = diagnostics.TrendSlopePercent,
            TrendStrengthPercent = diagnostics.TrendStrengthPercent,
            PullbackDepthPercent = diagnostics.PullbackDepthPercent,
            DistanceToMaPercent = diagnostics.DistanceToMediumMaPercent,
            ReclaimConfirmed = diagnostics.ReclaimConfirmed,
            ExpectedMovePercent = diagnostics.ExpectedMovePercent,
            RequiredGrossMovePercent = diagnostics.RequiredGrossMovePercent,
            StopDistancePercent = diagnostics.StopDistancePercent,
            RewardRisk = diagnostics.RewardRisk,
            TargetModelName = diagnostics.TargetModelName,
            ForwardMfe4hPercent = forward.ForwardMfe4hPercent,
            ForwardMfe8hPercent = forward.ForwardMfe8hPercent,
            ForwardMfe12hPercent = forward.ForwardMfe12hPercent,
            ForwardMae4hPercent = forward.ForwardMae4hPercent,
            ForwardMae8hPercent = forward.ForwardMae8hPercent,
            ForwardMae12hPercent = forward.ForwardMae12hPercent
        };

    private static BlockedEntryRecord BuildBlockedEntry(
        string interval,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        DateTime timeUtc,
        HigherTimeframeMomentumPullbackV1StepResult step)
        => new()
        {
            Interval = interval,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            TimeUtc = timeUtc,
            Reason = step.RejectionReason ?? "HigherTimeframeMomentumPullbackV1:Rejected",
            ExpectedMovePercent = step.Diagnostics.ExpectedMovePercent,
            ExpectedTargetSource = step.Diagnostics.TargetModelName,
            SignalReason = "HTF momentum pullback candidate rejected.",
            RejectionLayer = "HigherTimeframeMomentumPullbackV1",
            TargetModelName = step.Diagnostics.TargetModelName,
            LockDistancePercent = step.Diagnostics.Lock90DistancePercent ?? step.Diagnostics.ExpectedMovePercent
        };

    private sealed record HtfForwardAnalytics
    {
        public decimal? ForwardMfe4hPercent { get; init; }
        public decimal? ForwardMfe8hPercent { get; init; }
        public decimal? ForwardMfe12hPercent { get; init; }
        public decimal? ForwardMae4hPercent { get; init; }
        public decimal? ForwardMae8hPercent { get; init; }
        public decimal? ForwardMae12hPercent { get; init; }
    }
}
