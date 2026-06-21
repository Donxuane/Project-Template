using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed class PullbackFollowThroughV2Filter
{
    private const string StrategyRejectPrefix = "Strategy:";
    private readonly bool _enabled;
    private readonly bool _enableV3;
    private readonly decimal _roundTripCostPercent;
    private readonly decimal _minExpectedMovePercent;
    private readonly decimal _minNetProfitPercent;
    private readonly decimal _pullbackV3MinResidualExpectedMovePercent;
    private readonly decimal _pullbackV3MinResidualNetMovePercent;
    private readonly decimal _pullbackV3MinResidualRewardRisk;
    private readonly bool _pullbackV3RejectIfTargetAlreadyMostlyConsumed;
    private readonly decimal _pullbackV3MaxTargetConsumedPercent;
    private readonly Dictionary<TradingSymbol, PendingSetup> _pendingBySymbol = new();

    public PullbackFollowThroughV2Filter(IConfiguration configuration, ExecutionCostSettings costSettings)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:PullbackFollowThroughV2:Enabled") ?? false;
        _enableV3 = configuration.GetValue<bool?>("Backtest:PullbackFollowThroughV3:Enabled") ?? false;
        _roundTripCostPercent = Math.Max(0m, (costSettings.FeeRatePercent * 2m) + costSettings.SpreadPercent + (costSettings.SlippagePercent * 2m));
        _minExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinExpectedMovePercent") ?? 0m);
        _minNetProfitPercent = Math.Max(0m, configuration.GetValue<decimal?>("Trading:MinNetProfitPercent") ?? 0m);
        _pullbackV3MinResidualExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("PullbackV3MinResidualExpectedMovePercent") ?? 0.35m);
        _pullbackV3MinResidualNetMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("PullbackV3MinResidualNetMovePercent") ?? 0.12m);
        _pullbackV3MinResidualRewardRisk = Math.Max(0m, configuration.GetValue<decimal?>("PullbackV3MinResidualRewardRisk") ?? 1.25m);
        _pullbackV3RejectIfTargetAlreadyMostlyConsumed = configuration.GetValue<bool?>("PullbackV3RejectIfTargetAlreadyMostlyConsumed") ?? true;
        _pullbackV3MaxTargetConsumedPercent = Math.Clamp(configuration.GetValue<decimal?>("PullbackV3MaxTargetConsumedPercent") ?? 55m, 0m, 100m);
    }

    public PullbackFollowThroughV2Decision Evaluate(
        TradingSymbol symbol,
        StrategySignalResult signal,
        MarketSnapshot snapshot,
        bool hasOpenPosition)
    {
        if (!_enabled && !_enableV3)
            return PullbackFollowThroughV2Decision.None;

        if (hasOpenPosition)
        {
            _pendingBySymbol.Remove(symbol);
            return PullbackFollowThroughV2Decision.None;
        }

        if (_pendingBySymbol.TryGetValue(symbol, out var pending)
            && snapshot.TimestampUtc > pending.ReclaimTimeUtc)
        {
            _pendingBySymbol.Remove(symbol);
            var latestClose = ResolveLatestConfirmedClose(snapshot);
            var followThroughReference = Math.Max(pending.ReclaimClose, pending.ReclaimHigh);
            var followThroughConfirmed = latestClose > followThroughReference;
            if (!followThroughConfirmed)
            {
                return PullbackFollowThroughV2Decision.Rejected(
                    $"{StrategyRejectPrefix}PullbackFollowThroughNotConfirmed",
                    new PullbackV2Diagnostics(
                        PullbackSetupDetected: true,
                        PullbackReclaimConfirmed: true,
                        PullbackFollowThroughConfirmed: false,
                        PullbackRejectedReason: "Pullback follow-through confirmation failed on the next confirmed candle.",
                        ReclaimReferencePrice: pending.ReclaimHigh,
                        FollowThroughReferencePrice: followThroughReference,
                        CandlesWaitedAfterReclaim: 1));
            }

            var currentExpectedMove = pending.Signal.ExpectedTargetPrice.HasValue
                ? ((pending.Signal.ExpectedTargetPrice.Value - snapshot.CurrentPrice) * 100m / snapshot.CurrentPrice)
                : (decimal?)null;
            var residualExpectedMovePercent = currentExpectedMove;
            var residualEstimatedNetMovePercent = residualExpectedMovePercent.HasValue
                ? residualExpectedMovePercent.Value - _roundTripCostPercent
                : (decimal?)null;
            var residualRewardRisk = pending.Signal.DistanceToInvalidationPercent is > 0m && residualExpectedMovePercent.HasValue
                ? residualExpectedMovePercent.Value / pending.Signal.DistanceToInvalidationPercent.Value
                : (decimal?)null;
            var distanceFromEntryToExpectedTargetPercent = residualExpectedMovePercent;
            var originalExpectedMovePercent = pending.Signal.ExpectedMovePercent
                                              ?? (pending.Signal.ExpectedTargetPrice.HasValue
                                                  ? ((pending.Signal.ExpectedTargetPrice.Value - pending.SetupPrice) * 100m / pending.SetupPrice)
                                                  : (decimal?)null);
            var targetConsumedPercent = originalExpectedMovePercent is > 0m && residualExpectedMovePercent.HasValue
                ? Math.Clamp((1m - (residualExpectedMovePercent.Value / originalExpectedMovePercent.Value)) * 100m, 0m, 100m)
                : (decimal?)null;

            if (_enableV3 && residualExpectedMovePercent.HasValue && residualExpectedMovePercent.Value < _pullbackV3MinResidualExpectedMovePercent)
            {
                return PullbackFollowThroughV2Decision.Rejected(
                    $"{StrategyRejectPrefix}V3ResidualExpectedMoveBelowMinimum",
                    new PullbackV2Diagnostics(
                        true, true, true,
                        "V3 rejected setup because residual expected move is below minimum threshold.",
                        pending.ReclaimHigh, followThroughReference, 1,
                        residualExpectedMovePercent,
                        residualEstimatedNetMovePercent,
                        residualRewardRisk,
                        distanceFromEntryToExpectedTargetPercent));
            }

            if (_enableV3 && residualEstimatedNetMovePercent.HasValue && residualEstimatedNetMovePercent.Value < _pullbackV3MinResidualNetMovePercent)
            {
                return PullbackFollowThroughV2Decision.Rejected(
                    $"{StrategyRejectPrefix}V3ResidualNetMoveBelowMinimum",
                    new PullbackV2Diagnostics(
                        true, true, true,
                        "V3 rejected setup because residual estimated net move is below minimum threshold.",
                        pending.ReclaimHigh, followThroughReference, 1,
                        residualExpectedMovePercent,
                        residualEstimatedNetMovePercent,
                        residualRewardRisk,
                        distanceFromEntryToExpectedTargetPercent));
            }

            if (_enableV3 && residualRewardRisk.HasValue && residualRewardRisk.Value < _pullbackV3MinResidualRewardRisk)
            {
                return PullbackFollowThroughV2Decision.Rejected(
                    $"{StrategyRejectPrefix}V3ResidualRewardRiskBelowMinimum",
                    new PullbackV2Diagnostics(
                        true, true, true,
                        "V3 rejected setup because residual reward:risk is below minimum threshold.",
                        pending.ReclaimHigh, followThroughReference, 1,
                        residualExpectedMovePercent,
                        residualEstimatedNetMovePercent,
                        residualRewardRisk,
                        distanceFromEntryToExpectedTargetPercent));
            }

            if (_enableV3 && _pullbackV3RejectIfTargetAlreadyMostlyConsumed
                          && targetConsumedPercent.HasValue
                          && targetConsumedPercent.Value > _pullbackV3MaxTargetConsumedPercent)
            {
                return PullbackFollowThroughV2Decision.Rejected(
                    $"{StrategyRejectPrefix}V3TargetAlreadyMostlyConsumed",
                    new PullbackV2Diagnostics(
                        true, true, true,
                        "V3 rejected setup because expected target is already mostly consumed before delayed entry.",
                        pending.ReclaimHigh, followThroughReference, 1,
                        residualExpectedMovePercent,
                        residualEstimatedNetMovePercent,
                        residualRewardRisk,
                        distanceFromEntryToExpectedTargetPercent));
            }

            if (currentExpectedMove.HasValue && currentExpectedMove.Value < _minExpectedMovePercent)
            {
                return PullbackFollowThroughV2Decision.Rejected(
                    $"{StrategyRejectPrefix}TooCloseToExpectedTarget",
                    new PullbackV2Diagnostics(
                        true, true, true,
                        "Expected target is too close to current price after follow-through confirmation.",
                        pending.ReclaimHigh, followThroughReference, 1,
                        residualExpectedMovePercent,
                        residualEstimatedNetMovePercent,
                        residualRewardRisk,
                        distanceFromEntryToExpectedTargetPercent));
            }

            var estimatedNetMove = currentExpectedMove.HasValue ? currentExpectedMove.Value - _roundTripCostPercent : (decimal?)null;
            if (estimatedNetMove.HasValue && estimatedNetMove.Value < _minNetProfitPercent)
            {
                return PullbackFollowThroughV2Decision.Rejected(
                    $"{StrategyRejectPrefix}ExpectedNetMoveBelowMinimum",
                    new PullbackV2Diagnostics(
                        true, true, true,
                        "Expected net move after fee/spread is below the configured minimum net threshold.",
                        pending.ReclaimHigh, followThroughReference, 1,
                        residualExpectedMovePercent,
                        residualEstimatedNetMovePercent,
                        residualRewardRisk,
                        distanceFromEntryToExpectedTargetPercent));
            }

            var stagedSignal = new StrategySignalResult
            {
                StrategyName = pending.Signal.StrategyName,
                Signal = pending.Signal.Signal,
                Reason = $"{pending.Signal.Reason} [pullback-reclaim-followthrough-v2]",
                Confidence = pending.Signal.Confidence,
                TrendConfidenceScore = pending.Signal.TrendConfidenceScore,
                MarketConditionScore = pending.Signal.MarketConditionScore,
                VolatilityRegime = pending.Signal.VolatilityRegime,
                ExpectedTargetPrice = pending.Signal.ExpectedTargetPrice,
                ExpectedMovePercent = residualExpectedMovePercent ?? pending.Signal.ExpectedMovePercent,
                ExpectedTargetSource = pending.Signal.ExpectedTargetSource,
                BreakoutRangeHigh = pending.Signal.BreakoutRangeHigh,
                BreakoutRangeLow = pending.Signal.BreakoutRangeLow,
                BreakoutThresholdPrice = pending.Signal.BreakoutThresholdPrice,
                ExpectedTargetStructureExtensionUsed = pending.Signal.ExpectedTargetStructureExtensionUsed,
                ExpectedTargetAtrUsed = pending.Signal.ExpectedTargetAtrUsed,
                ConsecutiveBullishTrendCandles = pending.Signal.ConsecutiveBullishTrendCandles,
                EntryNearRecentHigh = pending.Signal.EntryNearRecentHigh,
                DistanceToRecentHighPercent = pending.Signal.DistanceToRecentHighPercent,
                DistanceToInvalidationPercent = pending.Signal.DistanceToInvalidationPercent,
                CurrentCloseAboveRecentHigh = pending.Signal.CurrentCloseAboveRecentHigh,
                PreviousCandleBearish = pending.Signal.PreviousCandleBearish,
                ShortMaSlopePercent = pending.Signal.ShortMaSlopePercent,
                TrendStrengthPercent = pending.Signal.TrendStrengthPercent,
                ProjectionMode = pending.Signal.ProjectionMode,
                ProjectedExtension = pending.Signal.ProjectedExtension,
                NormalTrendEntryRejectedReason = pending.Signal.NormalTrendEntryRejectedReason
            };
            return PullbackFollowThroughV2Decision.Execute(stagedSignal, new PullbackV2Diagnostics(
                true, true, true, null,
                pending.ReclaimHigh, followThroughReference, 1,
                residualExpectedMovePercent,
                residualEstimatedNetMovePercent,
                residualRewardRisk,
                distanceFromEntryToExpectedTargetPercent));
        }

        if (signal.Signal != TradeSignal.Buy)
            return PullbackFollowThroughV2Decision.None;

        var reason = signal.Reason ?? string.Empty;
        if (reason.Contains("low-volatility breakout", StringComparison.OrdinalIgnoreCase))
        {
            return PullbackFollowThroughV2Decision.Rejected(
                $"{StrategyRejectPrefix}NotNormalTrendPullbackContext",
                new PullbackV2Diagnostics(true, false, false, "Buy signal is not a normal-trend pullback setup.", null, null, 0));
        }

        if (!reason.Contains("pullback", StringComparison.OrdinalIgnoreCase))
        {
            return PullbackFollowThroughV2Decision.Rejected(
                $"{StrategyRejectPrefix}PullbackRetestNotDetected",
                new PullbackV2Diagnostics(true, false, false, "Pullback/retest was not detected for this buy setup.", null, null, 0));
        }

        if (!(signal.EntryNearRecentHigh == true || signal.PreviousCandleBearish == true))
        {
            return PullbackFollowThroughV2Decision.Rejected(
                $"{StrategyRejectPrefix}PullbackRetestNotDetected",
                new PullbackV2Diagnostics(true, false, false, "Signal did not satisfy pullback/retest prerequisites.", null, null, 0));
        }

        var confirmed = ResolveConfirmedSeries(snapshot);
        if (confirmed.ConfirmedCloses.Length < 2 || confirmed.ConfirmedHighs.Length < 2)
        {
            return PullbackFollowThroughV2Decision.Rejected(
                $"{StrategyRejectPrefix}InsufficientConfirmedCandles",
                new PullbackV2Diagnostics(true, false, false, "Insufficient confirmed candles to evaluate reclaim and follow-through.", null, null, 0));
        }

        var latestConfirmedClose = confirmed.ConfirmedCloses[^1];
        var previousConfirmedHigh = confirmed.ConfirmedHighs[^2];
        var reclaimConfirmed = latestConfirmedClose > previousConfirmedHigh;
        if (!reclaimConfirmed)
        {
            return PullbackFollowThroughV2Decision.Rejected(
                $"{StrategyRejectPrefix}PullbackReclaimNotConfirmed",
                new PullbackV2Diagnostics(
                    true,
                    false,
                    false,
                    "Pullback reclaim not confirmed because latest confirmed close did not exceed previous confirmed candle high.",
                    previousConfirmedHigh,
                    null,
                    0));
        }

        _pendingBySymbol[symbol] = new PendingSetup(
            signal,
            snapshot.TimestampUtc,
            latestConfirmedClose,
            previousConfirmedHigh,
            snapshot.CurrentPrice);
        return PullbackFollowThroughV2Decision.Pending(new PullbackV2Diagnostics(
            true,
            true,
            false,
            "Awaiting pullback follow-through confirmation on the next confirmed candle.",
            previousConfirmedHigh,
            latestConfirmedClose,
            0));
    }

    private static decimal ResolveLatestConfirmedClose(MarketSnapshot snapshot)
        => snapshot.LatestClosedCandleClosePrice ?? (snapshot.ClosePrices.Count > 0 ? snapshot.ClosePrices[^1] : snapshot.CurrentPrice);

    private static (decimal[] ConfirmedCloses, decimal[] ConfirmedHighs) ResolveConfirmedSeries(MarketSnapshot snapshot)
    {
        var closes = snapshot.ClosePrices;
        var highs = snapshot.HighPrices;
        var confirmedClosedCount = closes.Count;
        if (snapshot.LatestClosedCandleClosePrice.HasValue
            && closes.Count >= 2
            && closes[^1] != snapshot.LatestClosedCandleClosePrice.Value)
        {
            confirmedClosedCount = closes.Count - 1;
        }

        var confirmedCloses = closes.Take(Math.Max(0, confirmedClosedCount)).ToArray();
        var confirmedHighs = highs.Take(Math.Max(0, confirmedClosedCount)).ToArray();
        if (confirmedCloses.Length == 0)
            confirmedCloses = closes.ToArray();
        if (confirmedHighs.Length == 0)
            confirmedHighs = highs.ToArray();
        return (confirmedCloses, confirmedHighs);
    }

    private sealed record PendingSetup(
        StrategySignalResult Signal,
        DateTime ReclaimTimeUtc,
        decimal ReclaimClose,
        decimal ReclaimHigh,
        decimal SetupPrice);
}

