using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed record RegimeGatedLongEdgeV1TradeRecord
{
    public string WindowLabel { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "30m";
    public DateTime TimeUtc { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal ExitPrice { get; init; }
    public string ExitReason { get; init; } = string.Empty;
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public decimal TimeStopHours { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public string VolatilityRegime { get; init; } = string.Empty;
    public decimal? MarketWideReturnProxyPercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public decimal? MfePercent { get; init; }
    public decimal? MaePercent { get; init; }
    public decimal DurationMinutes { get; init; }
    public string EntryConfirmationMode { get; init; } = string.Empty;
}

public sealed record RegimeGatedLongEdgeV1BlockedSignalRecord
{
    public string WindowLabel { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string Symbols { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "30m";
    public DateTime TimeUtc { get; init; }
    public string RejectionReason { get; init; } = string.Empty;
    public string VolatilityRegime { get; init; } = string.Empty;
    public decimal? MarketWideReturnProxyPercent { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal DistanceFromRecentLowPercent { get; init; }
    public decimal DistanceFromRecentHighPercent { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal AtrPercent { get; init; }
    public decimal VolumeExpansionRatio { get; init; }
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public decimal TimeStopHours { get; init; }
    public string EntryConfirmationMode { get; init; } = string.Empty;
}

public sealed record RegimeGatedLongEdgeV1SummaryRow
{
    public string WindowLabel { get; init; } = string.Empty;
    public string RuleName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "30m";
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public decimal TimeStopHours { get; init; }
    public string EntryConfirmationMode { get; init; } = string.Empty;
    public int SignalCount { get; init; }
    public int BlockedSignalCount { get; init; }
    public int TradeCount { get; init; }
    public int NetWinnerCount { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public decimal? MedianNetPnlPerTrade { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record RegimeGatedLongEdgeV1RulePerformanceRow
{
    public string RuleName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "30m";
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public decimal TimeStopHours { get; init; }
    public string EntryConfirmationMode { get; init; } = string.Empty;
    public int TradeCount { get; init; }
    public decimal NetPnlQuote { get; init; }
    public decimal GrossPnlQuote { get; init; }
    public decimal? AvgNetPnlPerTrade { get; init; }
    public decimal StopLossRate { get; init; }
    public decimal ProfitTargetRate { get; init; }
    public decimal TimeStopRate { get; init; }
    public string Verdict { get; init; } = string.Empty;
}

public sealed record RegimeGatedLongEdgeV1WindowRobustnessRow
{
    public string RuleName { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public TradingSymbol Symbol { get; init; }
    public string Interval { get; init; } = "30m";
    public decimal TargetPercent { get; init; }
    public decimal StopPercent { get; init; }
    public decimal TimeStopHours { get; init; }
    public string EntryConfirmationMode { get; init; } = string.Empty;
    public int Window30dTrades { get; init; }
    public int Window60dTrades { get; init; }
    public int Window90dTrades { get; init; }
    public decimal Window30dNetPnl { get; init; }
    public decimal Window60dNetPnl { get; init; }
    public decimal Window90dNetPnl { get; init; }
    public string RobustnessVerdict { get; init; } = string.Empty;
}

public sealed record RegimeGatedLongEdgeV1RunResult(
    IReadOnlyList<RegimeGatedLongEdgeV1TradeRecord> Trades,
    IReadOnlyList<RegimeGatedLongEdgeV1BlockedSignalRecord> BlockedSignals,
    IReadOnlyList<RegimeGatedLongEdgeV1SummaryRow> Summaries,
    IReadOnlyList<RegimeGatedLongEdgeV1RulePerformanceRow> RulePerformance,
    IReadOnlyList<RegimeGatedLongEdgeV1WindowRobustnessRow> WindowRobustness,
    IReadOnlyList<ReachabilityResearchAnswer> ResearchAnswers,
    int ProfileCount);

public sealed record RegimeGatedLongEdgeV1ReplayContext(
    RegimeGatedLongEdgeV1Model Model,
    ExecutionSimulator Simulator,
    ProfileSignalStats SignalStats,
    BacktestExitPolicySettings ExitPolicy,
    ExecutionCostSettings ExecutionCosts,
    IConfiguration Configuration,
    BtcContextIndex? BtcContext,
    MarketWideContextIndex? MarketWideContext);

internal static class RegimeGatedLongEdgeV1Replay
{
    private static readonly StrategySignalResult HoldSignal = new() { Signal = TradeSignal.Hold };

    public static IReadOnlyList<SimulatedTrade> RunSymbolReplay(
        string interval,
        string profileName,
        string ruleName,
        string symbolsText,
        RegimeGatedLongEdgeV1ReplayContext context,
        TradingSymbol symbol,
        IReadOnlyList<KlineCandle> candles,
        decimal quantity,
        bool forceCloseAtEnd,
        List<RegimeGatedLongEdgeV1BlockedSignalRecord> blockedDestination,
        List<RegimeGatedLongEdgeV1TradeRecord> tradeRecordDestination,
        string windowLabel,
        CancellationToken cancellationToken)
    {
        var trades = new List<SimulatedTrade>();
        var model = context.Model;
        var simulator = context.Simulator;
        var signalStats = context.SignalStats;
        var timeStopHours = context.ExitPolicy.MaxHoldMinutes / 60m;
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

            var step = model.ProcessCandle(candles, i, interval, symbol, context.BtcContext, context.MarketWideContext);
            if (step.Kind == RegimeGatedLongEdgeV1StepKind.NoAction)
                continue;

            signalStats.RawBuySignals++;
            var diagnostics = step.Diagnostics with
            {
                RuleName = ruleName,
                TimeStopHours = timeStopHours
            };

            if (step.Kind == RegimeGatedLongEdgeV1StepKind.Blocked)
            {
                signalStats.IncrementStrategyRejected(step.RejectionReason ?? RegimeGatedLongEdgeV1Model.ConfirmationFailed);
                blockedDestination.Add(BuildBlockedSignal(
                    windowLabel, ruleName, profileName, symbolsText, symbol, interval,
                    snapshot.TimestampUtc, step.RejectionReason ?? RegimeGatedLongEdgeV1Model.ConfirmationFailed,
                    diagnostics, model.EntryConfirmationMode.ToString()));
                continue;
            }

            signalStats.ExecutedBuySignals++;
            var entrySnapshot = MarketSnapshotFactory.Build(candles, i);
            var roundTripCost = RangeExpansionCostModel.ComputeRoundTripCostPercent(context.ExecutionCosts);
            simulator.OnSignal(
                interval, symbol, quantity, step.Signal!, entrySnapshot,
                profileName, symbolsText, trades,
                wasGuarded: false,
                estimatedRoundTripCostPercent: roundTripCost,
                estimatedNetMovePercent: model.TargetPercent);

            tradeRecordDestination.Add(BuildPendingTradeRecord(
                windowLabel, ruleName, profileName, symbolsText, symbol, interval,
                entrySnapshot.TimestampUtc, entrySnapshot.CurrentPrice, diagnostics,
                model.EntryConfirmationMode.ToString()));
        }

        if (forceCloseAtEnd && lastSnapshot is not null)
            simulator.ForceClose(symbol, lastSnapshot, "EndOfData", profileName, symbolsText, trades);

        AttachTradeOutcomes(tradeRecordDestination, trades, profileName, interval, symbol, windowLabel);
        return trades;
    }

    private static void AttachTradeOutcomes(
        List<RegimeGatedLongEdgeV1TradeRecord> tradeRecords,
        IReadOnlyList<SimulatedTrade> trades,
        string profileName,
        string interval,
        TradingSymbol symbol,
        string windowLabel)
    {
        var tradeLookup = trades
            .GroupBy(t => $"{t.ProfileName}|{t.Interval}|{t.Symbol}|{t.EntryTimeUtc:O}", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < tradeRecords.Count; index++)
        {
            var record = tradeRecords[index];
            if (!string.Equals(record.ProfileName, profileName, StringComparison.OrdinalIgnoreCase)
                || record.Symbol != symbol
                || !string.Equals(record.Interval, interval, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(record.WindowLabel, windowLabel, StringComparison.OrdinalIgnoreCase)
                || record.ExitReason.Length > 0)
            {
                continue;
            }

            var key = $"{record.ProfileName}|{record.Interval}|{record.Symbol}|{record.TimeUtc:O}";
            if (!tradeLookup.TryGetValue(key, out var trade))
                continue;

            tradeRecords[index] = record with
            {
                ExitPrice = trade.ExitPrice,
                ExitReason = NormalizeExitReason(trade.ExitReason, trade.ProfitLockThresholdPercent),
                GrossPnlQuote = trade.GrossPnlQuote,
                NetPnlQuote = trade.NetPnlQuote,
                MfePercent = trade.MfePercent,
                MaePercent = trade.MaePercent,
                DurationMinutes = trade.DurationMinutes
            };
        }
    }

    private static RegimeGatedLongEdgeV1TradeRecord BuildPendingTradeRecord(
        string windowLabel,
        string ruleName,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        string interval,
        DateTime timeUtc,
        decimal entryPrice,
        RegimeGatedLongEdgeV1Diagnostics diagnostics,
        string entryConfirmationMode)
        => new()
        {
            WindowLabel = windowLabel,
            RuleName = ruleName,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            Interval = interval,
            TimeUtc = timeUtc,
            EntryPrice = entryPrice,
            TargetPercent = diagnostics.TargetPercent,
            StopPercent = diagnostics.StopPercent,
            TimeStopHours = diagnostics.TimeStopHours ?? 8m,
            VolatilityRegime = diagnostics.VolatilityRegime,
            MarketWideReturnProxyPercent = diagnostics.MarketWideReturnProxyPercent,
            RangeWidthPercent = diagnostics.RangeWidthPercent,
            DistanceFromRecentLowPercent = diagnostics.DistanceFromRecentLowPercent,
            DistanceFromRecentHighPercent = diagnostics.DistanceFromRecentHighPercent,
            TrendSlopePercent = diagnostics.TrendSlopePercent,
            AtrPercent = diagnostics.AtrPercent,
            VolumeExpansionRatio = diagnostics.VolumeExpansionRatio,
            EntryConfirmationMode = entryConfirmationMode
        };

    private static RegimeGatedLongEdgeV1BlockedSignalRecord BuildBlockedSignal(
        string windowLabel,
        string ruleName,
        string profileName,
        string symbolsText,
        TradingSymbol symbol,
        string interval,
        DateTime timeUtc,
        string rejectionReason,
        RegimeGatedLongEdgeV1Diagnostics diagnostics,
        string entryConfirmationMode)
        => new()
        {
            WindowLabel = windowLabel,
            RuleName = ruleName,
            ProfileName = profileName,
            Symbols = symbolsText,
            Symbol = symbol,
            Interval = interval,
            TimeUtc = timeUtc,
            RejectionReason = rejectionReason,
            VolatilityRegime = diagnostics.VolatilityRegime,
            MarketWideReturnProxyPercent = diagnostics.MarketWideReturnProxyPercent,
            RangeWidthPercent = diagnostics.RangeWidthPercent,
            DistanceFromRecentLowPercent = diagnostics.DistanceFromRecentLowPercent,
            DistanceFromRecentHighPercent = diagnostics.DistanceFromRecentHighPercent,
            TrendSlopePercent = diagnostics.TrendSlopePercent,
            AtrPercent = diagnostics.AtrPercent,
            VolumeExpansionRatio = diagnostics.VolumeExpansionRatio,
            TargetPercent = diagnostics.TargetPercent,
            StopPercent = diagnostics.StopPercent,
            TimeStopHours = diagnostics.TimeStopHours ?? 8m,
            EntryConfirmationMode = entryConfirmationMode
        };

    private static string? NormalizeExitReason(string? exitReason, decimal? profitLockThreshold)
    {
        if (string.Equals(exitReason, "ProfitLock", StringComparison.OrdinalIgnoreCase)
            && profitLockThreshold is >= 99m)
        {
            return "ProfitTarget";
        }

        return exitReason ?? "Unknown";
    }
}
