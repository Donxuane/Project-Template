using Microsoft.Extensions.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Models.Decision;

namespace TradingBot.Backtest;

public enum RangeExpansionTargetModelName
{
    RangeWidthExtension,
    AtrLimitedExtension,
    IntervalCappedExtension
}

public sealed class RangeExpansionBreakoutV1Model
{
    public const string GuardPrefix = "RangeExpansion:";
    public const string InsufficientHistory = GuardPrefix + "InsufficientHistory";
    public const string RangeNotCompressed = GuardPrefix + "RangeNotCompressed";
    public const string AtrNotCompressed = GuardPrefix + "AtrNotCompressed";
    public const string NoBreakoutClose = GuardPrefix + "NoBreakoutClose";
    public const string FollowThroughFailed = GuardPrefix + "FollowThroughFailed";
    public const string AntiChaseRangeHighExceeded = GuardPrefix + "AntiChaseRangeHighExceeded";
    public const string AntiChaseBreakoutDistanceExceeded = GuardPrefix + "AntiChaseBreakoutDistanceExceeded";
    public const string TargetTooSmall = GuardPrefix + "TargetTooSmall";
    public const string TargetTooSmallButNetTradable = GuardPrefix + "TargetTooSmallButNetTradable";
    public const string TargetTooSmallAndFeeUntradable = GuardPrefix + "TargetTooSmallAndFeeUntradable";
    public const string LockDistanceExceeded = GuardPrefix + "LockDistanceExceeded";
    public const string ExperimentalMaeRiskProxyExceeded = GuardPrefix + "Experimental:MaeRiskProxyExceeded";
    public const string ExperimentalBreakoutBodyTooWeak = GuardPrefix + "Experimental:BreakoutBodyTooWeak";
    public const string ExperimentalFollowThroughWickOnly = GuardPrefix + "Experimental:FollowThroughWickOnly";
    public const string ExperimentalBreakoutCandleRangeTooLarge = GuardPrefix + "Experimental:BreakoutCandleRangeTooLarge";
    public const string ExperimentalLock90BelowCostBuffer = GuardPrefix + "Experimental:Lock90BelowCostBuffer";

    private readonly bool _enabled;
    private readonly int _rangeLookbackCandles;
    private readonly decimal _breakoutBufferPercent;
    private readonly decimal _maxDistanceFromRangeHighPercent;
    private readonly decimal _maxDistanceFromBreakoutPercent;
    private readonly bool _enableAtrCompressionCheck;
    private readonly int _atrPeriod;
    private readonly int _atrLongPeriod;
    private readonly decimal _atrCompressionRatio;
    private readonly decimal _rangeWidthTargetMultiplier;
    private readonly decimal _atrTargetMultiplier;
    private readonly decimal _minExpectedMovePercent;
    private readonly RangeExpansionTargetFloorMode _targetFloorMode;
    private readonly decimal? _minLockNetProfitPercent;
    private readonly decimal? _minGrossLockDistancePercent;
    private readonly Dictionary<string, decimal> _maxRangeWidthPercentByInterval;
    private readonly Dictionary<string, decimal> _maxExpectedMovePercentByInterval;
    private readonly Dictionary<string, decimal> _maxLockDistancePercentByInterval;
    private readonly bool _experimentalFiltersEnabled;
    private readonly decimal? _tighterAntiChaseCapPercent;
    private readonly decimal _requireBreakoutBodyStrengthPercent;
    private readonly bool _requireFollowThroughCloseAboveBreakoutHigh;
    private readonly decimal _maxBreakoutCandleRangeToAtrRatio;
    private readonly decimal _maxMaeRiskProxyPercent;
    private readonly decimal _minLock90DistanceAboveCostBufferPercent;
    private readonly decimal _roundTripCostBufferPercent;

    private PendingBreakout? _pending;