public sealed record PullbackV2Diagnostics(
    bool PullbackSetupDetected,
    bool PullbackReclaimConfirmed,
    bool PullbackFollowThroughConfirmed,
    string? PullbackRejectedReason,
    decimal? ReclaimReferencePrice,
    decimal? FollowThroughReferencePrice,
    int CandlesWaitedAfterReclaim,
    decimal? ResidualExpectedMovePercent = null,
    decimal? ResidualEstimatedNetMovePercent = null,
    decimal? ResidualRewardRisk = null,
    decimal? DistanceFromEntryToExpectedTargetPercent = null);

public sealed record PullbackFollowThroughV2Decision
{
    public static PullbackFollowThroughV2Decision None { get; } = new();
    public bool IsPending { get; init; }
    public bool IsRejected { get; init; }
    public bool IsExecute { get; init; }
    public string? RejectionReason { get; init; }
    public StrategySignalResult? SignalToExecute { get; init; }
    public PullbackV2Diagnostics? Diagnostics { get; init; }

    public static PullbackFollowThroughV2Decision Pending(PullbackV2Diagnostics diagnostics) => new()
    {
        IsPending = true,
        Diagnostics = diagnostics
    };

    public static PullbackFollowThroughV2Decision Rejected(string reason, PullbackV2Diagnostics diagnostics) => new()
    {
        IsRejected = true,
        RejectionReason = reason,
        Diagnostics = diagnostics
    };

    public static PullbackFollowThroughV2Decision Execute(StrategySignalResult signal, PullbackV2Diagnostics diagnostics) => new()
    {
        IsExecute = true,
        SignalToExecute = signal,
        Diagnostics = diagnostics
    };
}
