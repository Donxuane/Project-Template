using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed class ExecutionSimulator(ExecutionCostSettings costSettings, BacktestExitPolicySettings exitPolicySettings)
{
    private readonly Dictionary<TradingSymbol, OpenPosition> _openPositions = new();
    private readonly decimal _roundTripCostPercent = Math.Max(0m, (costSettings.FeeRatePercent * 2m) + costSettings.SpreadPercent + (costSettings.SlippagePercent * 2m));

    public ExecutionSimulator(ExecutionCostSettings costSettings)
        : this(costSettings, new BacktestExitPolicySettings(
            ExitPolicyName: "OppositeSignalOnly",
            ProfitLockThresholdPercent: null,
            EnableBreakevenAfterNetProfit: false,
            BreakevenActivationNetProfitPercent: 0.12m,
            EnableTrailingAfterNetProfit: false,
            TrailingActivationNetProfitPercent: 0.20m,
            TrailingStopPercent: 0.10m))
    {
    }
    public bool HasOpenPosition(TradingSymbol symbol) => _openPositions.ContainsKey(symbol);
    public int OpenPositionCount => _openPositions.Count;

    public void OnSignal(
        string interval,
        TradingSymbol symbol,
        decimal quantity,
        StrategySignalResult signal,
        MarketSnapshot snapshot,
        string profileName,
        string symbolsText,
        List<SimulatedTrade> destination,
        bool wasGuarded = true,
        decimal? estimatedRoundTripCostPercent = null,
        decimal? estimatedNetMovePercent = null,
        PullbackV2Diagnostics? pullbackV2Diagnostics = null,
        BnbPullbackGuardDiagnostics? bnbGuardDiagnostics = null,
        BnbRetestContinuationDiagnostics? retestDiagnostics = null)
    {
        if (!_openPositions.TryGetValue(symbol, out var openPosition))
        {
            if (signal.Signal != TradeSignal.Buy)
                return;

            var entryFill = ApplyEntrySlippage(snapshot.CurrentPrice);
            var (capture90Price, capture95Price, capture98Price) = ResolveProfitCapturePrices(entryFill, signal.ExpectedTargetPrice);
            var structuralStopPrice = ResolveStructuralStopPrice(entryFill, signal, exitPolicySettings);
            _openPositions[symbol] = new OpenPosition(
                interval,
                profileName,
                symbolsText,
                symbol,
                quantity,
                snapshot.TimestampUtc,
                entryFill,
                signal.Reason,
                signal.ExpectedMovePercent,
                signal.ExpectedTargetPrice,
                signal.ExpectedTargetSource,
                signal.DistanceToInvalidationPercent,
                signal.ConsecutiveBullishTrendCandles,
                signal.CurrentCloseAboveRecentHigh,
                signal.PreviousCandleBearish,
                signal.EntryNearRecentHigh,
                signal.ShortMaSlopePercent,
                signal.TrendStrengthPercent,
                signal.ProjectionMode,
                signal.ProjectedExtension,
                signal.VolatilityRegime,
                wasGuarded,
                estimatedRoundTripCostPercent,
                estimatedNetMovePercent,
                pullbackV2Diagnostics?.PullbackSetupDetected,
                pullbackV2Diagnostics?.PullbackReclaimConfirmed,
                pullbackV2Diagnostics?.PullbackFollowThroughConfirmed,
                pullbackV2Diagnostics?.PullbackRejectedReason,
                pullbackV2Diagnostics?.ReclaimReferencePrice,
                pullbackV2Diagnostics?.FollowThroughReferencePrice,
                pullbackV2Diagnostics?.CandlesWaitedAfterReclaim,
                pullbackV2Diagnostics?.ResidualExpectedMovePercent,
                pullbackV2Diagnostics?.ResidualEstimatedNetMovePercent,
                pullbackV2Diagnostics?.ResidualRewardRisk,
                pullbackV2Diagnostics?.DistanceFromEntryToExpectedTargetPercent,
                null,
                null,
                false,
                null,
                false,
                false,
                false,
                capture90Price,
                capture95Price,
                capture98Price,
                false,
                false,
                false,
                null,
                null,
                structuralStopPrice,
                exitPolicySettings.MaxHoldMinutes,
                exitPolicySettings.ExitPolicyName,
                exitPolicySettings.ProfitLockThresholdPercent,
                bnbGuardDiagnostics,
                retestDiagnostics);
            return;
        }

        TrackExcursions(openPosition, snapshot);
        openPosition = _openPositions[symbol];
        if (TryApplyExitPolicy(openPosition, snapshot, destination))
        {
            _openPositions.Remove(symbol);
            return;
        }

        if (signal.Signal != TradeSignal.Sell)
            return;

        destination.Add(ClosePosition(openPosition, snapshot.TimestampUtc, snapshot.CurrentPrice, "OppositeSignal"));
        _openPositions.Remove(symbol);
    }

    public void ForceClose(
        TradingSymbol symbol,
        MarketSnapshot snapshot,
        string exitReason,
        string profileName,
        string symbolsText,
        List<SimulatedTrade> destination)
    {
        if (!_openPositions.TryGetValue(symbol, out var openPosition))
            return;

        destination.Add(ClosePosition(openPosition, snapshot.TimestampUtc, snapshot.CurrentPrice, exitReason));
        _openPositions.Remove(symbol);
    }

    private SimulatedTrade ClosePosition(OpenPosition position, DateTime exitTimeUtc, decimal rawExitPrice, string exitReason)
    {
        var exitFill = ApplyExitSlippage(rawExitPrice);
        var gross = (exitFill - position.EntryPrice) * position.Quantity;
        var entryNotional = position.EntryPrice * position.Quantity;
        var exitNotional = exitFill * position.Quantity;
        var feeEstimate = (entryNotional + exitNotional) * (costSettings.FeeRatePercent / 100m);
        var spreadEstimate = (entryNotional + exitNotional) * (costSettings.SpreadPercent / 100m) / 2m;
        var totalCostEstimate = feeEstimate + spreadEstimate;
        var net = gross - totalCostEstimate;
        decimal? counterfactualNet = null;
        decimal? counterfactualDelta = null;
        if (position.TouchedExpectedTarget && position.ExpectedTargetPrice.HasValue)
        {
            counterfactualNet = CalculateNetPnlForExitPrice(position, position.ExpectedTargetPrice.Value);
            counterfactualDelta = counterfactualNet.Value - net;
        }
        var capture90Net = position.ProfitCapture90Touched && position.ProfitCapture90Price.HasValue
            ? CalculateNetPnlForExitPrice(position, position.ProfitCapture90Price.Value)
            : (decimal?)null;
        var capture95Net = position.ProfitCapture95Touched && position.ProfitCapture95Price.HasValue
            ? CalculateNetPnlForExitPrice(position, position.ProfitCapture95Price.Value)
            : (decimal?)null;
        var capture98Net = position.ProfitCapture98Touched && position.ProfitCapture98Price.HasValue
            ? CalculateNetPnlForExitPrice(position, position.ProfitCapture98Price.Value)
            : (decimal?)null;
        if (capture90Net <= 0m) capture90Net = null;
        if (capture95Net <= 0m) capture95Net = null;
        if (capture98Net <= 0m) capture98Net = null;
        var bestCaptureNet = capture98Net ?? capture95Net ?? capture90Net;
        var captureDeltaVsOpposite = bestCaptureNet.HasValue ? bestCaptureNet.Value - net : (decimal?)null;

        decimal? rr = position.DistanceToInvalidationPercent is > 0m && position.ExpectedMovePercent is > 0m
            ? position.ExpectedMovePercent.Value / position.DistanceToInvalidationPercent.Value
            : null;

        return new SimulatedTrade
        {
            ProfileName = position.ProfileName,
            Interval = position.Interval,
            Symbols = position.Symbols,
            Symbol = position.Symbol,
            EntryTimeUtc = position.EntryTimeUtc,
            EntryPrice = position.EntryPrice,
            ExitTimeUtc = exitTimeUtc,
            ExitPrice = exitFill,
            Quantity = position.Quantity,
            GrossPnlQuote = gross,
            NetPnlQuote = net,
            FeeAndSpreadEstimateQuote = totalCostEstimate,
            EntryReason = position.EntryReason,
            ExitReason = exitReason,
            ExitPolicyName = position.ExitPolicyName,
            ProfitLockThresholdPercent = position.ProfitLockThresholdPercent,
            ExpectedMovePercent = position.ExpectedMovePercent,
            ExpectedTargetPrice = position.ExpectedTargetPrice,
            ExpectedTargetSource = position.ExpectedTargetSource,
            RewardRisk = rr,
            ConsecutiveBullishTrendCandles = position.ConsecutiveBullishTrendCandles,
            CurrentCloseAboveRecentHigh = position.CurrentCloseAboveRecentHigh,
            DistanceToInvalidationPercent = position.DistanceToInvalidationPercent,
            PreviousCandleBearish = position.PreviousCandleBearish,
            EntryNearRecentHigh = position.EntryNearRecentHigh,
            ShortMaSlopePercent = position.ShortMaSlopePercent,
            TrendStrengthPercent = position.TrendStrengthPercent,
            ProjectionMode = position.ProjectionMode,
            ProjectedExtension = position.ProjectedExtension,
            WasGuarded = position.WasGuarded,
            EstimatedRoundTripCostPercent = position.EstimatedRoundTripCostPercent,
            EstimatedNetMovePercent = position.EstimatedNetMovePercent,
            MaxFavorablePrice = position.MaxFavorablePrice,
            MaxAdversePrice = position.MaxAdversePrice,
            MfePercent = position.MaxFavorablePrice.HasValue
                ? (position.MaxFavorablePrice.Value - position.EntryPrice) * 100m / position.EntryPrice
                : null,
            MaePercent = position.MaxAdversePrice.HasValue
                ? (position.MaxAdversePrice.Value - position.EntryPrice) * 100m / position.EntryPrice
                : null,
            TouchedExpectedTarget = position.TouchedExpectedTarget,
            FirstExpectedTargetTouchTimeUtc = position.FirstExpectedTargetTouchTimeUtc,
            CounterfactualExitAtExpectedTargetNetPnlQuote = counterfactualNet,
            CounterfactualDeltaVsActualNetPnlQuote = counterfactualDelta,
            VolatilityRegime = position.VolatilityRegime,
            PullbackSetupDetected = position.PullbackSetupDetected,
            PullbackReclaimConfirmed = position.PullbackReclaimConfirmed,
            PullbackFollowThroughConfirmed = position.PullbackFollowThroughConfirmed,
            PullbackRejectedReason = position.PullbackRejectedReason,
            ReclaimReferencePrice = position.ReclaimReferencePrice,
            FollowThroughReferencePrice = position.FollowThroughReferencePrice,
            CandlesWaitedAfterReclaim = position.CandlesWaitedAfterReclaim,
            ResidualExpectedMovePercent = position.ResidualExpectedMovePercent,
            ResidualEstimatedNetMovePercent = position.ResidualEstimatedNetMovePercent,
            ResidualRewardRisk = position.ResidualRewardRisk,
            DistanceFromEntryToExpectedTargetPercent = position.DistanceFromEntryToExpectedTargetPercent,
            ProfitCapture90Touched = position.ProfitCapture90Touched,
            ProfitCapture95Touched = position.ProfitCapture95Touched,
            ProfitCapture98Touched = position.ProfitCapture98Touched,
            ProfitCapture90CounterfactualNetPnlQuote = capture90Net,
            ProfitCapture95CounterfactualNetPnlQuote = capture95Net,
            ProfitCapture98CounterfactualNetPnlQuote = capture98Net,
            ProfitCaptureDeltaVsOppositeSignalExitQuote = captureDeltaVsOpposite,
            GivebackFromMfePercent = position.MaxFavorablePrice.HasValue
                ? Math.Max(0m, ((position.MaxFavorablePrice.Value - position.EntryPrice) * 100m / position.EntryPrice)
                    - ((exitFill - position.EntryPrice) * 100m / position.EntryPrice))
                : null,
            CapturedMfePercent = position.MaxFavorablePrice.HasValue
                                 && ((position.MaxFavorablePrice.Value - position.EntryPrice) * 100m / position.EntryPrice) > 0m
                ? (((exitFill - position.EntryPrice) * 100m / position.EntryPrice)
                    / ((position.MaxFavorablePrice.Value - position.EntryPrice) * 100m / position.EntryPrice)) * 100m
                : null,
            DurationMinutes = Math.Max(0m, (decimal)(exitTimeUtc - position.EntryTimeUtc).TotalMinutes),
            BnbPullbackGuardEnabled = position.BnbGuardDiagnostics?.BnbPullbackGuardEnabled ?? false,
            BnbPullbackGuardRejected = position.BnbGuardDiagnostics?.BnbPullbackGuardRejected ?? false,
            BnbPullbackGuardRejectedReason = position.BnbGuardDiagnostics?.BnbPullbackGuardRejectedReason,
            LockDistancePercent = position.BnbGuardDiagnostics?.LockDistancePercent,
            MaxAllowedLockDistancePercent = position.BnbGuardDiagnostics?.MaxAllowedLockDistancePercent,
            LockReachabilityRejected = position.BnbGuardDiagnostics?.LockReachabilityRejected ?? false,
            ExpectedMoveCapRejected = position.BnbGuardDiagnostics?.ExpectedMoveCapRejected ?? false,
            DistanceToInvalidationCapRejected = position.BnbGuardDiagnostics?.DistanceToInvalidationCapRejected ?? false,
            TrendStrengthCapRejected = position.BnbGuardDiagnostics?.TrendStrengthCapRejected ?? false,
            ResidualExpectedMoveCapRejected = position.BnbGuardDiagnostics?.ResidualExpectedMoveCapRejected ?? false,
            ResidualRewardRiskCapRejected = position.BnbGuardDiagnostics?.ResidualRewardRiskCapRejected ?? false,
            ConsecutiveBullishCandlesAtEntry = position.BnbGuardDiagnostics?.ConsecutiveBullishCandlesAtEntry ?? position.ConsecutiveBullishTrendCandles,
            RetestContinuationEnabled = position.RetestDiagnostics?.RetestContinuationEnabled ?? false,
            RetestContinuationRejected = position.RetestDiagnostics?.RetestContinuationRejected ?? false,
            RetestContinuationRejectedReason = position.RetestDiagnostics?.RetestContinuationRejectedReason,
            RawExpectedMovePercent = position.RetestDiagnostics?.RawExpectedMovePercent,
            CappedExpectedMovePercent = position.RetestDiagnostics?.CappedExpectedMovePercent,
            RawExpectedTargetPrice = position.RetestDiagnostics?.RawExpectedTargetPrice,
            CappedExpectedTargetPrice = position.RetestDiagnostics?.CappedExpectedTargetPrice,
            TargetModelName = position.RetestDiagnostics?.TargetModelName,
            TargetWasCapped = position.RetestDiagnostics?.TargetWasCapped ?? false,
            CapReason = position.RetestDiagnostics?.CapReason,
            ExpectedMoveToRecentMfeRatio = position.RetestDiagnostics?.ExpectedMoveToRecentMfeRatio,
            HalfLockReachedBeforeExit = position.HalfLockReached
        };
    }

    private void TrackExcursions(OpenPosition position, MarketSnapshot snapshot)
    {
        // Entry is close-based; ignore the entry candle to avoid lookahead.
        if (snapshot.TimestampUtc <= position.EntryTimeUtc)
            return;

        var latestHigh = snapshot.HighPrices.Count > 0 ? snapshot.HighPrices[^1] : snapshot.CurrentPrice;
        var latestLow = snapshot.LowPrices.Count > 0 ? snapshot.LowPrices[^1] : snapshot.CurrentPrice;

        var maxFavorable = position.MaxFavorablePrice.HasValue
            ? Math.Max(position.MaxFavorablePrice.Value, latestHigh)
            : latestHigh;
        var maxAdverse = position.MaxAdversePrice.HasValue
            ? Math.Min(position.MaxAdversePrice.Value, latestLow)
            : latestLow;

        var touchedExpectedTarget = position.TouchedExpectedTarget;
        var firstTouchTimeUtc = position.FirstExpectedTargetTouchTimeUtc;
        if (!touchedExpectedTarget
            && position.ExpectedTargetPrice.HasValue
            && latestHigh >= position.ExpectedTargetPrice.Value)
        {
            touchedExpectedTarget = true;
            firstTouchTimeUtc = snapshot.TimestampUtc;
        }
        var capture90Touched = position.ProfitCapture90Touched;
        var capture95Touched = position.ProfitCapture95Touched;
        var capture98Touched = position.ProfitCapture98Touched;
        var halfLockReached = position.HalfLockReached;
        if (position.ExpectedTargetPrice.HasValue)
        {
            var target = position.ExpectedTargetPrice.Value;
            var range = target - position.EntryPrice;
            if (range > 0m)
            {
                var p90 = position.EntryPrice + (range * 0.90m);
                var p95 = position.EntryPrice + (range * 0.95m);
                var p98 = position.EntryPrice + (range * 0.98m);
                var halfLock = position.EntryPrice + (range * 0.45m);
                capture90Touched = capture90Touched || latestHigh >= p90;
                capture95Touched = capture95Touched || latestHigh >= p95;
                capture98Touched = capture98Touched || latestHigh >= p98;
                halfLockReached = halfLockReached || latestHigh >= halfLock;
            }
        }

        _openPositions[position.Symbol] = position with
        {
            MaxFavorablePrice = maxFavorable,
            MaxAdversePrice = maxAdverse,
            TouchedExpectedTarget = touchedExpectedTarget,
            FirstExpectedTargetTouchTimeUtc = firstTouchTimeUtc,
            ProfitCapture90Touched = capture90Touched,
            ProfitCapture95Touched = capture95Touched,
            ProfitCapture98Touched = capture98Touched,
            HalfLockReached = halfLockReached
        };
    }

    private bool TryApplyExitPolicy(OpenPosition position, MarketSnapshot snapshot, List<SimulatedTrade> destination)
    {
        if (!position.ExpectedTargetPrice.HasValue || position.ExpectedTargetPrice.Value <= position.EntryPrice)
            return false;

        var latestHigh = snapshot.HighPrices.Count > 0 ? snapshot.HighPrices[^1] : snapshot.CurrentPrice;
        var latestLow = snapshot.LowPrices.Count > 0 ? snapshot.LowPrices[^1] : snapshot.CurrentPrice;
        var netMoveAtHighPercent = ((latestHigh - position.EntryPrice) * 100m / position.EntryPrice) - _roundTripCostPercent;

        if (exitPolicySettings.MaxHoldMinutes is > 0
            && snapshot.TimestampUtc > position.EntryTimeUtc
            && (snapshot.TimestampUtc - position.EntryTimeUtc).TotalMinutes >= exitPolicySettings.MaxHoldMinutes.Value)
        {
            if (exitPolicySettings.EnableFeeAwareTimeStopExit)
            {
                var netAtCurrent = CalculateNetPnlForExitPrice(position, snapshot.CurrentPrice);
                var exitFill = ApplyExitSlippage(snapshot.CurrentPrice);
                var grossAtCurrent = (exitFill - position.EntryPrice) * position.Quantity;
                if (grossAtCurrent > 0m && netAtCurrent <= 0m)
                {
                    destination.Add(ClosePosition(position, snapshot.TimestampUtc, position.EntryPrice, "FeeAwareBreakeven"));
                    return true;
                }
            }

            destination.Add(ClosePosition(position, snapshot.TimestampUtc, snapshot.CurrentPrice, "TimeStop"));
            return true;
        }

        if (position.StructuralStopPrice is > 0m && latestLow <= position.StructuralStopPrice.Value)
        {
            destination.Add(ClosePosition(position, snapshot.TimestampUtc, position.StructuralStopPrice.Value, "StopLoss"));
            return true;
        }

        if (exitPolicySettings.NoProgressExitMinutes is > 0
            && snapshot.TimestampUtc > position.EntryTimeUtc
            && !position.HalfLockReached
            && (snapshot.TimestampUtc - position.EntryTimeUtc).TotalMinutes >= exitPolicySettings.NoProgressExitMinutes.Value)
        {
            destination.Add(ClosePosition(position, snapshot.TimestampUtc, snapshot.CurrentPrice, "NoProgressExit"));
            return true;
        }

        if (position.HalfLockReached)
        {
            if (exitPolicySettings.EnableCostCoveredBreakevenExit)
            {
                var costCoverPrice = ComputeCostCoverExitPrice(position);
                if (latestLow <= costCoverPrice)
                {
                    destination.Add(ClosePosition(position, snapshot.TimestampUtc, costCoverPrice, "CostCoveredBreakeven"));
                    return true;
                }
            }
            else if (exitPolicySettings.EnableHalfLockBreakevenExit
                     && latestLow <= position.EntryPrice)
            {
                destination.Add(ClosePosition(position, snapshot.TimestampUtc, position.EntryPrice, "HalfLockBreakeven"));
                return true;
            }
        }

        if (!position.BreakevenArmed
            && exitPolicySettings.EnableBreakevenAfterNetProfit
            && netMoveAtHighPercent >= exitPolicySettings.BreakevenActivationNetProfitPercent)
        {
            position = position with { BreakevenArmed = true };
            _openPositions[position.Symbol] = position;
        }

        if (!position.TrailingArmed
            && exitPolicySettings.EnableTrailingAfterNetProfit
            && netMoveAtHighPercent >= exitPolicySettings.TrailingActivationNetProfitPercent)
        {
            var stop = latestHigh * (1m - (exitPolicySettings.TrailingStopPercent / 100m));
            position = position with { TrailingArmed = true, TrailingStopPrice = stop };
            _openPositions[position.Symbol] = position;
        }
        else if (position.TrailingArmed && position.TrailingStopPrice.HasValue)
        {
            var updatedStop = Math.Max(position.TrailingStopPrice.Value, latestHigh * (1m - (exitPolicySettings.TrailingStopPercent / 100m)));
            if (updatedStop != position.TrailingStopPrice.Value)
            {
                position = position with { TrailingStopPrice = updatedStop };
                _openPositions[position.Symbol] = position;
            }
        }

        if (exitPolicySettings.ProfitLockThresholdPercent.HasValue)
        {
            var thresholdPrice = position.EntryPrice + ((position.ExpectedTargetPrice.Value - position.EntryPrice)
                * (exitPolicySettings.ProfitLockThresholdPercent.Value / 100m));
            if (latestHigh >= thresholdPrice)
            {
                destination.Add(ClosePosition(position, snapshot.TimestampUtc, thresholdPrice, "ProfitLock"));
                return true;
            }
        }

        if (position.BreakevenArmed && latestLow <= position.EntryPrice)
        {
            destination.Add(ClosePosition(position, snapshot.TimestampUtc, position.EntryPrice, "Breakeven"));
            return true;
        }

        if (position.TrailingArmed && position.TrailingStopPrice.HasValue && latestLow <= position.TrailingStopPrice.Value)
        {
            destination.Add(ClosePosition(position, snapshot.TimestampUtc, position.TrailingStopPrice.Value, "TrailingStop"));
            return true;
        }

        return false;
    }

    private decimal ComputeCostCoverExitPrice(OpenPosition position)
    {
        var requiredMovePercent = _roundTripCostPercent + exitPolicySettings.CostCoverMinNetPercent;
        return position.EntryPrice * (1m + (requiredMovePercent / 100m));
    }

    private decimal CalculateNetPnlForExitPrice(OpenPosition position, decimal rawExitPrice)
    {
        var exitFill = ApplyExitSlippage(rawExitPrice);
        var gross = (exitFill - position.EntryPrice) * position.Quantity;
        var entryNotional = position.EntryPrice * position.Quantity;
        var exitNotional = exitFill * position.Quantity;
        var feeEstimate = (entryNotional + exitNotional) * (costSettings.FeeRatePercent / 100m);
        var spreadEstimate = (entryNotional + exitNotional) * (costSettings.SpreadPercent / 100m) / 2m;
        return gross - (feeEstimate + spreadEstimate);
    }

    private decimal ApplyEntrySlippage(decimal price)
    {
        return price * (1m + (costSettings.SlippagePercent / 100m));
    }

    private decimal ApplyExitSlippage(decimal price)
    {
        return price * (1m - (costSettings.SlippagePercent / 100m));
    }

    private static decimal? ResolveStructuralStopPrice(
        decimal entryFill,
        StrategySignalResult signal,
        BacktestExitPolicySettings exitPolicySettings)
    {
        if (!exitPolicySettings.EnableStructuralStop)
            return null;

        if (!signal.BreakoutRangeHigh.HasValue || !signal.BreakoutRangeLow.HasValue)
            return null;

        var rangeHigh = signal.BreakoutRangeHigh.Value;
        var rangeLow = signal.BreakoutRangeLow.Value;
        if (rangeHigh <= 0m || rangeLow <= 0m || rangeHigh <= rangeLow)
            return null;

        return exitPolicySettings.StructuralStopMode switch
        {
            "RangeMidpoint" => (rangeHigh + rangeLow) / 2m,
            "BelowRangeHigh" => rangeHigh * (1m - 0.02m / 100m),
            _ => rangeLow
        };
    }

    private static (decimal? ProfitCapture90Price, decimal? ProfitCapture95Price, decimal? ProfitCapture98Price) ResolveProfitCapturePrices(
        decimal entryPrice,
        decimal? expectedTargetPrice)
    {
        if (!expectedTargetPrice.HasValue || expectedTargetPrice.Value <= entryPrice)
            return (null, null, null);

        var range = expectedTargetPrice.Value - entryPrice;
        return (
            entryPrice + (range * 0.90m),
            entryPrice + (range * 0.95m),
            entryPrice + (range * 0.98m));
    }

    private sealed record OpenPosition(
        string Interval,
        string ProfileName,
        string Symbols,
        TradingSymbol Symbol,
        decimal Quantity,
        DateTime EntryTimeUtc,
        decimal EntryPrice,
        string EntryReason,
        decimal? ExpectedMovePercent,
        decimal? ExpectedTargetPrice,
        string? ExpectedTargetSource,
        decimal? DistanceToInvalidationPercent,
        int? ConsecutiveBullishTrendCandles,
        bool? CurrentCloseAboveRecentHigh,
        bool? PreviousCandleBearish,
        bool? EntryNearRecentHigh,
        decimal? ShortMaSlopePercent,
        decimal? TrendStrengthPercent,
        string? ProjectionMode,
        decimal? ProjectedExtension,
        string? VolatilityRegime,
        bool WasGuarded,
        decimal? EstimatedRoundTripCostPercent,
        decimal? EstimatedNetMovePercent,
        bool? PullbackSetupDetected,
        bool? PullbackReclaimConfirmed,
        bool? PullbackFollowThroughConfirmed,
        string? PullbackRejectedReason,
        decimal? ReclaimReferencePrice,
        decimal? FollowThroughReferencePrice,
        int? CandlesWaitedAfterReclaim,
        decimal? ResidualExpectedMovePercent,
        decimal? ResidualEstimatedNetMovePercent,
        decimal? ResidualRewardRisk,
        decimal? DistanceFromEntryToExpectedTargetPercent,
        decimal? MaxFavorablePrice,
        decimal? MaxAdversePrice,
        bool TouchedExpectedTarget,
        DateTime? FirstExpectedTargetTouchTimeUtc,
        bool ProfitCapture90Touched,
        bool ProfitCapture95Touched,
        bool ProfitCapture98Touched,
        decimal? ProfitCapture90Price,
        decimal? ProfitCapture95Price,
        decimal? ProfitCapture98Price,
        bool HalfLockReached,
        bool BreakevenArmed,
        bool TrailingArmed,
        decimal? TrailingStopPrice,
        decimal? BreakevenPrice,
        decimal? StructuralStopPrice,
        int? MaxHoldMinutes,
        string ExitPolicyName,
        decimal? ProfitLockThresholdPercent,
        BnbPullbackGuardDiagnostics? BnbGuardDiagnostics,
        BnbRetestContinuationDiagnostics? RetestDiagnostics);
}