    public RangeExpansionBreakoutV1Model(IConfiguration configuration)
    {
        _enabled = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV1:Enabled") ?? false;
        _rangeLookbackCandles = Math.Max(3, configuration.GetValue<int?>("Backtest:RangeExpansionBreakoutV1:RangeLookbackCandles") ?? 12);
        _breakoutBufferPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:BreakoutBufferPercent") ?? 0.02m);
        _maxDistanceFromRangeHighPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:MaxDistanceFromRangeHighPercent") ?? 0.25m);
        _maxDistanceFromBreakoutPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:MaxDistanceFromBreakoutPercent") ?? 0.15m);
        _enableAtrCompressionCheck = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV1:EnableAtrCompressionCheck") ?? true;
        _atrPeriod = Math.Max(2, configuration.GetValue<int?>("Backtest:RangeExpansionBreakoutV1:AtrPeriod") ?? 14);
        _atrLongPeriod = Math.Max(_atrPeriod + 1, configuration.GetValue<int?>("Backtest:RangeExpansionBreakoutV1:AtrLongPeriod") ?? 28);
        _atrCompressionRatio = Math.Clamp(configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:AtrCompressionRatio") ?? 0.85m, 0.1m, 1m);
        _rangeWidthTargetMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:RangeWidthTargetMultiplier") ?? 1.0m);
        _atrTargetMultiplier = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:AtrTargetMultiplier") ?? 1.0m);
        _minExpectedMovePercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:MinExpectedMovePercent") ?? 0.10m);
        _targetFloorMode = RangeExpansionCostModel.ParseTargetFloorMode(configuration);
        _minLockNetProfitPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:MinLockNetProfitPercent");
        _minGrossLockDistancePercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:MinGrossLockDistancePercent");
        _maxRangeWidthPercentByInterval = ReadIntervalMap(configuration, "MaxRangeWidthPercentByInterval", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 0.35m,
            ["3m"] = 0.50m,
            ["5m"] = 0.55m
        });
        _maxExpectedMovePercentByInterval = ReadIntervalMap(configuration, "MaxExpectedMovePercentByInterval", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 0.50m,
            ["3m"] = 0.65m,
            ["5m"] = 0.70m
        });
        _maxLockDistancePercentByInterval = ReadIntervalMap(configuration, "MaxLockDistancePercentByInterval", new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["1m"] = 0.45m,
            ["3m"] = 0.60m,
            ["5m"] = 0.65m
        });
        _experimentalFiltersEnabled = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:Enabled") ?? false;
        _tighterAntiChaseCapPercent = configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:TighterAntiChaseCapPercent");
        _requireBreakoutBodyStrengthPercent = Math.Clamp(
            configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RequireBreakoutBodyStrengthPercent") ?? 0m,
            0m,
            100m);
        _requireFollowThroughCloseAboveBreakoutHigh = configuration.GetValue<bool?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RequireFollowThroughCloseAboveBreakoutHigh") ?? false;
        _maxBreakoutCandleRangeToAtrRatio = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:MaxBreakoutCandleRangeToAtrRatio") ?? 0m);
        _maxMaeRiskProxyPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:MaxMaeRiskProxyPercent") ?? 0m);
        _minLock90DistanceAboveCostBufferPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:MinLock90DistanceAboveCostBufferPercent") ?? 0m);
        _roundTripCostBufferPercent = Math.Max(0m, configuration.GetValue<decimal?>("Backtest:RangeExpansionBreakoutV1:ExperimentalFilters:RoundTripCostBufferPercent") ?? 0.20m);
    }

    public bool IsEnabled => _enabled;

    public int MinRequiredCandles => _rangeLookbackCandles + 2;

    public void Reset() => _pending = null;

    public RangeExpansionBreakoutStepResult ProcessCandle(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        TradingSymbol symbol,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        if (!_enabled || index < 0 || index >= candles.Count)
            return RangeExpansionBreakoutStepResult.NoAction();

        if (_pending is not null)
        {
            var followThroughResult = EvaluateFollowThrough(
                candles,
                index,
                interval,
                profitLockThresholdPercent,
                executionCosts,
                configuration);
            if (followThroughResult is not null)
                return followThroughResult;
        }

        if (_pending is null)
            TryDetectBreakout(candles, index, interval);

        return RangeExpansionBreakoutStepResult.NoAction();
    }

    private RangeExpansionBreakoutStepResult? EvaluateFollowThrough(
        IReadOnlyList<KlineCandle> candles,
        int index,
        string interval,
        decimal? profitLockThresholdPercent,
        ExecutionCostSettings executionCosts,
        IConfiguration configuration)
    {
        var pending = _pending!;
        if (index != pending.BreakoutIndex + 1)
        {
            _pending = null;
            return RangeExpansionBreakoutStepResult.Blocked(
                FollowThroughFailed,
                BuildDiagnostics(pending, candles[index], interval, profitLockThresholdPercent) with
                {
                    BreakoutConfirmed = true,
                    FollowThroughConfirmed = false,
                    RejectionReason = FollowThroughFailed
                });
        }

        var breakoutCandle = candles[pending.BreakoutIndex];
        var followCandle = candles[index];
        var followThroughConfirmed = _requireFollowThroughCloseAboveBreakoutHigh
            ? followCandle.Close > breakoutCandle.High
            : followCandle.Close > breakoutCandle.Close || followCandle.Close > breakoutCandle.High;

        var diagnosticsBase = BuildDiagnostics(pending, followCandle, interval, profitLockThresholdPercent) with
        {
            BreakoutConfirmed = true,
            FollowThroughConfirmed = followThroughConfirmed
        };

        _pending = null;

        if (!followThroughConfirmed)
        {
            var reason = _experimentalFiltersEnabled && _requireFollowThroughCloseAboveBreakoutHigh
                && followCandle.Close > breakoutCandle.Close
                && followCandle.Close <= breakoutCandle.High
                ? ExperimentalFollowThroughWickOnly
                : FollowThroughFailed;
            return RangeExpansionBreakoutStepResult.Blocked(
                reason,
                diagnosticsBase with { RejectionReason = reason });
        }

        var effectiveAntiChaseBreakoutCap = _experimentalFiltersEnabled && _tighterAntiChaseCapPercent.HasValue
            ? Math.Min(_maxDistanceFromBreakoutPercent, _tighterAntiChaseCapPercent.Value)
            : _maxDistanceFromBreakoutPercent;
        var effectiveAntiChaseRangeCap = _experimentalFiltersEnabled && _tighterAntiChaseCapPercent.HasValue
            ? Math.Min(_maxDistanceFromRangeHighPercent, _tighterAntiChaseCapPercent.Value + 0.05m)
            : _maxDistanceFromRangeHighPercent;

        var entryPrice = followCandle.Close;
        if (entryPrice <= 0m)
        {
            return RangeExpansionBreakoutStepResult.Blocked(
                TargetTooSmall,
                diagnosticsBase with { RejectionReason = TargetTooSmall });
        }

        var distanceFromRangeHighPercent = (entryPrice - pending.RangeHigh) / entryPrice * 100m;
        if (distanceFromRangeHighPercent > effectiveAntiChaseRangeCap)
        {
            return RangeExpansionBreakoutStepResult.Blocked(
                AntiChaseRangeHighExceeded,
                diagnosticsBase with
                {
                    DistanceFromBreakoutPercent = (entryPrice - pending.BreakoutClose) / entryPrice * 100m,
                    RejectionReason = AntiChaseRangeHighExceeded
                });
        }

        var distanceFromBreakoutPercent = (entryPrice - pending.BreakoutClose) / entryPrice * 100m;
        if (distanceFromBreakoutPercent > effectiveAntiChaseBreakoutCap)
        {
            return RangeExpansionBreakoutStepResult.Blocked(
                AntiChaseBreakoutDistanceExceeded,
                diagnosticsBase with
                {
                    DistanceFromBreakoutPercent = distanceFromBreakoutPercent,
                    RejectionReason = AntiChaseBreakoutDistanceExceeded
                });
        }

        var (expectedMovePercent, targetModelName, targetWasCapped, capReason) =
            ResolveTargetMovePercent(pending, candles, index, interval, entryPrice);

        diagnosticsBase = diagnosticsBase with
        {
            DistanceFromBreakoutPercent = distanceFromBreakoutPercent,
            ExpectedMovePercent = expectedMovePercent,
            TargetModelName = targetModelName.ToString(),
            TargetWasCapped = targetWasCapped,
            CapReason = capReason
        };

        var lock90 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 90m);
        var lock95 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 95m);
        var lock98 = CandidateForwardOutcomeAnalyzer.ComputeLockDistance(expectedMovePercent, 98m);
        var maxLockDistance = ResolveIntervalCap(_maxLockDistancePercentByInterval, interval, 0.45m);
        var activeLockDistance = profitLockThresholdPercent switch
        {
            90m => lock90,
            95m => lock95,
            98m => lock98,
            _ => lock90
        };

        diagnosticsBase = diagnosticsBase with
        {
            Lock90DistancePercent = lock90,
            Lock95DistancePercent = lock95,
            Lock98DistancePercent = lock98
        };

        var requiredNet = _minLockNetProfitPercent
            ?? RangeExpansionCostModel.ResolveRequiredNetProfitPercent(configuration);
        var roundTrip = RangeExpansionCostModel.ComputeRoundTripCostPercent(executionCosts);
        var requiredGross = _minGrossLockDistancePercent ?? (roundTrip + requiredNet);
        var costMetrics = RangeExpansionCostModel.Compute(
            executionCosts,
            configuration,
            expectedMovePercent,
            lock90,
            lock95,
            lock98,
            forwardMfe60Percent: null,
            lock90ReachableWithin60m: false,
            lock95ReachableWithin60m: false,
            lock98ReachableWithin60m: false,
            profitLockThresholdPercent);

        if (_targetFloorMode == RangeExpansionTargetFloorMode.CostAware)
        {
            if (activeLockDistance is null or <= 0m || activeLockDistance.Value < requiredGross)
            {
                var reason = RangeExpansionCostModel.ClassifyTargetTooSmallRejection(costMetrics, lock90);
                return RangeExpansionBreakoutStepResult.Blocked(
                    reason,
                    diagnosticsBase with { RejectionReason = reason });
            }
        }
        else
        {
            var minExpectedMove = RangeExpansionCostModel.ResolveMinExpectedMovePercent(configuration, _targetFloorMode);
            if (expectedMovePercent < minExpectedMove)
            {
                var reason = RangeExpansionCostModel.ClassifyTargetTooSmallRejection(costMetrics, lock90);
                return RangeExpansionBreakoutStepResult.Blocked(
                    reason,
                    diagnosticsBase with { RejectionReason = reason });
            }
        }

        if (_experimentalFiltersEnabled && _maxMaeRiskProxyPercent > 0m)
        {
            var maeRiskProxy = pending.RangeWidthPercent + distanceFromBreakoutPercent;
            if (maeRiskProxy > _maxMaeRiskProxyPercent)
            {
                return RangeExpansionBreakoutStepResult.Blocked(
                    ExperimentalMaeRiskProxyExceeded,
                    diagnosticsBase with { RejectionReason = ExperimentalMaeRiskProxyExceeded });
            }
        }

        if (activeLockDistance is > 0m && activeLockDistance.Value > maxLockDistance)
        {
            return RangeExpansionBreakoutStepResult.Blocked(
                LockDistanceExceeded,
                diagnosticsBase with { RejectionReason = LockDistanceExceeded });
        }

        if (_experimentalFiltersEnabled
            && _minLock90DistanceAboveCostBufferPercent > 0m
            && lock90 is > 0m
            && lock90.Value < _roundTripCostBufferPercent + _minLock90DistanceAboveCostBufferPercent)
        {
            return RangeExpansionBreakoutStepResult.Blocked(
                ExperimentalLock90BelowCostBuffer,
                diagnosticsBase with { RejectionReason = ExperimentalLock90BelowCostBuffer });
        }

        var targetPrice = entryPrice + (entryPrice * expectedMovePercent / 100m);
        var distanceToInvalidationPercent = pending.RangeLow > 0m
            ? (entryPrice - pending.RangeLow) / entryPrice * 100m
            : (decimal?)null;

        var signal = new StrategySignalResult
        {
            StrategyName = "RangeExpansionBreakoutV1",
            Signal = TradeSignal.Buy,
            Reason = "Range expansion breakout with follow-through confirmed.",
            Confidence = 0.75m,
            ExpectedTargetPrice = targetPrice,
            ExpectedMovePercent = expectedMovePercent,
            ExpectedTargetSource = "RangeExpansionBreakoutV1." + targetModelName,
            BreakoutRangeHigh = pending.RangeHigh,
            BreakoutRangeLow = pending.RangeLow,
            BreakoutThresholdPrice = pending.BreakoutThresholdPrice,
            DistanceToInvalidationPercent = distanceToInvalidationPercent,
            ProjectionMode = targetModelName.ToString(),
            VolatilityRegime = "Normal"
        };

        return RangeExpansionBreakoutStepResult.Entry(
            signal,
            diagnosticsBase with { RejectionReason = null });
    }

    private void TryDetectBreakout(IReadOnlyList<KlineCandle> candles, int index, string interval)
    {
        if (index < _rangeLookbackCandles)
            return;

        var rangeStart = index - _rangeLookbackCandles;
        var rangeEnd = index - 1;
        if (rangeStart < 0 || rangeEnd < rangeStart)
            return;

        decimal rangeHigh = decimal.MinValue;
        decimal rangeLow = decimal.MaxValue;
        for (var i = rangeStart; i <= rangeEnd; i++)
        {
            rangeHigh = Math.Max(rangeHigh, candles[i].High);
            rangeLow = Math.Min(rangeLow, candles[i].Low);
        }

        if (rangeHigh <= 0m || rangeLow <= 0m || rangeHigh <= rangeLow)
            return;

        var mid = (rangeHigh + rangeLow) / 2m;
        var rangeWidthPercent = (rangeHigh - rangeLow) / mid * 100m;
        var maxRangeWidth = ResolveIntervalCap(_maxRangeWidthPercentByInterval, interval, 0.35m);
        if (rangeWidthPercent > maxRangeWidth)
            return;

        if (_enableAtrCompressionCheck && !IsAtrCompressed(candles, index))
            return;

        var breakoutCandle = candles[index];
        var threshold = rangeHigh * (1m + _breakoutBufferPercent / 100m);
        if (breakoutCandle.Close <= threshold)
            return;

        var atrPercent = ComputeAtrPercent(candles, index);
        var breakoutCloseAboveRangePercent = rangeHigh > 0m ? (breakoutCandle.Close - rangeHigh) / rangeHigh * 100m : 0m;
        var candleRange = breakoutCandle.High - breakoutCandle.Low;
        var breakoutCandleRangePercent = breakoutCandle.Close > 0m ? candleRange / breakoutCandle.Close * 100m : 0m;
        var body = Math.Abs(breakoutCandle.Close - breakoutCandle.Open);
        var breakoutBodyStrengthPercent = candleRange > 0m ? body / candleRange * 100m : 0m;

        if (_experimentalFiltersEnabled)
        {
            if (_requireBreakoutBodyStrengthPercent > 0m && breakoutBodyStrengthPercent < _requireBreakoutBodyStrengthPercent)
                return;

            if (_maxBreakoutCandleRangeToAtrRatio > 0m
                && atrPercent > 0m
                && breakoutCandleRangePercent / atrPercent > _maxBreakoutCandleRangeToAtrRatio)
                return;
        }

        _pending = new PendingBreakout(
            index,
            rangeHigh,
            rangeLow,
            rangeWidthPercent,
            breakoutCandle.Close,
            breakoutCandle.High,
            threshold,
            atrPercent,
            breakoutCloseAboveRangePercent,
            breakoutBodyStrengthPercent,
            breakoutCandleRangePercent);
    }

    private (decimal MovePercent, RangeExpansionTargetModelName TargetModel, bool TargetWasCapped, string? CapReason)
        ResolveTargetMovePercent(
            PendingBreakout pending,
            IReadOnlyList<KlineCandle> candles,
            int index,
            string interval,
            decimal entryPrice)
    {
        var rangeMove = pending.RangeWidthPercent * _rangeWidthTargetMultiplier;
        var atrMove = pending.AtrPercent * _atrTargetMultiplier;
        var proposed = Math.Max(rangeMove, _minExpectedMovePercent);
        if (atrMove > 0m)
            proposed = Math.Min(proposed, atrMove);

        var targetModel = RangeExpansionTargetModelName.RangeWidthExtension;
        string? capReason = null;
        var wasCapped = false;

        var intervalCap = ResolveIntervalCap(_maxExpectedMovePercentByInterval, interval, 0.50m);
        if (proposed > intervalCap)
        {
            proposed = intervalCap;
            targetModel = RangeExpansionTargetModelName.IntervalCappedExtension;
            capReason = "IntervalCappedExtension";
            wasCapped = true;
        }
        else if (atrMove > 0m && rangeMove > atrMove)
        {
            targetModel = RangeExpansionTargetModelName.AtrLimitedExtension;
            capReason = "AtrLimitedExtension";
            wasCapped = true;
        }

        return (Math.Round(proposed, 6), targetModel, wasCapped, capReason);
    }

    private RangeExpansionBreakoutDiagnostics BuildDiagnostics(
        PendingBreakout pending,
        KlineCandle candle,
        string interval,
        decimal? profitLockThresholdPercent)
    {
        return new RangeExpansionBreakoutDiagnostics
        {
            RangeHigh = pending.RangeHigh,
            RangeLow = pending.RangeLow,
            RangeWidthPercent = pending.RangeWidthPercent,
            BreakoutBufferPercent = _breakoutBufferPercent,
            BreakoutClose = pending.BreakoutClose,
            BreakoutConfirmed = true,
            AtrPercent = pending.AtrPercent,
            BreakoutCloseAboveRangePercent = pending.BreakoutCloseAboveRangePercent,
            BreakoutBodyStrengthPercent = pending.BreakoutBodyStrengthPercent,
            BreakoutCandleRangePercent = pending.BreakoutCandleRangePercent,
            CandidateAgeCandles = 1,
            MaxAllowedLockDistancePercent = ResolveIntervalCap(_maxLockDistancePercentByInterval, interval, 0.45m)
        };
    }

    private bool IsAtrCompressed(IReadOnlyList<KlineCandle> candles, int index)
    {
        var shortAtr = ComputeAtr(candles, index, _atrPeriod);
        var longAtr = ComputeAtr(candles, index, _atrLongPeriod);
        if (shortAtr <= 0m || longAtr <= 0m)
            return true;

        return shortAtr / longAtr <= _atrCompressionRatio;
    }

    private decimal ComputeAtrPercent(IReadOnlyList<KlineCandle> candles, int index)
    {
        var close = candles[index].Close;
        if (close <= 0m)
            return 0m;

        return ComputeAtr(candles, index, _atrPeriod) / close * 100m;
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

    private static decimal ResolveIntervalCap(IReadOnlyDictionary<string, decimal> map, string interval, decimal fallback)
        => map.TryGetValue(interval, out var value) ? value : fallback;

    private static Dictionary<string, decimal> ReadIntervalMap(
        IConfiguration configuration,
        string suffix,
        Dictionary<string, decimal> defaults)
    {
        foreach (var interval in new[] { "1m", "3m", "5m" })
        {
            var configured = configuration.GetValue<decimal?>($"Backtest:RangeExpansionBreakoutV1:{suffix}:{interval}");
            if (configured.HasValue)
                defaults[interval] = configured.Value;
        }

        return defaults;
    }

    private sealed record PendingBreakout(
        int BreakoutIndex,
        decimal RangeHigh,
        decimal RangeLow,
        decimal RangeWidthPercent,
        decimal BreakoutClose,
        decimal BreakoutHigh,
        decimal BreakoutThresholdPrice,
        decimal AtrPercent,
        decimal BreakoutCloseAboveRangePercent,
        decimal BreakoutBodyStrengthPercent,
        decimal BreakoutCandleRangePercent);
}

