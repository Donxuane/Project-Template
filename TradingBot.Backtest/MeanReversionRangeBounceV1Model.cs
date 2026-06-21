using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum MeanReversionRangeBounceV1TargetModelName
{
    RangeMidpointTarget,
    RangeSixtyPercentTarget,
    RangeHighTarget,
    AtrLimitedTarget
}

public enum MeanReversionRangeBounceV1StopModeName
{
    RangeLowBuffer,
    RejectionCandleLow
}

public sealed class MeanReversionRangeBounceV1Model
{
    public const string GuardPrefix = "MeanReversionRangeBounceV1:";
    public const string InsufficientHistory = GuardPrefix + "InsufficientHistory";
    public const string RangeNotBounded = GuardPrefix + "RangeNotBounded";
    public const string TrendTooStrong = GuardPrefix + "TrendTooStrong";
    public const string PriceNotNearRangeLow = GuardPrefix + "PriceNotNearRangeLow";
    public const string RangeBreakdown = GuardPrefix + "RangeBreakdown";
    public const string NoRejectionCandle = GuardPrefix + "NoRejectionCandle";
    public const string CloseNotBackInsideRange = GuardPrefix + "CloseNotBackInsideRange";
    public const string ConfirmationFailed = GuardPrefix + "ConfirmationFailed";
    public const string TargetBelowRequiredGross = GuardPrefix + "TargetBelowRequiredGross";
    public const string TargetModelProducedNoMove = GuardPrefix + "TargetModelProducedNoMove";

    private readonly bool _enabled;
    private readonly int _rangeLookbackCandles;
    private readonly decimal _minRangeWidthPercent;
    private readonly decimal _maxRangeWidthPercent;
    private readonly decimal _maxTrendSlopePercent;
    private readonly decimal _maxDistanceFromRangeLowPercent;
    private readonly decimal _maxBreakdownBelowRangeLowPercent;
    private readonly decimal _minRejectionWickPercent;
    private readonly bool _requireConfirmation;
    private readonly bool _requireCloseAboveShortMa;
    private readonly int _shortMaPeriod;
    private readonly MeanReversionRangeBounceV1TargetModelName _targetModelName;
    private readonly decimal _rangeSixtyPercentFraction;
    private readonly decimal _atrLimitedMultiplier;
    private readonly MeanReversionRangeBounceV1StopModeName _stopMode;
    private readonly decimal _stopBufferBelowRangeLowPercent;
    private readonly decimal _requiredNetProfitPercent;
    private readonly int _atrPeriod;

    private PendingRangeBounceV1? _pending;

