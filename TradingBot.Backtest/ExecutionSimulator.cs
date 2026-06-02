using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public sealed class ExecutionSimulator(ExecutionCostSettings costSettings)
{
    private readonly Dictionary<TradingSymbol, OpenPosition> _openPositions = new();
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
        decimal? estimatedNetMovePercent = null)
    {
        if (!_openPositions.TryGetValue(symbol, out var openPosition))
        {
            if (signal.Signal != TradeSignal.Buy)
                return;

            var entryFill = ApplyEntrySlippage(snapshot.CurrentPrice);
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
                null,
                null,
                false,
                null);
            return;
        }

        TrackExcursions(openPosition, snapshot);

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
            DurationMinutes = Math.Max(0m, (decimal)(exitTimeUtc - position.EntryTimeUtc).TotalMinutes)
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

        _openPositions[position.Symbol] = position with
        {
            MaxFavorablePrice = maxFavorable,
            MaxAdversePrice = maxAdverse,
            TouchedExpectedTarget = touchedExpectedTarget,
            FirstExpectedTargetTouchTimeUtc = firstTouchTimeUtc
        };
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
        decimal? MaxFavorablePrice,
        decimal? MaxAdversePrice,
        bool TouchedExpectedTarget,
        DateTime? FirstExpectedTargetTouchTimeUtc);
}