public sealed record RangeExpansionBreakoutDiagnostics
{
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
    public string? RejectionReason { get; init; }
    public decimal? MaxAllowedLockDistancePercent { get; init; }
    public decimal? BreakoutCloseAboveRangePercent { get; init; }
    public decimal? BreakoutBodyStrengthPercent { get; init; }
    public decimal? BreakoutCandleRangePercent { get; init; }
    public int CandidateAgeCandles { get; init; } = 1;
}

public sealed record RangeExpansionBreakoutStepResult
{
    public RangeExpansionStepKind Kind { get; init; }
    public StrategySignalResult Signal { get; init; } = new() { Signal = TradeSignal.Hold };
    public RangeExpansionBreakoutDiagnostics Diagnostics { get; init; } = new();
    public string? RejectionReason { get; init; }

    public static RangeExpansionBreakoutStepResult NoAction()
        => new() { Kind = RangeExpansionStepKind.NoAction };

    public static RangeExpansionBreakoutStepResult Entry(StrategySignalResult signal, RangeExpansionBreakoutDiagnostics diagnostics)
        => new() { Kind = RangeExpansionStepKind.Entry, Signal = signal, Diagnostics = diagnostics };

    public static RangeExpansionBreakoutStepResult Blocked(string reason, RangeExpansionBreakoutDiagnostics diagnostics)
        => new() { Kind = RangeExpansionStepKind.Blocked, RejectionReason = reason, Diagnostics = diagnostics with { RejectionReason = reason } };
}

public enum RangeExpansionStepKind
{
    NoAction,
    Blocked,
    Entry
}