    public MeanReversionRangeBounceV1Model(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:MeanReversionRangeBounceV1:Enabled") ?? false;
        _rangeLookbackCandles = Math.Max(5, configuration.GetValue<int?>("Backtest:MeanReversionRangeBounceV1:RangeLookbackCandles") ?? 30);
        _minRangeWidthPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:MinRangeWidthPercent") ?? 0.25m);
        _maxRangeWidthPercent = Math.Max(_minRangeWidthPercent, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:MaxRangeWidthPercent") ?? 1.50m);
        _maxTrendSlopePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:MaxTrendSlopePercent") ?? 0.08m);
        _maxDistanceFromRangeLowPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:MaxDistanceFromRangeLowPercent") ?? 0.18m);
        _maxBreakdownBelowRangeLowPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:MaxBreakdownBelowRangeLowPercent") ?? 0.05m);
        _minRejectionWickPercent = Math.Clamp(configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:MinRejectionWickPercent") ?? 35m, 0m, 100m);
        _requireConfirmation = configuration.GetValue<bool?>("Backtest:MeanReversionRangeBounceV1:RequireConfirmation") ?? true;
        _requireCloseAboveShortMa = configuration.GetValue<bool?>("Backtest:MeanReversionRangeBounceV1:RequireCloseAboveShortMa") ?? false;
        _shortMaPeriod = Math.Max(2, configuration.GetValue<int?>("Backtest:MeanReversionRangeBounceV1:ShortMaPeriod") ?? 5);
        _targetModelName = ParseTargetModel(configuration.GetValue<string?>("Backtest:MeanReversionRangeBounceV1:TargetModelName"));
        _rangeSixtyPercentFraction = Math.Clamp(configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:RangeSixtyPercentFraction") ?? 0.60m, 0.1m, 1m);
        _atrLimitedMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:AtrLimitedMultiplier") ?? 1.50m);
        _stopMode = ParseStopMode(configuration.GetValue<string?>("Backtest:MeanReversionRangeBounceV1:StopMode"));
        _stopBufferBelowRangeLowPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:StopBufferBelowRangeLowPercent") ?? 0.08m);
        _requiredNetProfitPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:MeanReversionRangeBounceV1:RequiredNetProfitPercent") ?? 0.10m);
        _atrPeriod = Math.Max(2, configuration.GetValue<int?>("Backtest:MeanReversionRangeBounceV1:AtrPeriod") ?? 14);
    }

    public bool IsEnabled => _enabled;
    public int MinRequiredCandles => _rangeLookbackCandles + _shortMaPeriod + 2;
    public decimal RequiredNetProfitPercent => _requiredNetProfitPercent;

    public void Reset() => _pending = null;

    public MeanReversionRangeBounceV1StepResult ProcessCandle(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        TradingSymbol symbol,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        if (!_enabled || index < 0 || index >= candles.Count)
            return MeanReversionRangeBounceV1StepResult.NoAction();

        if (_pending is not null)
        {
            var confirmationResult = EvaluateConfirmation(candles, index, executionCosts);
            if (confirmationResult is not null)
                return confirmationResult;
        }

        if (_pending is null)
            TryDetectRejectionSetup(candles, index);

        if (!_requireConfirmation && _pending is not null)
        {
            var immediate = EvaluateEntry(candles, index, _pending!, executionCosts, usePendingCandle: true);
            _pending = null;
            if (immediate.Kind != MeanReversionRangeBounceV1StepKind.NoAction)
                return immediate;
        }

        return MeanReversionRangeBounceV1StepResult.NoAction();
    }

    private MeanReversionRangeBounceV1StepResult? EvaluateConfirmation(
        IReadOnlyList<KlineCandle> candles,
        int index,
        ExecutionCostSettings executionCosts)
    {
        var pending = _pending!;
        if (index != pending.RejectionIndex + 1)
        {
            _pending = null;
            return MeanReversionRangeBounceV1StepResult.Blocked(
                ConfirmationFailed,
                BuildDiagnostics(pending, candles[index]) with { RejectionReason = ConfirmationFailed });
        }

        var result = EvaluateEntry(candles, index, pending, executionCosts, usePendingCandle: false);
        _pending = null;
        return result;
    }

    private MeanReversionRangeBounceV1StepResult EvaluateEntry(
        IReadOnlyList<KlineCandle> candles,
        int index,
        PendingRangeBounceV1 pending,
        ExecutionCostSettings executionCosts,
        bool usePendingCandle)
    {
        var entryCandle = usePendingCandle ? candles[pending.RejectionIndex] : candles[index];
        var entryPrice = entryCandle.Close;
        var diagnostics = BuildDiagnostics(pending, entryCandle) with { FollowThroughConfirmed = !usePendingCandle };

        if (!usePendingCandle)
        {
            var rejectionCandle = candles[pending.RejectionIndex];
            var confirmed = entryCandle.Close > rejectionCandle.Close;
            if (_requireCloseAboveShortMa)
            {
                var shortMa = ComputeSma(candles, index, _shortMaPeriod);
                confirmed = confirmed || (shortMa > 0m && entryCandle.Close > shortMa);
            }

            diagnostics = diagnostics with { FollowThroughConfirmed = confirmed };
            if (!confirmed)
            {
                return MeanReversionRangeBounceV1StepResult.Blocked(
                    ConfirmationFailed,
                    diagnostics with { RejectionReason = ConfirmationFailed });
            }
        }

        var roundTrip = RangeExpansionCostModel.ComputeRoundTripCostPercent(executionCosts);
        var requiredGross = roundTrip + _requiredNetProfitPercent;
        var atrPercent = ComputeAtrPercent(candles, index);

        var (expectedMovePercent, targetPrice, targetModelName) =
            ResolveTargetMovePercent(pending, entryPrice, atrPercent);

        var stopPrice = ResolveStopPrice(pending);
        var stopDistancePercent = entryPrice > 0m && stopPrice > 0m
            ? Math.Round((entryPrice - stopPrice) / entryPrice * 100m, 6)
            : (decimal?)null;
        var rewardRisk = expectedMovePercent is > 0m && stopDistancePercent is > 0m
            ? Math.Round(expectedMovePercent.Value / stopDistancePercent.Value, 6)
            : (decimal?)null;

        diagnostics = diagnostics with
        {
            ExpectedMovePercent = expectedMovePercent,
            TargetModelName = targetModelName.ToString(),
            EstimatedRoundTripCostPercent = roundTrip,
            RequiredNetProfitPercent = _requiredNetProfitPercent,
            RequiredGrossMovePercent = requiredGross,
            StopDistancePercent = stopDistancePercent,
            RewardRisk = rewardRisk,
            AtrPercent = atrPercent
        };

        if (!expectedMovePercent.HasValue || expectedMovePercent.Value <= 0m)
        {
            return MeanReversionRangeBounceV1StepResult.Blocked(
                TargetModelProducedNoMove,
                diagnostics with { RejectionReason = TargetModelProducedNoMove });
        }

        if (expectedMovePercent.Value < requiredGross)
        {
            return MeanReversionRangeBounceV1StepResult.Blocked(
                TargetBelowRequiredGross,
                diagnostics with { RejectionReason = TargetBelowRequiredGross });
        }

        var signal = new StrategySignalResult
        {
            StrategyName = "MeanReversionRangeBounceV1",
            Signal = TradeSignal.Buy,
            Reason = "Range-bound bounce from support with cost-aware target.",
            Confidence = 0.78m,
            ExpectedTargetPrice = targetPrice,
            ExpectedMovePercent = expectedMovePercent,
            ExpectedTargetSource = "MeanReversionRangeBounceV1." + targetModelName,
            BreakoutRangeHigh = pending.RangeHigh,
            BreakoutRangeLow = stopPrice,
            BreakoutThresholdPrice = pending.RangeMidpoint,
            DistanceToInvalidationPercent = stopDistancePercent,
            ProjectionMode = targetModelName.ToString(),
            VolatilityRegime = pending.RangeWidthPercent <= _maxRangeWidthPercent / 2m ? "TightRange" : "NormalRange",
            ShortMaSlopePercent = pending.TrendSlopePercent
        };

        return MeanReversionRangeBounceV1StepResult.Entry(signal, diagnostics with { RejectionReason = null });
    }

    private void TryDetectRejectionSetup(IReadOnlyList<KlineCandle> candles, int index)
    {
        if (index < _rangeLookbackCandles)
            return;

        var rangeStart = index - _rangeLookbackCandles;
        var rangeEnd = index - 1;
        if (rangeStart < 0 || rangeEnd < rangeStart)
            return;

        if (!TryComputeRangeMetrics(candles, rangeStart, rangeEnd, out var rangeHigh, out var rangeLow, out var rangeWidthPercent, out var trendSlopePercent))
            return;

        if (rangeWidthPercent < _minRangeWidthPercent || rangeWidthPercent > _maxRangeWidthPercent)
            return;

        if (Math.Abs(trendSlopePercent) > _maxTrendSlopePercent)
            return;

        var rejectionCandle = candles[index];
        if (rejectionCandle.Close <= 0m || rangeLow <= 0m)
            return;

        var breakdownThreshold = rangeLow * (1m - _maxBreakdownBelowRangeLowPercent / 100m);
        if (rejectionCandle.Close < breakdownThreshold || rejectionCandle.Low < breakdownThreshold)
            return;

        var distanceToRangeLowPercent = (rejectionCandle.Close - rangeLow) / rejectionCandle.Close * 100m;
        if (distanceToRangeLowPercent < 0m || distanceToRangeLowPercent > _maxDistanceFromRangeLowPercent)
            return;

        var candleRange = rejectionCandle.High - rejectionCandle.Low;
        if (candleRange <= 0m)
            return;

        var bodyBottom = Math.Min(rejectionCandle.Open, rejectionCandle.Close);
        var lowerWick = bodyBottom - rejectionCandle.Low;
        var rejectionWickPercent = lowerWick / candleRange * 100m;
        var rejectionBodyPercent = Math.Abs(rejectionCandle.Close - rejectionCandle.Open) / candleRange * 100m;
        var bullishRejection = rejectionCandle.Close >= rejectionCandle.Open || rejectionWickPercent >= _minRejectionWickPercent;
        if (!bullishRejection)
            return;

        var closeBackInsideRange = rejectionCandle.Close > rangeLow && rejectionCandle.Close < rangeHigh;
        if (!closeBackInsideRange)
            return;

        var rangeMidpoint = (rangeHigh + rangeLow) / 2m;
        var distanceToRangeHighPercent = (rangeHigh - rejectionCandle.Close) / rejectionCandle.Close * 100m;

        _pending = new PendingRangeBounceV1(
            index,
            rangeHigh,
            rangeLow,
            rangeMidpoint,
            rangeWidthPercent,
            trendSlopePercent,
            distanceToRangeLowPercent,
            distanceToRangeHighPercent,
            rejectionBodyPercent,
            rejectionWickPercent,
            closeBackInsideRange,
            rejectionCandle.Open,
            rejectionCandle.Close,
            rejectionCandle.High,
            rejectionCandle.Low);
    }

    private (decimal? MovePercent, decimal TargetPrice, MeanReversionRangeBounceV1TargetModelName TargetModel)
        ResolveTargetMovePercent(PendingRangeBounceV1 pending, decimal entryPrice, decimal atrPercent)
    {
        decimal targetPrice = _targetModelName switch
        {
            MeanReversionRangeBounceV1TargetModelName.RangeSixtyPercentTarget =>
                pending.RangeLow + ((pending.RangeHigh - pending.RangeLow) * _rangeSixtyPercentFraction),
            MeanReversionRangeBounceV1TargetModelName.RangeHighTarget => pending.RangeHigh,
            MeanReversionRangeBounceV1TargetModelName.AtrLimitedTarget =>
                entryPrice + (entryPrice * atrPercent * _atrLimitedMultiplier / 100m),
            _ => pending.RangeMidpoint
        };

        if (_targetModelName == MeanReversionRangeBounceV1TargetModelName.AtrLimitedTarget)
        {
            var midpointPrice = pending.RangeMidpoint;
            if (midpointPrice > entryPrice)
                targetPrice = Math.Min(targetPrice, midpointPrice);
        }

        if (targetPrice <= entryPrice)
            return (null, targetPrice, _targetModelName);

        var movePercent = Math.Round((targetPrice - entryPrice) / entryPrice * 100m, 6);
        return (movePercent, targetPrice, _targetModelName);
    }

    private decimal ResolveStopPrice(PendingRangeBounceV1 pending)
        => _stopMode == MeanReversionRangeBounceV1StopModeName.RejectionCandleLow
            ? pending.RejectionLow
            : pending.RangeLow * (1m - _stopBufferBelowRangeLowPercent / 100m);

    private static bool TryComputeRangeMetrics(
        IReadOnlyList<KlineCandle> candles,
        int rangeStart,
        int rangeEnd,
        out decimal rangeHigh,
        out decimal rangeLow,
        out decimal rangeWidthPercent,
        out decimal trendSlopePercent)
    {
        rangeHigh = decimal.MinValue;
        rangeLow = decimal.MaxValue;
        for (var i = rangeStart; i <= rangeEnd; i++)
        {
            rangeHigh = Math.Max(rangeHigh, candles[i].High);
            rangeLow = Math.Min(rangeLow, candles[i].Low);
        }

        if (rangeHigh <= 0m || rangeLow <= 0m || rangeHigh <= rangeLow)
        {
            rangeWidthPercent = 0m;
            trendSlopePercent = 0m;
            return false;
        }

        var mid = (rangeHigh + rangeLow) / 2m;
        rangeWidthPercent = Math.Round((rangeHigh - rangeLow) / mid * 100m, 6);
        var startClose = candles[rangeStart].Close;
        var endClose = candles[rangeEnd].Close;
        trendSlopePercent = startClose > 0m
            ? Math.Round((endClose - startClose) / startClose * 100m, 6)
            : 0m;
        return true;
    }

    private MeanReversionRangeBounceV1Diagnostics BuildDiagnostics(
        PendingRangeBounceV1 pending,
        KlineCandle candle)
        => new()
        {
            RangeHigh = pending.RangeHigh,
            RangeLow = pending.RangeLow,
            RangeMidpoint = pending.RangeMidpoint,
            RangeWidthPercent = pending.RangeWidthPercent,
            DistanceToRangeLowPercent = pending.DistanceToRangeLowPercent,
            DistanceToRangeHighPercent = pending.DistanceToRangeHighPercent,
            EntryRejectionCandleBodyPercent = pending.RejectionBodyPercent,
            EntryRejectionWickPercent = pending.RejectionWickPercent,
            CloseBackInsideRange = pending.CloseBackInsideRange,
            TrendSlopePercent = pending.TrendSlopePercent
        };

    private static decimal ComputeSma(IReadOnlyList<KlineCandle> candles, int index, int period)
    {
        if (index < period - 1)
            return 0m;
        decimal sum = 0m;
        for (var i = index - period + 1; i <= index; i++)
            sum += candles[i].Close;
        return sum / period;
    }

    private decimal ComputeAtrPercent(IReadOnlyList<KlineCandle> candles, int index)
    {
        var atr = ComputeAtr(candles, index, _atrPeriod);
        var close = candles[index].Close;
        return close > 0m ? Math.Round(atr / close * 100m, 6) : 0m;
    }

    private static decimal ComputeAtr(IReadOnlyList<KlineCandle> candles, int lastIndexInclusive, int period)
    {
        if (lastIndexInclusive < 1)
            return 0m;

        var trueRanges = new List<decimal>();
        var start = Math.Max(1, lastIndexInclusive - period + 1);
        for (var i = start; i <= lastIndexInclusive; i++)
        {
            var highLow = candles[i].High - candles[i].Low;
            var highPrevClose = Math.Abs(candles[i].High - candles[i - 1].Close);
            var lowPrevClose = Math.Abs(candles[i].Low - candles[i - 1].Close);
            trueRanges.Add(Math.Max(highLow, Math.Max(highPrevClose, lowPrevClose)));
        }

        return trueRanges.Count == 0 ? 0m : trueRanges.Average();
    }

    private static MeanReversionRangeBounceV1TargetModelName ParseTargetModel(string? raw)
        => Enum.TryParse<MeanReversionRangeBounceV1TargetModelName>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : MeanReversionRangeBounceV1TargetModelName.RangeMidpointTarget;

    private static MeanReversionRangeBounceV1StopModeName ParseStopMode(string? raw)
        => Enum.TryParse<MeanReversionRangeBounceV1StopModeName>(raw, ignoreCase: true, out var parsed)
            ? parsed
            : MeanReversionRangeBounceV1StopModeName.RangeLowBuffer;

    private sealed record PendingRangeBounceV1(
        int RejectionIndex,
        decimal RangeHigh,
        decimal RangeLow,
        decimal RangeMidpoint,
        decimal RangeWidthPercent,
        decimal TrendSlopePercent,
        decimal DistanceToRangeLowPercent,
        decimal DistanceToRangeHighPercent,
        decimal RejectionBodyPercent,
        decimal RejectionWickPercent,
        bool CloseBackInsideRange,
        decimal RejectionOpen,
        decimal RejectionClose,
        decimal RejectionHigh,
        decimal RejectionLow);
}

public sealed record MeanReversionRangeBounceV1Diagnostics
{
    public decimal RangeHigh { get; init; }
    public decimal RangeLow { get; init; }
    public decimal RangeMidpoint { get; init; }
    public decimal RangeWidthPercent { get; init; }
    public decimal DistanceToRangeLowPercent { get; init; }
    public decimal DistanceToRangeHighPercent { get; init; }
    public decimal EntryRejectionCandleBodyPercent { get; init; }
    public decimal EntryRejectionWickPercent { get; init; }
    public bool CloseBackInsideRange { get; init; }
    public bool FollowThroughConfirmed { get; init; }
    public decimal TrendSlopePercent { get; init; }
    public decimal AtrPercent { get; init; }
    public string? TargetModelName { get; init; }
    public decimal? ExpectedMovePercent { get; init; }
    public decimal? EstimatedRoundTripCostPercent { get; init; }
    public decimal? RequiredNetProfitPercent { get; init; }
    public decimal? RequiredGrossMovePercent { get; init; }
    public decimal? StopDistancePercent { get; init; }
    public decimal? RewardRisk { get; init; }
    public string? RejectionReason { get; init; }
}

public sealed record MeanReversionRangeBounceV1StepResult
{
    public MeanReversionRangeBounceV1StepKind Kind { get; init; }
    public StrategySignalResult Signal { get; init; } = new() { Signal = TradeSignal.Hold };
    public MeanReversionRangeBounceV1Diagnostics Diagnostics { get; init; } = new();
    public string? RejectionReason { get; init; }

    public static MeanReversionRangeBounceV1StepResult NoAction()
        => new() { Kind = MeanReversionRangeBounceV1StepKind.NoAction };

    public static MeanReversionRangeBounceV1StepResult Entry(StrategySignalResult signal, MeanReversionRangeBounceV1Diagnostics diagnostics)
        => new() { Kind = MeanReversionRangeBounceV1StepKind.Entry, Signal = signal, Diagnostics = diagnostics };

    public static MeanReversionRangeBounceV1StepResult Blocked(string reason, MeanReversionRangeBounceV1Diagnostics diagnostics)
        => new() { Kind = MeanReversionRangeBounceV1StepKind.Blocked, RejectionReason = reason, Diagnostics = diagnostics with { RejectionReason = reason } };
}

public enum MeanReversionRangeBounceV1StepKind
{
    NoAction,
    Blocked,
    Entry
}
